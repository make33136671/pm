using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;
using FreeSql;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Sb.Apps.Pm.Services.Pipelines
{
    [AutoDependencyInjection]
    public class GitWebhookService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<GitWebhookService> _logger;
        private readonly PipelineProfileService _profileService;

        public GitWebhookService(IFreeSql fsql, ILogger<GitWebhookService> logger,
            PipelineProfileService profileService)
        {
            _fsql = fsql;
            _logger = logger;
            _profileService = profileService;
        }

        public async Task<Result> ProcessGitLabWebhook(int repoId, string payload)
        {
            var repo = _fsql.Select<GitRepository>().Where(r => r.Id == repoId).First();
            if (repo == null) return Result.Fail("Repository not found");

            var json = JObject.Parse(payload);
            var eventType = json["object_kind"]?.ToString() ?? json["event_name"]?.ToString() ?? "unknown";

            var log = new GitWebhookLog
            {
                RepositoryId = repoId,
                EventType = eventType,
                Payload = payload,
            };

            // Extract info based on event type
            switch (eventType)
            {
                case "push":
                    log.Branch = json["ref"]?.ToString()?.Replace("refs/heads/", "");
                    log.CommitSha = json["after"]?.ToString();
                    log.AuthorName = json["user_name"]?.ToString();
                    var commits = json["commits"] as JArray;
                    log.Summary = commits?.Count > 0 ? $"{commits.Count} commit(s): {commits[0]["message"]}" : "Push event";
                    break;
                case "merge_request":
                    var mr = json["object_attributes"];
                    log.Branch = mr?["source_branch"]?.ToString();
                    log.AuthorName = json["user"]?["name"]?.ToString();
                    log.Summary = $"MR: {mr?["title"]}";
                    break;
                case "pipeline":
                    var pipeline = json["object_attributes"];
                    log.Branch = pipeline?["ref"]?.ToString();
                    log.CommitSha = pipeline?["sha"]?.ToString();
                    log.Summary = $"Pipeline {pipeline?["id"]} - {pipeline?["status"]}";
                    // Also upsert GitPipeline record
                    await UpsertPipeline(repoId, pipeline);
                    // Sync Pipeline status to BuildRecord
                    await SyncBuildRecordFromPipeline(pipeline);
                    break;
                case "tag_push":
                    log.Branch = json["ref"]?.ToString()?.Replace("refs/tags/", "");
                    log.CommitSha = json["after"]?.ToString();
                    log.AuthorName = json["user_name"]?.ToString();
                    log.Summary = $"Tag: {log.Branch}";
                    break;
            }

            _fsql.Insert(log).ExecuteAffrows();
            return Result.Success("Webhook processed");
        }

        public async Task<Result> ProcessGitHubWebhook(int repoId, string eventType, string payload)
        {
            var repo = _fsql.Select<GitRepository>().Where(r => r.Id == repoId).First();
            if (repo == null) return Result.Fail("Repository not found");

            var json = JObject.Parse(payload);
            var log = new GitWebhookLog
            {
                RepositoryId = repoId,
                EventType = eventType,
                Payload = payload,
            };

            switch (eventType)
            {
                case "push":
                    log.Branch = json["ref"]?.ToString()?.Replace("refs/heads/", "");
                    log.CommitSha = json["after"]?.ToString();
                    log.AuthorName = json["pusher"]?["name"]?.ToString();
                    var commits = json["commits"] as JArray;
                    log.Summary = commits?.Count > 0 ? $"{commits.Count} commit(s): {commits[0]["message"]}" : "Push event";
                    break;
                case "pull_request":
                    var pr = json["pull_request"];
                    log.Branch = pr?["head"]?["ref"]?.ToString();
                    log.AuthorName = json["sender"]?["login"]?.ToString();
                    log.Summary = $"PR: {pr?["title"]}";
                    break;
                case "workflow_run":
                    var run = json["workflow_run"];
                    log.Branch = run?["head_branch"]?.ToString();
                    log.CommitSha = run?["head_sha"]?.ToString();
                    log.Summary = $"Workflow {run?["name"]} - {run?["conclusion"]}";
                    break;
            }

            _fsql.Insert(log).ExecuteAffrows();
            return Result.Success("Webhook processed");
        }

        private async Task UpsertPipeline(int repoId, JToken? pipelineData)
        {
            if (pipelineData == null) return;
            var pipelineId = pipelineData["id"]?.ToString();
            if (string.IsNullOrEmpty(pipelineId)) return;

            var existing = _fsql.Select<GitPipeline>().Where(p => p.RepositoryId == repoId && p.PipelineId == pipelineId).First();

            var statusStr = pipelineData["status"]?.ToString() ?? "Pending";
            if (!Enum.TryParse<PipelineStatus>(statusStr, true, out var status))
                status = PipelineStatus.Pending;

            if (existing != null)
            {
                existing.Status = status;
                existing.FinishedAt = pipelineData["finished_at"]?.ToObject<DateTime?>();
                var duration = pipelineData["duration"]?.ToObject<int?>();
                if (duration.HasValue) existing.Duration = duration;
                _fsql.Update<GitPipeline>().SetSource(existing).ExecuteAffrows();
            }
            else
            {
                var pipeline = new GitPipeline
                {
                    RepositoryId = repoId,
                    PipelineId = pipelineId,
                    Branch = pipelineData["ref"]?.ToString(),
                    CommitSha = pipelineData["sha"]?.ToString(),
                    Status = status,
                    StartedAt = pipelineData["created_at"]?.ToObject<DateTime?>(),
                };
                _fsql.Insert(pipeline).ExecuteAffrows();
            }
        }

        /// <summary>
        /// Pipeline 状态回调时同步更新关联的 BuildRecord
        /// </summary>
        private async Task SyncBuildRecordFromPipeline(JToken? pipelineData)
        {
            if (pipelineData == null) return;
            var gitlabPipelineId = pipelineData["id"]?.ToObject<int?>();
            if (!gitlabPipelineId.HasValue) return;

            var buildRecord = _fsql.Select<BuildRecord>()
                .Where(b => b.GitLabPipelineId == gitlabPipelineId.Value && b.Status == BuildStatus.Building)
                .First();
            if (buildRecord == null) return;

            var statusStr = pipelineData["status"]?.ToString()?.ToLower();
            var changed = false;

            switch (statusStr)
            {
                case "success":
                    buildRecord.Status = BuildStatus.Success;
                    buildRecord.FinishedAt = DateTime.Now;
                    buildRecord.CommitSha = pipelineData["sha"]?.ToString();
                    if (buildRecord.StartedAt.HasValue)
                        buildRecord.Duration = (int)(DateTime.Now - buildRecord.StartedAt.Value).TotalSeconds;
                    changed = true;
                    break;
                case "failed":
                    buildRecord.Status = BuildStatus.Failed;
                    buildRecord.FinishedAt = DateTime.Now;
                    if (buildRecord.StartedAt.HasValue)
                        buildRecord.Duration = (int)(DateTime.Now - buildRecord.StartedAt.Value).TotalSeconds;
                    changed = true;
                    break;
            }

            if (!changed) return;

            _fsql.Update<BuildRecord>().SetSource(buildRecord).ExecuteAffrows();
            _logger.LogInformation("BuildRecord #{Id} 状态更新: {Status} (Pipeline #{PipelineId})",
                buildRecord.Id, buildRecord.Status, gitlabPipelineId);

            // 构建成功 + AutoDeployOnSuccess → 自动提交部署
            if (buildRecord.Status == BuildStatus.Success && buildRecord.PipelineProfileId.HasValue)
            {
                var profile = _fsql.Select<PipelineProfile>()
                    .Where(p => p.Id == buildRecord.PipelineProfileId.Value)
                    .First();
                if (profile is { AutoDeployOnSuccess: true, DeployMode: not DeployMode.None })
                {
                    try
                    {
                        // 真正建发布并执行部署(RequireApproval=true 时仅挂起待审批)
                        await _profileService.AutoDeployAfterBuild(buildRecord.Id);
                        _logger.LogInformation("BuildRecord #{Id} 构建成功,自动进入部署", buildRecord.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BuildRecord #{Id} 自动部署失败", buildRecord.Id);
                    }
                }
            }
        }
    }
}

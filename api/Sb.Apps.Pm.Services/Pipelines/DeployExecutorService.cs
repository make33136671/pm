using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using SB.Framework.Core.Util;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Pipelines
{
    /// <summary>
    /// SbpmDeploy 部署执行器
    /// 从 GitLab 下载构建产物，根据 ProjectServer 的 DeployMethod 分发部署
    /// </summary>
    [AutoDependencyInjection]
    public class DeployExecutorService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<DeployExecutorService> _logger;
        private readonly GitLabApiService _gitLabApiService;
        private readonly DeployJobService _deployJobService;
        private readonly SystemSettingService _settingService;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

        public DeployExecutorService(
            IFreeSql fsql,
            ILogger<DeployExecutorService> logger,
            GitLabApiService gitLabApiService,
            DeployJobService deployJobService,
            SystemSettingService settingService)
        {
            _fsql = fsql;
            _logger = logger;
            _gitLabApiService = gitLabApiService;
            _deployJobService = deployJobService;
            _settingService = settingService;
        }

        /// <summary>
        /// 执行 SbpmDeploy：下载产物 → 按服务器逐个部署
        /// </summary>
        public async Task ExecuteAsync(int releaseId)
        {
            var release = _fsql.Select<ReleaseRecord>().Where(r => r.Id == releaseId).First();
            if (release == null || release.Status != ReleaseStatus.Deploying) return;

            var build = _fsql.Select<BuildRecord>().Where(b => b.Id == release.BuildRecordId).First();
            if (build == null) { FailRelease(release, "关联构建记录不存在"); return; }

            // 不要内联 .Include(GitRepository):FreeSql 3.5.x 对 PipelineProfile 内联 Include 会把
            // 父实体标量字段读错位(详见 PipelineProfileService.LoadNavs 注释)。改为分开查再拼接。
            var profile = build.PipelineProfileId.HasValue
                ? _fsql.Select<PipelineProfile>().Where(p => p.Id == build.PipelineProfileId.Value).First()
                : null;
            if (profile == null) { FailRelease(release, "关联配置不存在"); return; }
            profile.GitRepository = _fsql.Select<GitRepository>().Where(g => g.Id == profile.GitRepositoryId).First();

            var serverIds = profile.DeployServerIds ?? new List<int>();
            if (serverIds.Count == 0) { FailRelease(release, "未配置目标服务器"); return; }

            var projectServers = _fsql.Select<ProjectServer>()
                .Where(ps => serverIds.Contains(ps.Id))
                .ToList();
            if (projectServers.Count == 0) { FailRelease(release, "目标服务器不存在"); return; }
            // Server 导航单独查再拼接:不要内联 .Include(ProjectServer)(会把 ServerId/BuiltInApiKey 等读错位,
            // 详见 ProjectServerService.LoadNavs 的 FreeSql 物化 bug 注释)
            var srvIds = projectServers.Select(ps => ps.ServerId).Distinct().ToList();
            var srvMap = _fsql.Select<Server>().Where(s => srvIds.Contains(s.Id)).ToList().ToDictionary(s => s.Id);
            foreach (var ps in projectServers)
                ps.Server = srvMap.TryGetValue(ps.ServerId, out var s) ? s : null;

            var logs = new List<string>();
            var allSuccess = true;
            logs.Add($"[策略] {profile.DeployStrategy} / 产物来源 {profile.BuildSource}");

            // 取得产物:本机构建产物优先(build.ArtifactPath),否则从 GitLab 下载
            string? artifactPath = null;
            var artifactIsTemp = false;   // 临时产物部署完删除;非临时(外部指定)保留
            try
            {
                if (!string.IsNullOrEmpty(build.ArtifactPath) && File.Exists(build.ArtifactPath))
                {
                    artifactPath = build.ArtifactPath;
                    artifactIsTemp = artifactPath.Contains("sb_pm_artifacts");
                    logs.Add($"[OK] 使用本机产物: {artifactPath}");
                }
                else
                {
                    if (profile.GitRepository == null)
                        throw new Exception("无本机产物且未关联 GitLab 仓库");
                    artifactPath = await DownloadArtifact(profile, build);
                    artifactIsTemp = true;
                    logs.Add($"[OK] 产物下载完成: {artifactPath}");
                }
            }
            catch (Exception ex)
            {
                FailRelease(release, $"获取产物失败: {ex.Message}");
                return;
            }

            // 逐个服务器部署
            foreach (var ps in projectServers)
            {
                var method = ps.PreferredDeployMethod ?? DeployMethod.Manual;
                var serverName = ps.Server?.Name ?? ps.Server?.Host ?? $"Server#{ps.ServerId}";
                logs.Add($"\n--- 部署到 {serverName} ({method}) ---");

                try
                {
                    switch (method)
                    {
                        case DeployMethod.BuiltInApi:
                            await DeployViaBuiltInApi(ps, artifactPath!, build, logs);
                            break;
                        case DeployMethod.SSH:
                            logs.Add("[SKIP] SSH 部署尚未实现，请手动部署");
                            break;
                        case DeployMethod.FTP:
                            logs.Add("[SKIP] FTP 部署尚未实现，请手动部署");
                            break;
                        case DeployMethod.BtPanel:
                            logs.Add("[SKIP] 宝塔面板部署尚未实现，请手动部署");
                            break;
                        default:
                            logs.Add("[SKIP] 手动部署模式，请手动操作");
                            break;
                    }
                    logs.Add($"[OK] {serverName} 部署完成");
                }
                catch (Exception ex)
                {
                    logs.Add($"[FAIL] {serverName} 部署失败: {ex.Message}");
                    allSuccess = false;
                }
            }

            // 备份产物 — 通过 DeployJob 归档(SbpmDeploy 走 SSH 部署,不通过 framework Deploy 流程,
            // 这里直接以 Deployed 状态插一条 archive job;同 target 旧 Deployed job 自动转 RolledBack)
            if (artifactPath != null && File.Exists(artifactPath))
            {
                try
                {
                    var archiveJob = await _deployJobService.CreateFromFile(
                        $"profile_{profile.Id}",
                        profile.Name,
                        artifactPath,
                        Path.GetFileName(artifactPath),
                        new FileInfo(artifactPath).Length,
                        release.OperatorName ?? "system"
                    );
                    await _deployJobService.MarkDeployed(archiveJob.Id, $"profile_{profile.Id}");
                    logs.Add("[OK] 产物已备份");
                }
                catch (Exception ex)
                {
                    logs.Add($"[WARN] 备份失败: {ex.Message}");
                }
            }

            // 更新发布状态
            release.Status = allSuccess ? ReleaseStatus.Success : ReleaseStatus.Failed;
            release.DeployLog = string.Join("\n", logs);
            release.FinishedAt = DateTime.Now;
            _fsql.Update<ReleaseRecord>().SetSource(release).ExecuteAffrows();

            _logger.LogInformation("Release #{Id} 部署{Result}", releaseId, allSuccess ? "成功" : "失败");

            // 清理临时文件(仅临时产物;外部指定的产物保留)
            if (artifactIsTemp && artifactPath != null && File.Exists(artifactPath))
            {
                try { File.Delete(artifactPath); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// 从 GitLab 下载构建产物（Job Artifact）
        /// </summary>
        private async Task<string> DownloadArtifact(PipelineProfile profile, BuildRecord build)
        {
            var repoId = profile.GitRepository!.RepoId!;
            var jobName = profile.ArtifactJobName ?? "build";
            var branch = build.TriggerRef ?? profile.DefaultBranch;

            var baseUrl = _settingService.GetSystemSettingByKey<string>("gitlab_url")!.TrimEnd('/');
            var token = _settingService.GetSystemSettingByKey<string>("gitlab_token")!;

            // 下载最新成功 Job 的 Artifact
            var url = $"{baseUrl}/api/v4/projects/{Uri.EscapeDataString(repoId)}/jobs/artifacts/{Uri.EscapeDataString(branch)}/download?job={Uri.EscapeDataString(jobName)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("PRIVATE-TOKEN", token);

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"下载失败: HTTP {(int)response.StatusCode}");

            var tempDir = Path.Combine(Path.GetTempPath(), "sb_pm_artifacts");
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, $"artifact_{build.Id}_{DateTime.Now:yyyyMMddHHmmss}.zip");

            await using var fs = File.Create(tempFile);
            await response.Content.CopyToAsync(fs);

            return tempFile;
        }

        /// <summary>
        /// 通过 SB 框架内置 API 部署
        /// </summary>
        private async Task DeployViaBuiltInApi(ProjectServer ps, string artifactPath, BuildRecord build, List<string> logs)
        {
            if (string.IsNullOrEmpty(ps.BuiltInApiUrl))
                throw new Exception("未配置 BuiltInApiUrl");

            var apiKey = ps.BuiltInApiKey;
            if (!string.IsNullOrEmpty(apiKey) && apiKey != "******")
            {
                try { apiKey = EncryptionUtil.AesDecrypt(apiKey); } catch { /* use raw */ }
            }

            var url = ps.BuiltInApiUrl.TrimEnd('/');
            logs.Add($"调用 BuiltInApi: {url}");

            // 上传 zip 产物
            using var form = new MultipartFormDataContent();
            form.Add(new StreamContent(File.OpenRead(artifactPath)), "file", Path.GetFileName(artifactPath));

            // 新访问令牌系统:旧静态 X-Master-Key 通道已废,改用 Authorization: Bearer <sk_ key>。
            // BuiltInApiKey 须存目标侧签发的 key(部署目标 UploadProject 未声明 [Scope],需 FullAccess key 或密码 JWT 才放行)。
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"BuiltInApi 返回 {(int)response.StatusCode}: {body}");

            logs.Add($"BuiltInApi 响应: {body[..Math.Min(body.Length, 200)]}");
        }

        private void FailRelease(ReleaseRecord release, string reason)
        {
            release.Status = ReleaseStatus.Failed;
            release.DeployLog = reason;
            release.FinishedAt = DateTime.Now;
            _fsql.Update<ReleaseRecord>().SetSource(release).ExecuteAffrows();
            _logger.LogWarning("Release #{Id} 部署失败: {Reason}", release.Id, reason);
        }
    }
}

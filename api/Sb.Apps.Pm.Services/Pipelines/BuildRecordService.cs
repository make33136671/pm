using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;
using Newtonsoft.Json;

namespace Sb.Apps.Pm.Services.Pipelines
{
    [AutoDependencyInjection]
    public class BuildRecordService : BaseCrudService<BuildRecord>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "构建发布";
        public int InitOrder => 130;
        public Type[] GetEntityTypes() => new[]
        {
            typeof(BuildRecord), typeof(ReleaseRecord),
            typeof(ReleaseBackup), typeof(ApprovalRecord),
        };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                Name = "BuildRelease", Path = "/build-release", Component = "BasicLayout",
                Redirect = "/build-release/build", Title = "构建与发布",
                Icon = "lucide:rocket", OrderNo = 5,
                Children = new()
                {
                    new() { Name = "BuildList", Path = "/build-release/build", Component = "/build/list/index", Title = "构建记录", Icon = "lucide:hammer", OrderNo = 1 },
                    new() { Name = "ReleaseList", Path = "/build-release/release", Component = "/release/list/index", Title = "发布管理", Icon = "lucide:upload", OrderNo = 2 },
                    new() { Name = "BackupList", Path = "/build-release/backup", Component = "/release/backup/index", Title = "备份管理", Icon = "lucide:archive", OrderNo = 3 },
                }
            }
        };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly GitLabApiService _gitlab;

        public BuildRecordService(IFreeSql fsql, ILogger<BuildRecordService> logger,
            IHttpContextAccessor httpContextAccessor, GitLabApiService gitlab) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
            _gitlab = gitlab;
        }

        /// <summary>
        /// 从构建记录解析出 GitLab projectId(RepoId) 与 pipelineId，供 CI 查询复用。
        /// </summary>
        private (BuildRecord record, string repoId, int pipelineId) ResolveCi(int buildId)
        {
            var record = Select.Where(c => c.Id == buildId).First();
            if (record == null)
                throw new InvalidOperationException("构建记录不存在！");
            if (record.GitLabPipelineId is null or 0)
                throw new InvalidOperationException("该构建未关联 GitLab Pipeline（可能尚未触发或非 CI 构建）。");
            // PipelineProfile + 其 GitRepository 单独查再拼接:不要内联 .Include(c => c.PipelineProfile.GitRepository)
            // (经 join 进 PipelineProfile 会触发 FreeSql 物化 bug,详见 PipelineProfileService.LoadNavs)
            if (record.PipelineProfileId.HasValue)
            {
                record.PipelineProfile = _fsql.Select<PipelineProfile>()
                    .Where(p => p.Id == record.PipelineProfileId.Value).First();
                if (record.PipelineProfile != null)
                    record.PipelineProfile.GitRepository = _fsql.Select<GitRepository>()
                        .Where(g => g.Id == record.PipelineProfile.GitRepositoryId).First();
            }
            var repoId = record.PipelineProfile?.GitRepository?.RepoId;
            if (string.IsNullOrEmpty(repoId))
                throw new InvalidOperationException("未找到关联的 GitLab 仓库 RepoId，请先在仓库绑定中配置。");
            return (record, repoId, record.GitLabPipelineId.Value);
        }

        public Result GetList(int? projectId = null, BuildStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(status.HasValue, c => c.Status == status.Value)
                .Include(c => c.Project);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(BuildRecord record)
        {
            record.CreatedTime = DateTime.Now;
            record.StartedAt = DateTime.Now;
            record.Status = BuildStatus.Building;
            record.OperatorName = _httpContextAccessor.HttpContext?.User?.Username();
            record = this.Insert(record);
            return Result.Success("构建记录创建成功!", record);
        }

        public Result UpdateStatus(int id, BuildStatus status, string? buildLog = null)
        {
            var record = Select.Where(c => c.Id == id).First();
            if (record == null) return Result.Fail("构建记录不存在!");

            record.Status = status;
            if (buildLog != null) record.BuildLog = buildLog;
            if (status == BuildStatus.Success || status == BuildStatus.Failed)
            {
                record.FinishedAt = DateTime.Now;
                if (record.StartedAt.HasValue)
                    record.Duration = (int)(DateTime.Now - record.StartedAt.Value).TotalSeconds;
            }

            _fsql.Update<BuildRecord>().SetSource(record).ExecuteAffrows();
            return Result.Success("状态更新成功!");
        }

        #region CI 流水线 / 测试

        /// <summary>
        /// 获取构建对应 Pipeline 的所有 Job（含状态、stage），供 UI 轮询。
        /// </summary>
        public async Task<Result> GetJobs(int buildId)
        {
            try
            {
                var (_, repoId, pipelineId) = ResolveCi(buildId);
                var jobs = await _gitlab.GetPipelineJobs(repoId, pipelineId);
                return Result.Success(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetJobs 失败 buildId={BuildId}", buildId);
                return Result.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 获取某个 Job 的控制台日志(trace)。
        /// </summary>
        public async Task<Result> GetJobTrace(int buildId, int jobId)
        {
            try
            {
                var (_, repoId, _) = ResolveCi(buildId);
                var trace = await _gitlab.GetJobTrace(repoId, jobId);
                return Result.Success(new { jobId, trace });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetJobTrace 失败 buildId={BuildId} jobId={JobId}", buildId, jobId);
                return Result.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 获取 Pipeline 的 JUnit 测试报告，并把汇总缓存进 TestSummary。
        /// refresh=false 时若已有缓存直接返回缓存汇总（明细仍实时拉）。
        /// </summary>
        public async Task<Result> GetTestReport(int buildId, bool refresh = false)
        {
            try
            {
                var (record, repoId, pipelineId) = ResolveCi(buildId);
                object report;
                try
                {
                    report = await _gitlab.GetPipelineTestReport(repoId, pipelineId);
                }
                catch (HttpRequestException)
                {
                    return Result.Fail("测试报告尚未生成或该 Pipeline 未配置 JUnit 报告（artifacts:reports:junit）。");
                }

                // 缓存汇总（不含逐条用例明细），失败不阻断返回
                try
                {
                    var json = JsonConvert.SerializeObject(report);
                    var obj = JsonConvert.DeserializeObject<dynamic>(json);
                    record.TestSummary = JsonConvert.SerializeObject(new
                    {
                        totalCount = (int?)obj.totalCount,
                        successCount = (int?)obj.successCount,
                        failedCount = (int?)obj.failedCount,
                        skippedCount = (int?)obj.skippedCount,
                        totalTime = (double?)obj.totalTime,
                        cachedAt = DateTime.Now,
                    });
                    _fsql.Update<BuildRecord>().Set(c => c.TestSummary, record.TestSummary)
                        .Where(c => c.Id == buildId).ExecuteAffrows();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "缓存 TestSummary 失败 buildId={BuildId}", buildId);
                }

                return Result.Success(report);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetTestReport 失败 buildId={BuildId}", buildId);
                return Result.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 把本构建的 CI 结果关联到需求 / 任务（任一可空，传 null 表示解除关联）。
        /// </summary>
        public Result LinkTarget(int buildId, int? requirementId, int? taskItemId)
        {
            var record = Select.Where(c => c.Id == buildId).First();
            if (record == null) return Result.Fail("构建记录不存在！");

            _fsql.Update<BuildRecord>()
                .Set(c => c.RequirementId, requirementId)
                .Set(c => c.TaskItemId, taskItemId)
                .Where(c => c.Id == buildId)
                .ExecuteAffrows();

            return Result.Success("关联已更新", new { buildId, requirementId, taskItemId });
        }

        #endregion
    }
}

using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class DashboardService
    {
        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DashboardService(IFreeSql fsql, IHttpContextAccessor httpContextAccessor)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        public Result Workbench()
        {
            var activeProjects = _fsql.Select<Project>().Where(c => c.Status == ProjectStatus.Active).Count();
            var todoReqs = _fsql.Select<Requirement>().Where(c => c.Status == RequirementStatus.Todo || c.Status == RequirementStatus.InProgress).Count();
            var todayDone = _fsql.Select<Requirement>().Where(c => c.Status == RequirementStatus.Done && c.CompletedDate >= DateTime.Today).Count();
            var pendingApproval = _fsql.Select<ReleaseRecord>().Where(c => c.ApprovalStatus == ApprovalStatus.Pending).Count();

            var recentActivities = _fsql.Select<ProjectActivity>()
                .OrderByDescending(c => c.Id)
                .Limit(10)
                .ToList(c => new { c.ProjectId, c.Action, c.Summary, c.OperatorName, c.CreatedTime });

            // 健康度排名
            var projects = _fsql.Select<Project>().Where(c => c.Status == ProjectStatus.Active).ToList();
            var healthRanking = projects.Select(p =>
            {
                var score = CalculateHealthScore(p.Id);
                return new { p.Id, p.Name, Score = score.score };
            }).OrderByDescending(x => x.Score).Take(10).ToList();

            // 优化建议
            var suggestions = projects.SelectMany(p => GenerateSuggestions(p.Id, p.Name)).Take(10).ToList();

            return Result.Success(new
            {
                stats = new { activeProjects, todoReqs, todayDone, pendingApproval },
                recentActivities,
                healthRanking,
                suggestions
            });
        }

        public Result TodoList(int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<Requirement>()
                .Where(c => c.Status == RequirementStatus.Todo || c.Status == RequirementStatus.InProgress)
                .OrderBy(c => c.DueDate);

            var data = query.Count(out long total).Page(page, pageSize)
                .ToList(c => new { c.Id, c.ProjectId, c.Title, c.Status, c.Assignee, c.DueDate });

            return Result.Page(data, total);
        }

        public Result CompletedList(int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<Requirement>()
                .Where(c => c.Status == RequirementStatus.Done)
                .OrderByDescending(c => c.CompletedDate);

            var data = query.Count(out long total).Page(page, pageSize)
                .ToList(c => new { c.Id, c.ProjectId, c.Title, c.CompletedDate });

            return Result.Page(data, total);
        }

        public Result HealthScore(int projectId)
        {
            var project = _fsql.Select<Project>().Where(c => c.Id == projectId).First();
            if (project == null) return Result.Fail("项目不存在!");

            var (score, details) = CalculateHealthScore(projectId);
            var suggestions = GenerateSuggestions(projectId, project.Name);

            string level = score switch
            {
                >= 90 => "优秀",
                >= 70 => "良好",
                >= 50 => "一般",
                _ => "需关注"
            };

            return Result.Success(new { score, level, details, suggestions });
        }

        public Result OptimizationSuggestions()
        {
            var projects = _fsql.Select<Project>().Where(c => c.Status == ProjectStatus.Active).ToList();
            var suggestions = projects.SelectMany(p => GenerateSuggestions(p.Id, p.Name)).ToList();
            return Result.Success(suggestions);
        }

        private (int score, object details) CalculateHealthScore(int projectId)
        {
            // 需求完成率 (25%)
            var reqTotal = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId).Count();
            var reqDone = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId && c.Status == RequirementStatus.Done).Count();
            double reqRate = reqTotal > 0 ? (double)reqDone / reqTotal : 0;

            // 需求逾期率 (20%)
            var reqInProgress = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId &&
                (c.Status == RequirementStatus.InProgress || c.Status == RequirementStatus.Todo)).Count();
            var reqOverdue = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId &&
                (c.Status == RequirementStatus.InProgress || c.Status == RequirementStatus.Todo) &&
                c.DueDate < DateTime.Now && c.DueDate != null).Count();
            double overdueRate = reqInProgress > 0 ? (double)reqOverdue / reqInProgress : 0;

            // 构建成功率 (15%)
            var buildTotal = _fsql.Select<BuildRecord>().Where(c => c.ProjectId == projectId).Count();
            var buildSuccess = _fsql.Select<BuildRecord>().Where(c => c.ProjectId == projectId && c.Status == BuildStatus.Success).Count();
            double buildRate = buildTotal > 0 ? (double)buildSuccess / buildTotal : 1;

            // CI/CD 稳定性 - 近7天 (15%)
            var pipelineTotal = _fsql.Select<GitPipeline>()
                .InnerJoin<GitRepository>((p, r) => p.RepositoryId == r.Id && r.ProjectId == projectId)
                .Where(c => c.CreatedTime >= DateTime.Now.AddDays(-7)).Count();
            var pipelineSuccess = _fsql.Select<GitPipeline>()
                .InnerJoin<GitRepository>((p, r) => p.RepositoryId == r.Id && r.ProjectId == projectId)
                .Where(c => c.CreatedTime >= DateTime.Now.AddDays(-7) && c.Status == PipelineStatus.Success).Count();
            double pipelineRate = pipelineTotal > 0 ? (double)pipelineSuccess / pipelineTotal : 1;

            // 活跃度 - 7天内活动 (10%)
            var recentActivity = _fsql.Select<ProjectActivity>()
                .Where(c => c.ProjectId == projectId && c.CreatedTime >= DateTime.Now.AddDays(-7)).Any();
            double activeScore = recentActivity ? 1.0 : 0;

            // 文档完整度 (10%)
            var hasDoc = _fsql.Select<ProjectDocument>().Where(c => c.ProjectId == projectId).Any();
            var hasConfig = _fsql.Select<ProjectConfig>().Where(c => c.ProjectId == projectId).Any();
            var hasDb = _fsql.Select<DbMigrationScript>().Where(c => c.ProjectId == projectId).Any();
            double docScore = ((hasDoc ? 1 : 0) + (hasConfig ? 1 : 0) + (hasDb ? 1 : 0)) / 3.0;

            // 备份覆盖率 (5%)
            var hasBackup = _fsql.Select<ReleaseBackup>().Where(c => c.ProjectId == projectId &&
                c.CreatedTime >= DateTime.Now.AddDays(-30)).Any();
            double backupScore = hasBackup ? 1.0 : 0;

            int total = (int)(
                reqRate * 25 +
                (1 - overdueRate) * 20 +
                buildRate * 15 +
                pipelineRate * 15 +
                activeScore * 10 +
                docScore * 10 +
                backupScore * 5
            );

            total = Math.Clamp(total, 0, 100);

            var details = new
            {
                requirementCompletion = (int)(reqRate * 100),
                overdueRate = (int)(overdueRate * 100),
                buildSuccessRate = (int)(buildRate * 100),
                pipelineStability = (int)(pipelineRate * 100),
                recentlyActive = recentActivity,
                documentCompleteness = (int)(docScore * 100),
                hasRecentBackup = hasBackup
            };

            return (total, details);
        }

        private List<object> GenerateSuggestions(int projectId, string projectName)
        {
            var suggestions = new List<object>();

            // 需求逾期
            var overdueCount = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId &&
                (c.Status == RequirementStatus.InProgress || c.Status == RequirementStatus.Todo) &&
                c.DueDate < DateTime.Now && c.DueDate != null).Count();
            var inProgressCount = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId &&
                (c.Status == RequirementStatus.InProgress || c.Status == RequirementStatus.Todo)).Count();

            if (inProgressCount > 0 && (double)overdueCount / inProgressCount > 0.3)
                suggestions.Add(new { project = projectName, message = "需求逾期率过高，建议拆分大需求或调整截止日期", level = "warning" });

            // 构建连续失败
            var recentBuilds = _fsql.Select<BuildRecord>().Where(c => c.ProjectId == projectId)
                .OrderByDescending(c => c.Id).Limit(3).ToList();
            if (recentBuilds.Count >= 3 && recentBuilds.All(b => b.Status == BuildStatus.Failed))
                suggestions.Add(new { project = projectName, message = "构建连续失败，请检查 CI 配置", level = "error" });

            // 长期无更新
            var lastActivity = _fsql.Select<ProjectActivity>().Where(c => c.ProjectId == projectId)
                .OrderByDescending(c => c.Id).First();
            if (lastActivity == null || lastActivity.CreatedTime < DateTime.Now.AddDays(-30))
                suggestions.Add(new { project = projectName, message = "项目30天无更新，请确认是否需要归档", level = "info" });

            // 无备份
            var hasBackup = _fsql.Select<ReleaseBackup>().Where(c => c.ProjectId == projectId).Any();
            if (!hasBackup)
                suggestions.Add(new { project = projectName, message = "建议配置自动备份策略", level = "warning" });

            // 无文档
            var hasDoc = _fsql.Select<ProjectDocument>().Where(c => c.ProjectId == projectId).Any();
            if (!hasDoc)
                suggestions.Add(new { project = projectName, message = "建议补充项目文档", level = "info" });

            return suggestions;
        }
    }
}

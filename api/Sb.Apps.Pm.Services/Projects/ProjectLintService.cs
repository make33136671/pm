using FreeSql;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Tasks;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    /// <summary>
    /// 项目完整度校验(Lint)服务(#94)。
    /// 检查项目数据缺口:有需求无任务/任务无关联/任务无描述/需求无负责人/模块无任务。
    /// 解决"录入 PM 靠被提醒才补齐"的问题。
    /// </summary>
    [AutoDependencyInjection]
    public class ProjectLintService
    {
        private readonly IFreeSql _fsql;
        public ProjectLintService(IFreeSql fsql) => _fsql = fsql;

        public Result Lint(int projectId)
        {
            var issues = new List<LintIssue>();

            // 1) 有需求但无任务
            var reqIds = _fsql.Select<Requirement>()
                .Where(r => r.ProjectId == projectId && r.IsConfirmed && !r.IsDeleted)
                .ToList(r => r.Id);

            var reqsWithTasks = _fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted && t.RequirementId != null)
                .Distinct().ToList(t => t.RequirementId!.Value);

            foreach (var rid in reqIds.Except(reqsWithTasks))
            {
                var r = _fsql.Select<Requirement>().Where(x => x.Id == rid).First();
                issues.Add(new LintIssue("NO_TASKS", "warning", $"需求 #{rid}「{r?.Title}」下没有任务"));
            }

            // 2) 任务未挂功能模块
            var tasksNoModule = _fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted && t.FeatureModuleId == null)
                .ToList(t => new { t.Id, t.Title });
            foreach (var t in tasksNoModule)
                issues.Add(new LintIssue("MISSING_MODULE", "error", $"任务 #{t.Id}「{t.Title}」未挂功能模块"));

            // 3) 任务无描述
            var tasksNoContent = _fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted &&
                       (t.Content == null || t.Content == ""))
                .ToList(t => new { t.Id, t.Title });
            foreach (var t in tasksNoContent)
                issues.Add(new LintIssue("MISSING_CONTENT", "warning", $"任务 #{t.Id}「{t.Title}」描述为空"));

            // 4) 需求无负责人
            var reqsNoAssignee = _fsql.Select<Requirement>()
                .Where(r => r.ProjectId == projectId && r.IsConfirmed && !r.IsDeleted &&
                       (r.Assignee == null || r.Assignee == ""))
                .ToList(r => new { r.Id, r.Title });
            foreach (var r in reqsNoAssignee)
                issues.Add(new LintIssue("MISSING_ASSIGNEE", "info", $"需求 #{r.Id}「{r.Title}」无负责人"));

            // 5) 功能模块(子功能点)无关联任务
            var modules = _fsql.Select<FeatureModule>()
                .Where(m => m.ProjectId == projectId && m.ParentId != null && !m.IsDeleted)
                .ToList(m => new { m.Id, m.Name });
            var modulesWithTasks = _fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted && t.FeatureModuleId != null)
                .Distinct().ToList(t => t.FeatureModuleId!.Value);
            foreach (var m in modules.Where(m => !modulesWithTasks.Contains(m.Id)))
                issues.Add(new LintIssue("MODULE_NO_TASKS", "info", $"功能点 #{m.Id}「{m.Name}」无关联任务"));

            var errors = issues.Count(i => i.Level == "error");
            var warnings = issues.Count(i => i.Level == "warning");
            return Result.Success(new { issues, errors, warnings, total = issues.Count });
        }
    }

    public class LintIssue
    {
        public string Code { get; set; }
        public string Level { get; set; }   // error / warning / info
        public string Message { get; set; }

        public LintIssue(string code, string level, string message)
        {
            Code = code; Level = level; Message = message;
        }
    }
}

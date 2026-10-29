using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Tasks;
using Sb.Apps.Pm.Models.Requirements;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectDocumentService : BaseCrudService<ProjectDocument>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "文档管理";
        public int InitOrder => 160;
        public Type[] GetEntityTypes() => new[] { typeof(ProjectDocument), typeof(DocVersion) };
        public List<MenuDefinition> GetMenus() => new();
        #endregion

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IFreeSql _fsql;

        public ProjectDocumentService(IFreeSql fsql, ILogger<ProjectDocumentService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _fsql = fsql;
        }

        public Result GetList(int? projectId = null, string? category = null, string? keyword = null,
            string? metadataStatus = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(!string.IsNullOrEmpty(category), c => c.Category == category)
                .WhereIf(!string.IsNullOrEmpty(keyword), c => c.Title.Contains(keyword))
                .WhereIf(!string.IsNullOrEmpty(metadataStatus), c => c.Metadata != null && c.Metadata.Contains($"\"status\":\"{metadataStatus}\""))
                .Include(c => c.Project);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(ProjectDocument doc)
        {
            doc.CreatedTime = DateTime.Now;
            doc.OperatorName = _httpContextAccessor.HttpContext?.User?.Username();
            doc = this.Insert(doc);
            return Result.Success("文档创建成功!", doc);
        }

        public Result Edit(ProjectDocument doc)
        {
            doc.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Title, c.Category, c.Content, c.Metadata, c.Sort, c.UpdatedTime
            }).SetSource(doc).ExecuteAffrows();

            return count > 0 ? Result.Success("文档更新成功!") : Result.Fail("文档更新失败!");
        }

        /// <summary>AI 将项目需求/任务/模块状态写成 Markdown 快照文档存入资源管理(#84)。</summary>
        public Result SaveProjectSnapshot(int projectId, string title = "")
        {
            var project = _fsql.Select<Project>().Where(p => p.Id == projectId).First();
            if (project == null) return Result.Fail("项目不存在");

            var reqs = _fsql.Select<Requirement>()
                .Where(r => r.ProjectId == projectId && r.IsConfirmed && !r.IsDeleted)
                .ToList(r => new { r.Id, r.Title, r.Status, r.Complexity });

            var tasks = _fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted)
                .ToList(t => new { t.Id, t.Title, t.ExecStatus, t.Type });

            var now = DateTime.Now;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {project.Name} — 项目状态快照 {now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"\n**生成时间**: {now:yyyy-MM-dd HH:mm:ss}  \n");
            sb.AppendLine($"## 需求({reqs.Count} 条)");
            foreach (var r in reqs.Take(50))
                sb.AppendLine($"- [{r.Status}] #{r.Id} {r.Title} ({r.Complexity})");
            sb.AppendLine($"\n## 任务({tasks.Count} 条)");
            var byStatus = tasks.GroupBy(t => t.ExecStatus.ToString())
                .Select(g => $"{g.Key}: {g.Count()}").ToList();
            sb.AppendLine(string.Join(" | ", byStatus));
            var done = tasks.Count(t => t.ExecStatus == TaskItemExecStatus.Success);
            sb.AppendLine($"\n**整体完成率**: {done}/{tasks.Count} ({(tasks.Count > 0 ? done * 100 / tasks.Count : 0)}%)");

            var doc = new ProjectDocument
            {
                ProjectId = projectId,
                Title = string.IsNullOrEmpty(title) ? $"{project.Name} 状态快照 {now:yyyyMMdd}" : title,
                Category = "项目快照",
                Content = sb.ToString(),
                FileType = "markdown",
                Sort = 999,
                CreatedTime = now,
            };
            doc = this.Insert(doc);
            return Result.Success("快照已生成", doc);
        }

        /// <summary>生成审计报告文档(#85):项目做了什么、谁做的、何时、存在哪些问题。</summary>
        public Result GenerateAuditReport(int projectId)
        {
            var project = _fsql.Select<Project>().Where(p => p.Id == projectId).First();
            if (project == null) return Result.Fail("项目不存在");

            var tasks = _fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted)
                .OrderBy(t => t.CompletedDate)
                .ToList(t => new { t.Id, t.Title, t.Type, t.ExecStatus, t.Assignee, t.ActualHours, t.CompletedDate });

            var now = DateTime.Now;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {project.Name} — 审计报告 {now:yyyy-MM-dd}");
            sb.AppendLine($"\n**生成时间**: {now:yyyy-MM-dd HH:mm:ss}\n");
            sb.AppendLine("## 已完成工作");
            var done = tasks.Where(t => t.ExecStatus == TaskItemExecStatus.Success).ToList();
            foreach (var t in done.Take(100))
                sb.AppendLine($"- [{t.Type}] {t.Title} — {t.Assignee ?? "未分配"} ({t.ActualHours:F1}h) {t.CompletedDate:yyyy-MM-dd}");
            sb.AppendLine($"\n**总工时**: {done.Sum(t => t.ActualHours ?? 0):F1}h");
            sb.AppendLine("\n## 未完成/问题项");
            var issues = tasks.Where(t => t.ExecStatus is TaskItemExecStatus.Failed or TaskItemExecStatus.Idle).ToList();
            foreach (var t in issues.Take(50))
                sb.AppendLine($"- [{t.ExecStatus}] {t.Title}");
            var doc = new ProjectDocument
            {
                ProjectId = projectId,
                Title = $"{project.Name} 审计报告 {now:yyyyMMdd}",
                Category = "审计报告",
                Content = sb.ToString(),
                FileType = "markdown",
                Sort = 998,
                CreatedTime = now,
            };
            doc = this.Insert(doc);
            return Result.Success("审计报告已生成", doc);
        }
    }
}

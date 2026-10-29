using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Tasks;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectService : BaseCrudService<Project>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "项目管理";
        public int InitOrder => 100;
        public Type[] GetEntityTypes() => new[]
        {
            typeof(Project), typeof(ProjectEnvironment), typeof(SubProject),
            typeof(ProjectActivity), typeof(ProjectMilestone),
        };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                Name = "Project", Path = "/project", Component = "BasicLayout",
                Redirect = "/project/list", Title = "项目管理",
                Icon = "lucide:folder-kanban", OrderNo = 2,
                Children = new()
                {
                    new() { Name = "ProjectList", Path = "/project/list", Component = "/project/list/index", Title = "项目列表", Icon = "lucide:list", OrderNo = 1 },
                    new() { Name = "ProjectTemplate", Path = "/project/template", Component = "/project/template/index", Title = "项目模板", Icon = "lucide:layout-template", OrderNo = 2 },
                    new() { Name = "ProjectDetail", Path = "/project/detail", Component = "/project/detail/index", Title = "项目详情", OrderNo = 3, HideInMenu = true },
                    new() { Name = "ProjectForm", Path = "/project/form", Component = "/project/form/index", Title = "项目表单", OrderNo = 4, HideInMenu = true },
                }
            }
        };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly MindMapService _mindMapService;

        public ProjectService(IFreeSql fsql, ILogger<ProjectService> logger, IHttpContextAccessor httpContextAccessor, MindMapService mindMapService) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
            _mindMapService = mindMapService;
        }

        public Result GetList(string? keyword = null, ProjectStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select;
            query = query.WhereIf(!string.IsNullOrEmpty(keyword), c => c.Name.Contains(keyword) || c.Code.Contains(keyword));
            query = query.WhereIf(status.HasValue, c => c.Status == status.Value);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(Project project)
        {
            if (Select.Where(c => c.Code == project.Code && !string.IsNullOrEmpty(project.Code)).Any())
                return Result.Fail("项目代号已存在!");

            project.CreatedTime = DateTime.Now;
            project = this.Insert(project);

            // 自动创建主导图
            _mindMapService.CreateMainForProject(project.Id);

            return Result.Success("项目创建成功!", project);
        }

        public Result Edit(Project project)
        {
            if (Select.Where(c => c.Code == project.Code && c.Id != project.Id && !string.IsNullOrEmpty(project.Code)).Any())
                return Result.Fail("项目代号已存在!");

            project.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Name, c.Code, c.Description, c.LogoUrl, c.Status,
                c.Tags, c.PortRangeStart, c.PortRangeEnd,
                c.GitlabGroupId, c.GitlabGroupPath, c.GitlabGroupUrl,
                c.LocalPath, c.TemplateId,
                c.StartDate, c.DueDate, c.CompletedDate, c.Sort,
                c.UpdatedTime
            }).SetSource(project).ExecuteAffrows();

            return count > 0 ? Result.Success("项目更新成功!") : Result.Fail("项目更新失败!");
        }

        public Result GetListWithStats(string? keyword = null, ProjectStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select;
            query = query.WhereIf(!string.IsNullOrEmpty(keyword), c => c.Name.Contains(keyword) || c.Code.Contains(keyword));
            query = query.WhereIf(status.HasValue, c => c.Status == status.Value);
            query = ApplySorting(query, field, order);

            var projects = query.Count(out long dataCount).Page(page, pageSize).ToList();
            if (projects.Count == 0) return Result.Page(new List<object>(), dataCount);

            var projectIds = projects.Select(p => p.Id).ToList();

            // 需求统计：按项目分组
            // 注意:Requirement.Status 是 MapType=string(varchar)。FreeSql 在 GroupBy 的
            // Sum(case when 列 == 枚举常量 …) 聚合上下文里会丢失 MapType,把枚举退化成 int 序号
            // (生成 "Status" = 4),PG 下报 42883 varchar=integer。改为查回内存再用 C# 统计,绕开该翻译盲区。
            var reqStats = _fsql.Select<Requirement>()
                .Where(r => projectIds.Contains(r.ProjectId))
                .ToList(r => new { r.ProjectId, r.Status })
                .GroupBy(r => r.ProjectId)
                .ToDictionary(g => g.Key, g => new
                {
                    ProjectId = g.Key,
                    Total = g.Count(),
                    Done = g.Count(r => r.Status == RequirementStatus.Done),
                    InProgress = g.Count(r => r.Status == RequirementStatus.InProgress),
                    Testing = g.Count(r => r.Status == RequirementStatus.Testing),
                });

            // Git 仓库数量
            var gitCounts = _fsql.Select<GitRepository>()
                .Where(r => projectIds.Contains(r.ProjectId))
                .GroupBy(r => r.ProjectId)
                .ToList(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.ProjectId, x => x.Count);

            // 功能模块数量
            var featureModuleCounts = _fsql.Select<FeatureModule>()
                .Where(f => projectIds.Contains(f.ProjectId))
                .GroupBy(f => f.ProjectId)
                .ToList(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.ProjectId, x => x.Count);

            // 任务统计(同上:TaskItem.ExecStatus 是 MapType=string,聚合 enum 比较改内存统计避开 42883)
            var taskStats = _fsql.Select<TaskItem>()
                .Where(t => projectIds.Contains(t.ProjectId))
                .ToList(t => new { t.ProjectId, t.ExecStatus })
                .GroupBy(t => t.ProjectId)
                .ToDictionary(g => g.Key, g => new
                {
                    ProjectId = g.Key,
                    Total = g.Count(),
                    Done = g.Count(t => t.ExecStatus == TaskItemExecStatus.Success),
                });

            // 模板名称
            var templateIds = projects.Where(p => p.TemplateId.HasValue).Select(p => p.TemplateId!.Value).Distinct().ToList();
            var templateNames = templateIds.Count > 0
                ? _fsql.Select<ProjectTemplate>().Where(t => templateIds.Contains(t.Id)).ToList(t => new { t.Id, t.Name }).ToDictionary(t => t.Id, t => t.Name)
                : new Dictionary<int, string>();

            // 子项目数量
            var subProjectCounts = _fsql.Select<SubProject>()
                .Where(s => projectIds.Contains(s.ProjectId))
                .GroupBy(s => s.ProjectId)
                .ToList(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.ProjectId, x => x.Count);

            // 思维导图数量
            var mindMapCounts = _fsql.Select<MindMap>()
                .Where(m => projectIds.Contains(m.ProjectId))
                .GroupBy(m => m.ProjectId)
                .ToList(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.ProjectId, x => x.Count);

            // 最近 Git 活动时间（通过 GitRepository 关联）
            var repoIds = _fsql.Select<GitRepository>()
                .Where(r => projectIds.Contains(r.ProjectId))
                .ToList(r => new { r.Id, r.ProjectId });
            var repoIdToProject = repoIds.ToDictionary(r => r.Id, r => r.ProjectId);
            var allRepoIds = repoIds.Select(r => r.Id).ToList();

            var webhookDict = new Dictionary<int, DateTime>();
            if (allRepoIds.Count > 0)
            {
                var latestWebhooks = _fsql.Select<GitWebhookLog>()
                    .Where(w => allRepoIds.Contains(w.RepositoryId))
                    .GroupBy(w => w.RepositoryId)
                    .ToList(g => new
                    {
                        RepositoryId = g.Key,
                        LastTime = g.Max(g.Value.CreatedTime),
                    });

                foreach (var wh in latestWebhooks)
                {
                    if (repoIdToProject.TryGetValue(wh.RepositoryId, out var pid))
                    {
                        if (!webhookDict.ContainsKey(pid) || wh.LastTime > webhookDict[pid])
                            webhookDict[pid] = wh.LastTime;
                    }
                }
            }

            var items = projects.Select(p =>
            {
                reqStats.TryGetValue(p.Id, out var rs);
                gitCounts.TryGetValue(p.Id, out var gc);
                subProjectCounts.TryGetValue(p.Id, out var spc);
                mindMapCounts.TryGetValue(p.Id, out var mc);
                featureModuleCounts.TryGetValue(p.Id, out var fmc);
                taskStats.TryGetValue(p.Id, out var ts);
                webhookDict.TryGetValue(p.Id, out var lastGitTime);

                return new
                {
                    p.Id, p.Name, p.Code, p.Description, p.Status,
                    p.Tags, p.StartDate, p.DueDate,
                    p.GitlabGroupId, p.GitlabGroupPath, p.GitlabGroupUrl,
                    p.TemplateId, p.CreatedTime,
                    TemplateName = p.TemplateId.HasValue && templateNames.TryGetValue(p.TemplateId.Value, out var tn) ? tn : null,
                    Stats = new
                    {
                        FeatureModuleCount = fmc,
                        ReqTotal = rs?.Total ?? 0,
                        ReqDone = rs?.Done ?? 0,
                        ReqInProgress = rs?.InProgress ?? 0,
                        ReqTesting = rs?.Testing ?? 0,
                        TaskTotal = ts?.Total ?? 0,
                        TaskDone = ts?.Done ?? 0,
                        SubProjectCount = spc,
                        GitRepoCount = gc,
                        MindMapCount = mc,
                        LastGitActivity = lastGitTime == default ? (DateTime?)null : lastGitTime,
                    }
                };
            }).ToList();

            return Result.Page(items, dataCount);
        }

        public Result Overview(int projectId)
        {
            var project = Select.Where(c => c.Id == projectId).First();
            if (project == null) return Result.Fail("项目不存在!");

            var reqTotal = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId).Count();
            var reqDone = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId && (c.Status == RequirementStatus.Done || c.Status == RequirementStatus.Closed)).Count();
            var reqInProgress = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId && c.Status == RequirementStatus.InProgress).Count();
            var reqTesting = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId && c.Status == RequirementStatus.Testing).Count();
            var reqTodo = _fsql.Select<Requirement>().Where(c => c.ProjectId == projectId && (c.Status == RequirementStatus.Draft || c.Status == RequirementStatus.Todo)).Count();
            var envCount = _fsql.Select<ProjectEnvironment>().Where(c => c.ProjectId == projectId).Count();

            return Result.Success(new
            {
                project,
                stats = new { reqTotal, reqDone, reqInProgress, reqTesting, reqTodo, envCount }
            });
        }

        /// <summary>
        /// 功能地图从数据派生(#98)。
        /// 由 FeatureModule + TaskItem 数据自动生成 MindMap JSON 并写入(或更新)项目的 FeatureMap。
        /// </summary>
        public Result GenerateFeatureMap(int projectId)
        {
            var topModules = _fsql.Select<FeatureModule>()
                .Where(m => m.ProjectId == projectId && m.ParentId == null && !m.IsDeleted)
                .OrderBy(m => m.Sort).ToList();

            var children = _fsql.Select<FeatureModule>()
                .Where(m => m.ProjectId == projectId && m.ParentId != null && !m.IsDeleted)
                .OrderBy(m => m.Sort).ToList();

            var tasksByModule = _fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted && t.FeatureModuleId != null)
                .ToList().GroupBy(t => t.FeatureModuleId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            object MakeNode(string text, object? tag = null, object[]? kids = null)
            {
                var d = new Dictionary<string, object> { ["text"] = text };
                if (tag != null) d["tag"] = tag;
                return new { data = d, children = kids ?? Array.Empty<object>() };
            }

            var rootChildren = topModules.Select(m =>
            {
                var subs = children.Where(c => c.ParentId == m.Id).Select(sub =>
                {
                    var tasks = tasksByModule.GetValueOrDefault(sub.Id, new());
                    var doneCount = tasks.Count(t => t.ExecStatus == TaskItemExecStatus.Success);
                    var tag = tasks.Count > 0 ? new[] { $"{doneCount}/{tasks.Count}" } : null;
                    return MakeNode(sub.Name, tag);
                }).ToArray<object>();
                return MakeNode(m.Name, new[] { m.Description ?? "" }, subs);
            }).ToArray<object>();

            var project = _fsql.Select<Project>().Where(p => p.Id == projectId).First();
            var rootNode = MakeNode(project?.Name ?? $"项目 {projectId}", new[] { $"功能模块: {topModules.Count}" }, rootChildren);
            var mapData = JsonSerializer.Serialize(rootNode, new JsonSerializerOptions { WriteIndented = false });

            // 找或建 FeatureMap 记录
            var existing = _fsql.Select<MindMap>()
                .Where(x => x.ProjectId == projectId && x.Type == MindMapType.FeatureMap && !x.IsDeleted)
                .OrderByDescending(x => x.Id).First();

            if (existing != null)
            {
                _fsql.Update<MindMap>().Where(x => x.Id == existing.Id)
                    .Set(x => x.MapData, mapData)
                    .Set(x => x.GeneratedBy, GeneratedBy.Auto)
                    .Set(x => x.UpdatedTime, (DateTime?)DateTime.Now)
                    .ExecuteAffrows();
            }
            else
            {
                _fsql.Insert(new MindMap
                {
                    ProjectId = projectId,
                    Title = $"{project?.Name ?? "项目"} - 功能地图",
                    Type = MindMapType.FeatureMap,
                    MapData = mapData,
                    GeneratedBy = GeneratedBy.Auto,
                    CreatedTime = DateTime.Now,
                }).ExecuteAffrows();
            }

            return Result.Success("功能地图已从数据派生");
        }
    }
}

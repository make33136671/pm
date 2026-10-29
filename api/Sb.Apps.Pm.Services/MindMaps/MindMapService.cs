using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.MindMaps
{
    [AutoDependencyInjection]
    public class MindMapService : BaseCrudService<MindMap>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "思维导图";
        public int InitOrder => 150;
        public Type[] GetEntityTypes() => new[] { typeof(MindMap) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition { Name = "MindMap", Path = "/mindmap", Component = "/mindmap/index", Title = "思维导图", Icon = "lucide:brain", OrderNo = 7 },
        };

        public void SeedData(IFreeSql fsql)
        {
            // 确保每个项目都有一个主导图
            var projects = fsql.Select<Project>().ToList();
            foreach (var project in projects)
            {
                bool hasMain = fsql.Select<MindMap>().Where(m => m.ProjectId == project.Id && m.IsMain).Any();
                if (hasMain) continue;

                var first = fsql.Select<MindMap>().Where(m => m.ProjectId == project.Id && m.Type == MindMapType.Requirement).OrderBy(m => m.Id).First();
                if (first != null)
                {
                    fsql.Update<MindMap>().Set(m => m.IsMain, true).Where(m => m.Id == first.Id).ExecuteAffrows();
                    continue;
                }

                var mapData = System.Text.Json.JsonSerializer.Serialize(new
                {
                    data = new { text = project.Name, tag = new[] { "进度: 暂无需求" } },
                    children = Array.Empty<object>()
                });
                fsql.Insert(new MindMap
                {
                    ProjectId = project.Id,
                    Title = $"{project.Name} - 主导图",
                    Type = MindMapType.Requirement,
                    MapData = mapData,
                    IsMain = true,
                    GeneratedBy = GeneratedBy.Auto,
                    CreatedTime = DateTime.Now
                }).ExecuteAffrows();
            }
        }
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public MindMapService(IFreeSql fsql, ILogger<MindMapService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        public Result GetList(int? projectId = null, MindMapType? type = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            // 排除已删除项目的导图
            var activeProjectIds = _fsql.Select<Project>().Where(p => p.IsDeleted == false).ToList(p => p.Id);
            var query = Select
                .Where(c => activeProjectIds.Contains(c.ProjectId))
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(type.HasValue, c => c.Type == type.Value)
                .Include(c => c.Project);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(MindMap mindMap)
        {
            mindMap.CreatedTime = DateTime.Now;
            // 普通新增不允许设置为主导图（主导图只能通过 CreateMainForProject 创建）
            if (!mindMap.IsMain)
                mindMap.IsMain = false;
            mindMap = this.Insert(mindMap);
            return Result.Success("思维导图创建成功!", mindMap);
        }

        public Result Edit(MindMap mindMap)
        {
            mindMap.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Title, c.MapData, c.UpdatedTime
            }).SetSource(mindMap).ExecuteAffrows();
            return count > 0 ? Result.Success("思维导图更新成功!") : Result.Fail("思维导图更新失败!");
        }

        /// <summary>
        /// 删除（主导图不允许删除）
        /// </summary>
        public new Result Del(int id)
        {
            var mindMap = Select.Where(c => c.Id == id).First();
            if (mindMap == null) return Result.Fail("思维导图不存在!");
            if (mindMap.IsMain) return Result.Fail("主导图不能删除!");
            return base.Del(id);
        }

        /// <summary>
        /// 获取项目主导图
        /// </summary>
        public Result GetMain(int projectId)
        {
            var main = Select.Where(c => c.ProjectId == projectId && c.IsMain).First();
            if (main == null)
            {
                // 自动创建主导图
                main = CreateMainForProject(projectId);
                if (main == null) return Result.Fail("项目不存在!");
            }
            return Result.Success(main);
        }

        /// <summary>
        /// 为项目创建主导图
        /// </summary>
        public MindMap? CreateMainForProject(int projectId)
        {
            var project = _fsql.Select<Project>().Where(c => c.Id == projectId).First();
            if (project == null) return null;

            // 检查是否已有主导图
            var existing = Select.Where(c => c.ProjectId == projectId && c.IsMain).First();
            if (existing != null) return existing;

            var mapData = BuildMainMapData(project);
            var mindMap = new MindMap
            {
                ProjectId = projectId,
                Title = $"{project.Name} - 主导图",
                Type = MindMapType.Requirement,
                MapData = mapData,
                IsMain = true,
                GeneratedBy = GeneratedBy.Auto,
                CreatedTime = DateTime.Now
            };
            return this.Insert(mindMap);
        }

        /// <summary>
        /// 同步主导图（从需求重新生成）
        /// </summary>
        public Result SyncMain(int projectId)
        {
            var project = _fsql.Select<Project>().Where(c => c.Id == projectId).First();
            if (project == null) return Result.Fail("项目不存在!");

            var main = Select.Where(c => c.ProjectId == projectId && c.IsMain).First();
            if (main == null)
            {
                main = CreateMainForProject(projectId);
                return Result.Success("主导图已创建!", main);
            }

            main.MapData = BuildMainMapData(project);
            main.UpdatedTime = DateTime.Now;
            UpdateDiy.UpdateColumns(c => new { c.MapData, c.UpdatedTime }).SetSource(main).ExecuteAffrows();
            return Result.Success("主导图已同步!", main);
        }

        /// <summary>
        /// 构建主导图数据 (simple-mind-map 格式，按功能模块分组，带完成状态)
        /// </summary>
        private string BuildMainMapData(Project project)
        {
            var requirements = _fsql.Select<Requirement>()
                .Where(c => c.ProjectId == project.Id)
                .OrderBy(c => c.Sort)
                .ToList();

            var featureModules = _fsql.Select<FeatureModule>()
                .Where(c => c.ProjectId == project.Id)
                .OrderBy(c => c.Sort)
                .ToList();

            var children = new List<object>();

            if (featureModules.Count == 0)
            {
                // 没有功能模块时，需求平铺（兼容旧行为）
                children = requirements.Select(r => BuildReqNode(r)).ToList<object>();
            }
            else
            {
                // 获取需求与功能模块的关联
                var moduleIds = featureModules.Select(m => m.Id).ToList();
                var relations = _fsql.Select<RequirementFeatureRel>()
                    .Where(r => moduleIds.Contains(r.FeatureModuleId))
                    .ToList();

                var reqById = requirements.ToDictionary(r => r.Id);

                // 按功能模块分组需求
                var reqsByModule = relations
                    .Where(r => reqById.ContainsKey(r.RequirementId))
                    .GroupBy(r => r.FeatureModuleId)
                    .ToDictionary(g => g.Key, g => g.Select(r => reqById[r.RequirementId]).ToList());

                // 未关联功能模块的需求
                var assignedReqIds = relations.Select(r => r.RequirementId).Distinct().ToHashSet();
                var unassignedReqs = requirements.Where(r => !assignedReqIds.Contains(r.Id)).ToList();

                // 功能模块树 + 各模块下的需求
                children.AddRange(BuildModuleNodes(featureModules, null, reqsByModule));

                // 未分类需求
                if (unassignedReqs.Count > 0)
                {
                    var doneCount = unassignedReqs.Count(r => r.Status == RequirementStatus.Done || r.Status == RequirementStatus.Closed);
                    children.Add(new
                    {
                        data = new
                        {
                            text = "未分类",
                            tag = new[] { $"{doneCount}/{unassignedReqs.Count}" },
                            fillColor = ""
                        },
                        children = unassignedReqs.Select(r => BuildReqNode(r)).ToList<object>()
                    });
                }
            }

            // 总体进度
            var totalReqs = requirements.Count;
            var totalDone = requirements.Count(r => r.Status == RequirementStatus.Done || r.Status == RequirementStatus.Closed);
            var progress = totalReqs > 0 ? $"{totalDone}/{totalReqs} ({totalDone * 100 / totalReqs}%)" : "暂无需求";

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                data = new
                {
                    text = project.Name,
                    tag = new[] { $"进度: {progress}" }
                },
                children
            });
        }

        /// <summary>
        /// 递归构建功能模块节点（含子模块和关联需求）
        /// </summary>
        private List<object> BuildModuleNodes(List<FeatureModule> allModules, int? parentId,
            Dictionary<int, List<Requirement>> reqsByModule)
        {
            return allModules
                .Where(m => m.ParentId == parentId)
                .OrderBy(m => m.Sort)
                .Select(m =>
                {
                    var moduleReqs = reqsByModule.GetValueOrDefault(m.Id) ?? new List<Requirement>();
                    var childModuleNodes = BuildModuleNodes(allModules, m.Id, reqsByModule);

                    // 收集该模块及子模块下的所有需求，计算进度
                    var allReqs = CollectBranchReqs(allModules, m.Id, reqsByModule);
                    var total = allReqs.Count;
                    var done = allReqs.Count(r => r.Status == RequirementStatus.Done || r.Status == RequirementStatus.Closed);

                    var status = CalcGroupStatus(allReqs);
                    var tags = new List<string> { status };
                    if (total > 0) tags.Add($"{done}/{total}");

                    // 子节点：子模块在前，该模块直接关联的需求在后
                    var children = new List<object>();
                    children.AddRange(childModuleNodes);
                    children.AddRange(moduleReqs.Select(r => BuildReqNode(r)));

                    return (object)new
                    {
                        data = new
                        {
                            text = m.Name,
                            tag = tags.ToArray(),
                            fillColor = GetGroupColor(status)
                        },
                        children
                    };
                }).ToList();
        }

        /// <summary>
        /// 递归收集某模块及其所有子模块下的需求
        /// </summary>
        private List<Requirement> CollectBranchReqs(List<FeatureModule> allModules, int moduleId,
            Dictionary<int, List<Requirement>> reqsByModule)
        {
            var result = new List<Requirement>();
            result.AddRange(reqsByModule.GetValueOrDefault(moduleId) ?? new List<Requirement>());
            foreach (var child in allModules.Where(m => m.ParentId == moduleId))
                result.AddRange(CollectBranchReqs(allModules, child.Id, reqsByModule));
            return result;
        }

        private static string CalcGroupStatus(List<Requirement> reqs)
        {
            if (reqs.Count == 0) return "规划中";
            if (reqs.All(r => r.Status == RequirementStatus.Done || r.Status == RequirementStatus.Closed)) return "已完成";
            if (reqs.Any(r => r.Status == RequirementStatus.InProgress || r.Status == RequirementStatus.Testing)) return "开发中";
            if (reqs.All(r => r.Status == RequirementStatus.Draft || r.Status == RequirementStatus.Todo)) return "规划中";
            return "部分完成";
        }

        private static string GetGroupColor(string status) => status switch
        {
            "已完成" => "#d4edda",
            "开发中" => "#fff3cd",
            "部分完成" => "#cce5ff",
            _ => ""
        };

        private static object BuildReqNode(Requirement r)
        {
            var isDone = r.Status == RequirementStatus.Done || r.Status == RequirementStatus.Closed;
            var statusLabel = isDone ? "已完成" : r.Status.ToString();
            return new
            {
                data = new
                {
                    text = r.Title,
                    tag = new[] { statusLabel },
                    note = $"类型: {r.Type}\n状态: {r.Status}",
                    fillColor = isDone ? "#d4edda" : r.Status == RequirementStatus.InProgress ? "#fff3cd" : ""
                },
                children = Array.Empty<object>()
            };
        }

        /// <summary>
        /// 生成需求思维导图（作为普通导图，非主导图）。
        /// upsert：同项目同类型已存在则更新，否则新建——避免导图无限增生。
        /// </summary>
        public Result GenerateRequirementMap(int projectId)
        {
            var project = _fsql.Select<Project>().Where(c => c.Id == projectId).First();
            if (project == null) return Result.Fail("项目不存在!");

            var mapData = BuildMainMapData(project);

            // 先查同 (ProjectId, Type=Requirement) 是否已存在
            var existing = _fsql.Select<MindMap>()
                .Where(m => m.ProjectId == projectId && m.Type == MindMapType.Requirement)
                .OrderBy(m => m.Id)
                .First();

            if (existing != null)
            {
                _fsql.Update<MindMap>().Where(m => m.Id == existing.Id)
                    .Set(m => m.MapData, mapData)
                    .Set(m => m.Title, $"{project.Name} - 需求导图")
                    .Set(m => m.UpdatedTime, DateTime.Now)
                    .ExecuteAffrows();
                existing.MapData = mapData;
                return Result.Success("需求导图已更新!", existing);
            }

            var mindMap = new MindMap
            {
                ProjectId = projectId,
                Title = $"{project.Name} - 需求导图",
                Type = MindMapType.Requirement,
                MapData = mapData,
                IsMain = false,
                GeneratedBy = GeneratedBy.Auto,
                CreatedTime = DateTime.Now
            };

            mindMap = this.Insert(mindMap);
            return Result.Success("需求导图生成成功!", mindMap);
        }
    }
}

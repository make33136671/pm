using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.FeatureModules
{
    [AutoDependencyInjection]
    public class FeatureModuleService : BaseCrudService<FeatureModule>
    {
        private readonly IFreeSql _fsql;
        private readonly MindMapService _mindMapService;

        public FeatureModuleService(IFreeSql fsql, ILogger<FeatureModuleService> logger, MindMapService mindMapService) : base(fsql, logger)
        {
            _fsql = fsql;
            _mindMapService = mindMapService;
        }

        #region CRUD

        public Result GetList(int projectId, string field = "", string order = "")
        {
            var query = Select.Where(c => c.ProjectId == projectId);
            query = ApplySorting(query, field, order);
            var items = query.ToList();
            return Result.Success(items);
        }

        /// <summary>
        /// 获取树形结构（含状态自动计算）
        /// </summary>
        public Result GetTree(int projectId)
        {
            var allModules = Select.Where(c => c.ProjectId == projectId).OrderBy(c => c.Sort).ToList();

            // 获取所有关联关系
            var allRels = _fsql.Select<RequirementFeatureRel>()
                .Where(r => allModules.Select(m => m.Id).Contains(r.FeatureModuleId))
                .ToList();

            // 获取相关需求的状态
            var reqIds = allRels.Select(r => r.RequirementId).Distinct().ToList();
            var reqStatuses = reqIds.Count > 0
                ? _fsql.Select<Requirement>().Where(r => reqIds.Contains(r.Id))
                    .ToList(r => new { r.Id, r.Status })
                    .ToDictionary(r => r.Id, r => r.Status)
                : new Dictionary<int, RequirementStatus>();

            // 按模块分组关联需求状态
            var moduleReqStatuses = allRels
                .Where(r => reqStatuses.ContainsKey(r.RequirementId))
                .GroupBy(r => r.FeatureModuleId)
                .ToDictionary(g => g.Key, g => g.Select(r => reqStatuses[r.RequirementId]).ToList());

            // 构建树
            var tree = BuildTree(allModules, null, moduleReqStatuses);
            return Result.Success(tree);
        }

        public Result Create(FeatureModule module)
        {
            module.CreatedTime = DateTime.Now;
            module = this.Insert(module);
            SyncFeatureMap(module.ProjectId);
            return Result.Success("功能模块创建成功!", module);
        }

        public Result Edit(FeatureModule module)
        {
            module.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Name, c.Description, c.ParentId, c.Sort, c.UpdatedTime, c.Status
            }).SetSource(module).ExecuteAffrows();

            if (count > 0) SyncFeatureMap(module.ProjectId);
            return count > 0 ? Result.Success("功能模块更新成功!") : Result.Fail("更新失败!");
        }

        public new Result Del(int id)
        {
            var module = Select.Where(c => c.Id == id).First();
            if (module == null) return Result.Fail("功能模块不存在!");

            // 递归软删除子节点
            DeleteRecursive(id);

            // 清理关联关系
            _fsql.Delete<RequirementFeatureRel>().Where(r => r.FeatureModuleId == id).ExecuteAffrows();

            SyncFeatureMap(module.ProjectId);
            return Result.Success("删除成功!");
        }

        /// <summary>
        /// 节点详情（含关联需求列表）
        /// </summary>
        public Result GetNodeDetail(int id)
        {
            var module = Select.Where(c => c.Id == id).First();
            if (module == null) return Result.Fail("功能模块不存在!");

            var relReqIds = _fsql.Select<RequirementFeatureRel>()
                .Where(r => r.FeatureModuleId == id)
                .ToList(r => r.RequirementId);

            var requirements = relReqIds.Count > 0
                ? _fsql.Select<Requirement>().Where(r => relReqIds.Contains(r.Id))
                    .ToList<object>(r => new { r.Id, r.Title, r.Type, r.Status, r.Assignee, r.DueDate })
                : new List<object>();

            // 计算状态
            var statuses = relReqIds.Count > 0
                ? _fsql.Select<Requirement>().Where(r => relReqIds.Contains(r.Id)).ToList(r => r.Status)
                : new List<RequirementStatus>();

            return Result.Success(new
            {
                module.Id, module.ProjectId, module.ParentId, module.Name, module.Description,
                module.Sort, module.CreatedTime,
                status = CalcNodeStatus(statuses, module.Status),
                statusManual = module.Status,
                requirementCount = relReqIds.Count,
                requirements,
            });
        }

        #endregion

        #region 需求关联

        /// <summary>
        /// 保存需求的功能模块关联（覆盖式）
        /// </summary>
        public Result SaveRelations(int requirementId, List<int> featureModuleIds)
        {
            _fsql.Delete<RequirementFeatureRel>().Where(r => r.RequirementId == requirementId).ExecuteAffrows();

            if (featureModuleIds.Count > 0)
            {
                var rels = featureModuleIds.Select(fmId => new RequirementFeatureRel
                {
                    RequirementId = requirementId,
                    FeatureModuleId = fmId,
                    CreatedTime = DateTime.Now,
                }).ToList();
                _fsql.Insert(rels).ExecuteAffrows();
            }

            // 获取涉及的项目ID，同步导图
            var projectIds = Select.Where(c => featureModuleIds.Contains(c.Id)).Distinct().ToList(c => c.ProjectId);
            foreach (var pid in projectIds) SyncFeatureMap(pid);

            return Result.Success("关联保存成功!");
        }

        /// <summary>
        /// 获取需求的关联模块 ID 列表
        /// </summary>
        public Result GetRelations(int requirementId)
        {
            var ids = _fsql.Select<RequirementFeatureRel>()
                .Where(r => r.RequirementId == requirementId)
                .ToList(r => r.FeatureModuleId);
            return Result.Success(ids);
        }

        #endregion

        #region 导图同步

        /// <summary>
        /// 同步功能模块导图（从 FeatureModule 表构建导图数据）
        /// </summary>
        public Result SyncFeatureMap(int projectId)
        {
            var project = _fsql.Select<Project>().Where(p => p.Id == projectId).First();
            if (project == null) return Result.Fail("项目不存在!");

            var mapData = BuildFeatureMapData(projectId, project.Name);

            // 查找或创建 FeatureMap 类型的导图
            var existing = _fsql.Select<MindMap>()
                .Where(m => m.ProjectId == projectId && m.Type == MindMapType.FeatureMap)
                .DisableGlobalFilter()
                .Where(m => m.IsDeleted == false)
                .First();

            if (existing != null)
            {
                existing.MapData = mapData;
                existing.UpdatedTime = DateTime.Now;
                _fsql.Update<MindMap>().Where(m => m.Id == existing.Id)
                    .Set(m => m.MapData, mapData)
                    .Set(m => m.UpdatedTime, DateTime.Now)
                    .ExecuteAffrows();
            }
            else
            {
                var mindMap = new MindMap
                {
                    ProjectId = projectId,
                    Title = $"{project.Name} - 功能地图",
                    Type = MindMapType.FeatureMap,
                    MapData = mapData,
                    IsMain = false,
                    GeneratedBy = GeneratedBy.Auto,
                    CreatedTime = DateTime.Now,
                };
                _mindMapService.Insert(mindMap);
            }

            return Result.Success("功能地图同步成功!");
        }

        /// <summary>
        /// 构建功能地图 simple-mind-map JSON
        /// </summary>
        public string BuildFeatureMapData(int projectId, string projectName)
        {
            var allModules = Select.Where(c => c.ProjectId == projectId).OrderBy(c => c.Sort).ToList();

            // 获取所有关联和需求状态
            var moduleIds = allModules.Select(m => m.Id).ToList();
            var allRels = moduleIds.Count > 0
                ? _fsql.Select<RequirementFeatureRel>().Where(r => moduleIds.Contains(r.FeatureModuleId)).ToList()
                : new List<RequirementFeatureRel>();
            var reqIds = allRels.Select(r => r.RequirementId).Distinct().ToList();
            var reqStatuses = reqIds.Count > 0
                ? _fsql.Select<Requirement>().Where(r => reqIds.Contains(r.Id)).ToList(r => new { r.Id, r.Status }).ToDictionary(r => r.Id, r => r.Status)
                : new Dictionary<int, RequirementStatus>();

            var moduleReqStatuses = allRels
                .Where(r => reqStatuses.ContainsKey(r.RequirementId))
                .GroupBy(r => r.FeatureModuleId)
                .ToDictionary(g => g.Key, g => g.Select(r => reqStatuses[r.RequirementId]).ToList());

            var moduleReqCounts = allRels.GroupBy(r => r.FeatureModuleId)
                .ToDictionary(g => g.Key, g => g.Count());

            // 构建树形 JSON
            var children = BuildMapNodes(allModules, null, moduleReqStatuses, moduleReqCounts);

            var totalModules = allModules.Count;
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                data = new { text = projectName, tag = new[] { $"功能模块: {totalModules}" } },
                children
            });
        }

        /// <summary>
        /// 从导图数据回写到 FeatureModule 表（双向编辑）
        /// </summary>
        public Result SaveFromMap(int projectId, string mapDataJson)
        {
            try
            {
                var mapData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(mapDataJson);
                var children = mapData.GetProperty("children");

                // 获取现有模块
                var existingModules = Select.Where(c => c.ProjectId == projectId).ToList();
                var processedIds = new HashSet<int>();

                // 递归处理节点
                ProcessMapNodes(children, projectId, null, existingModules, processedIds, 0);

                // 软删除不在导图中的节点
                foreach (var module in existingModules)
                {
                    if (!processedIds.Contains(module.Id))
                    {
                        _fsql.Update<FeatureModule>().Where(c => c.Id == module.Id)
                            .Set(c => c.IsDeleted, true).ExecuteAffrows();
                    }
                }

                return Result.Success("导图数据已保存!");
            }
            catch (Exception ex)
            {
                return Result.Fail($"解析导图数据失败: {ex.Message}");
            }
        }

        #endregion

        #region 私有方法

        private List<object> BuildTree(List<FeatureModule> allModules, int? parentId, Dictionary<int, List<RequirementStatus>> moduleReqStatuses)
        {
            return allModules
                .Where(m => m.ParentId == parentId)
                .OrderBy(m => m.Sort)
                .Select(m =>
                {
                    var children = BuildTree(allModules, m.Id, moduleReqStatuses);
                    var directStatuses = moduleReqStatuses.GetValueOrDefault(m.Id) ?? new List<RequirementStatus>();
                    var allStatuses = CollectAllStatuses(allModules, m.Id, moduleReqStatuses);
                    var reqCount = _fsql.Select<RequirementFeatureRel>().Where(r => r.FeatureModuleId == m.Id).Count();

                    return (object)new
                    {
                        m.Id, m.ProjectId, m.ParentId, m.Name, m.Description, m.Sort,
                        status = CalcNodeStatus(allStatuses, m.Status),
                        statusManual = m.Status,   // 手动覆盖原始枚举值（前端表单回填用，null=自动）
                        requirementCount = reqCount,
                        children,
                    };
                }).ToList();
        }

        private List<RequirementStatus> CollectAllStatuses(List<FeatureModule> allModules, int moduleId, Dictionary<int, List<RequirementStatus>> moduleReqStatuses)
        {
            var result = new List<RequirementStatus>();
            result.AddRange(moduleReqStatuses.GetValueOrDefault(moduleId) ?? new List<RequirementStatus>());
            foreach (var child in allModules.Where(m => m.ParentId == moduleId))
            {
                result.AddRange(CollectAllStatuses(allModules, child.Id, moduleReqStatuses));
            }
            return result;
        }

        /// <summary>
        /// 计算节点展示状态。手动 Status 非空时优先用手动值（映射成中文），
        /// 否则按关联需求自动派生。
        /// </summary>
        private static string CalcNodeStatus(List<RequirementStatus> statuses, FeatureModuleStatus? manual = null)
        {
            // 手动覆盖优先
            if (manual.HasValue)
            {
                return manual.Value switch
                {
                    FeatureModuleStatus.Planning => "规划中",
                    FeatureModuleStatus.InProgress => "开发中",
                    FeatureModuleStatus.Done => "已上线",
                    FeatureModuleStatus.Deprecated => "已废弃",
                    _ => "规划中",
                };
            }
            // 自动派生
            if (statuses.Count == 0) return "规划中";
            if (statuses.Any(s => s == RequirementStatus.InProgress || s == RequirementStatus.Testing)) return "开发中";
            if (statuses.All(s => s == RequirementStatus.Done || s == RequirementStatus.Closed)) return "已上线";
            if (statuses.All(s => s == RequirementStatus.Draft || s == RequirementStatus.Todo)) return "规划中";
            return "部分上线";
        }

        private static readonly Dictionary<string, string> StatusColorMap = new()
        {
            ["规划中"] = "",
            ["开发中"] = "#fff3cd",
            ["已上线"] = "#d4edda",
            ["部分上线"] = "#cce5ff",
        };

        private List<object> BuildMapNodes(List<FeatureModule> allModules, int? parentId,
            Dictionary<int, List<RequirementStatus>> moduleReqStatuses, Dictionary<int, int> moduleReqCounts)
        {
            return allModules
                .Where(m => m.ParentId == parentId)
                .OrderBy(m => m.Sort)
                .Select(m =>
                {
                    var allStatuses = CollectAllStatuses(allModules, m.Id, moduleReqStatuses);
                    var status = CalcNodeStatus(allStatuses, m.Status);
                    var reqCount = moduleReqCounts.GetValueOrDefault(m.Id, 0);
                    var tags = new List<string> { status };
                    if (reqCount > 0) tags.Add($"{reqCount}个需求");

                    return (object)new
                    {
                        data = new
                        {
                            text = m.Name,
                            tag = tags.ToArray(),
                            note = m.Description ?? "",
                            fillColor = StatusColorMap.GetValueOrDefault(status, ""),
                            uid = m.Id,
                        },
                        children = BuildMapNodes(allModules, m.Id, moduleReqStatuses, moduleReqCounts),
                    };
                }).ToList();
        }

        private void DeleteRecursive(int id)
        {
            _fsql.Update<FeatureModule>().Where(c => c.Id == id).Set(c => c.IsDeleted, true).ExecuteAffrows();
            var childIds = _fsql.Select<FeatureModule>().DisableGlobalFilter().Where(c => c.ParentId == id && c.IsDeleted == false).ToList(c => c.Id);
            foreach (var childId in childIds) DeleteRecursive(childId);
        }

        private void ProcessMapNodes(System.Text.Json.JsonElement nodes, int projectId, int? parentId, List<FeatureModule> existing, HashSet<int> processedIds, int sortStart)
        {
            int sort = sortStart;
            foreach (var node in nodes.EnumerateArray())
            {
                if (!node.TryGetProperty("data", out var data)) continue;
                var name = data.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
                var uid = data.TryGetProperty("uid", out var uidProp) && uidProp.TryGetInt32(out var uidVal) ? uidVal : 0;

                int moduleId;
                if (uid > 0 && existing.Any(m => m.Id == uid))
                {
                    // 更新现有节点
                    _fsql.Update<FeatureModule>().Where(c => c.Id == uid)
                        .Set(c => c.Name, name).Set(c => c.ParentId, parentId).Set(c => c.Sort, sort)
                        .Set(c => c.UpdatedTime, DateTime.Now).ExecuteAffrows();
                    moduleId = uid;
                }
                else
                {
                    // 新增节点
                    var newModule = new FeatureModule
                    {
                        ProjectId = projectId, ParentId = parentId, Name = name, Sort = sort, CreatedTime = DateTime.Now,
                    };
                    _fsql.Insert(newModule).ExecuteIdentity();
                    moduleId = newModule.Id;
                }

                processedIds.Add(moduleId);
                sort++;

                if (node.TryGetProperty("children", out var children) && children.GetArrayLength() > 0)
                {
                    ProcessMapNodes(children, projectId, moduleId, existing, processedIds, 0);
                }
            }
        }

        #endregion
    }
}

using FreeSql;
using Microsoft.Extensions.Logging;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Tasks;
using Sb.Apps.Pm.Models.Requirements;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    /// <summary>
    /// 一站式项目蓝图导入(#96)。
    /// 接受结构化导入数据(需求[]+功能模块树+任务[]),
    /// 自动建立 Requirement ↔ TaskItem ↔ FeatureModule 外键关联并落库。
    /// </summary>
    [AutoDependencyInjection]
    public class ProjectBlueprintService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<ProjectBlueprintService> _logger;
        public ProjectBlueprintService(IFreeSql fsql, ILogger<ProjectBlueprintService> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        public Result Import(int projectId, BlueprintInput input)
        {
            using var uow = _fsql.CreateUnitOfWork();
            var stats = new ImportStats();
            try
            {
                // 1) 建顶层功能模块
                var moduleIdMap = new Dictionary<string, int>(); // clientKey → 真实 Id
                foreach (var m in input.FeatureModules ?? new())
                {
                    var entity = new FeatureModule
                    {
                        ProjectId = projectId,
                        ParentId = null,
                        Name = m.Name,
                        Description = m.Description,
                        Sort = m.Sort,
                        CreatedTime = DateTime.Now,
                    };
                    entity.Id = (int)_fsql.Insert(entity).ExecuteIdentity();
                    if (!string.IsNullOrEmpty(m.Key)) moduleIdMap[m.Key] = entity.Id;

                    // 建子功能点
                    foreach (var child in m.Children ?? new())
                    {
                        var childEntity = new FeatureModule
                        {
                            ProjectId = projectId,
                            ParentId = entity.Id,
                            Name = child.Name,
                            Description = child.Description,
                            Sort = child.Sort,
                            CreatedTime = DateTime.Now,
                        };
                        childEntity.Id = (int)_fsql.Insert(childEntity).ExecuteIdentity();
                        if (!string.IsNullOrEmpty(child.Key)) moduleIdMap[child.Key] = childEntity.Id;
                        stats.Modules++;
                    }
                    stats.Modules++;
                }

                // 2) 建需求
                var reqIdMap = new Dictionary<string, int>(); // clientKey → 真实 Id
                foreach (var r in input.Requirements ?? new())
                {
                    var entity = new Requirement
                    {
                        ProjectId = projectId,
                        Title = r.Title,
                        Content = r.Content,
                        Type = Enum.TryParse<RequirementType>(r.Type, true, out var t) ? t : RequirementType.Feature,
                        Status = Enum.TryParse<RequirementStatus>(r.Status, true, out var s) ? s : RequirementStatus.Todo,
                        Complexity = Enum.TryParse<Complexity>(r.Complexity, true, out var c) ? c : Complexity.Medium,
                        Assignee = r.Assignee,
                        Source = RequirementSource.Manual,
                        IsConfirmed = true,
                        Sort = r.Sort,
                        CreatedTime = DateTime.Now,
                    };
                    entity.Id = (int)_fsql.Insert(entity).ExecuteIdentity();
                    if (!string.IsNullOrEmpty(r.Key)) reqIdMap[r.Key] = entity.Id;
                    stats.Requirements++;
                }

                // 3) 建任务(并关联需求+模块)
                foreach (var t in input.Tasks ?? new())
                {
                    var reqId = t.RequirementKey != null && reqIdMap.TryGetValue(t.RequirementKey, out var rid) ? rid : t.RequirementId;
                    var modId = t.FeatureModuleKey != null && moduleIdMap.TryGetValue(t.FeatureModuleKey, out var mid) ? mid : t.FeatureModuleId;

                    var entity = new TaskItem
                    {
                        ProjectId = projectId,
                        RequirementId = reqId,
                        FeatureModuleId = modId,
                        Title = t.Title,
                        Content = string.IsNullOrWhiteSpace(t.Content) ? t.Title : t.Content,
                        Type = Enum.TryParse<TaskItemType>(t.Type, true, out var tt) ? tt : TaskItemType.Development,
                        Complexity = Enum.TryParse<Complexity>(t.Complexity, true, out var tc) ? tc : Complexity.Medium,
                        Assignee = t.Assignee,
                        EstimatedHours = t.EstimatedHours,
                        ClaimStatus = TaskItemClaimStatus.Pending,
                        ExecStatus = TaskItemExecStatus.Idle,
                        ExecMode = TaskItemExecMode.Api,
                        Sort = t.Sort,
                        CreatedTime = DateTime.Now,
                    };
                    entity.Id = (int)_fsql.Insert(entity).ExecuteIdentity();
                    // #99 批量操作审计日志
                    _fsql.Insert(new TaskItemLog
                    {
                        TaskItemId = entity.Id,
                        Action = TaskItemLogAction.Created,
                        Message = "蓝图导入创建",
                        Operator = "System",
                        CreatedTime = DateTime.Now,
                    }).ExecuteAffrows();
                    stats.Tasks++;
                }

                uow.Commit();
                return Result.Success("蓝图导入成功", stats);
            }
            catch (Exception ex)
            {
                uow.Rollback();
                _logger.LogError(ex, "蓝图导入失败 projectId={Id}", projectId);
                return Result.Fail($"导入失败: {ex.Message}");
            }
        }
    }

    public class ImportStats
    {
        public int Modules { get; set; }
        public int Requirements { get; set; }
        public int Tasks { get; set; }
    }

    // ---- 输入 DTO ----
    public class BlueprintInput
    {
        public List<BlueprintModule>? FeatureModules { get; set; }
        public List<BlueprintRequirement>? Requirements { get; set; }
        public List<BlueprintTask>? Tasks { get; set; }
    }

    public class BlueprintModule
    {
        public string? Key { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public int Sort { get; set; }
        public List<BlueprintModule>? Children { get; set; }
    }

    public class BlueprintRequirement
    {
        public string? Key { get; set; }
        public string Title { get; set; } = "";
        public string? Content { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public string? Complexity { get; set; }
        public string? Assignee { get; set; }
        public int Sort { get; set; }
    }

    public class BlueprintTask
    {
        public string Title { get; set; } = "";
        public string? Content { get; set; }
        public string? RequirementKey { get; set; }   // 对应 BlueprintRequirement.Key
        public int? RequirementId { get; set; }        // 或直接用已有 Id
        public string? FeatureModuleKey { get; set; }  // 对应 BlueprintModule.Key
        public int? FeatureModuleId { get; set; }
        public string? Type { get; set; }
        public string? Complexity { get; set; }
        public string? Assignee { get; set; }
        public decimal? EstimatedHours { get; set; }
        public int Sort { get; set; }
    }
}

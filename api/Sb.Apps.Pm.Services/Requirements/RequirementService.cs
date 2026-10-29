using System.Text.RegularExpressions;
using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Tasks;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.Services.Requirements
{
    [AutoDependencyInjection]
    public class RequirementService : BaseCrudService<Requirement>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "需求管理";
        public int InitOrder => 110;
        public Type[] GetEntityTypes() => new[]
        {
            typeof(Requirement), typeof(RequirementComment),
            typeof(FeatureModule), typeof(RequirementFeatureRel),
        };
        public List<MenuDefinition> GetMenus() => new()
        {
            // 顶级路由容器（隐藏，实际入口在项目管理下）
            new MenuDefinition
            {
                Name = "Requirement", Path = "/requirement", Component = "BasicLayout",
                Redirect = "/requirement/list", Title = "需求管理",
                Icon = "lucide:clipboard-list", OrderNo = 3, HideInMenu = true,
            },
            // 挂载到项目管理下
            new MenuDefinition { ParentPath = "/project", Name = "FeatureModule", Path = "/project/feature-module", Component = "/feature-module/index", Title = "功能模块", Icon = "lucide:puzzle", OrderNo = 5 },
            new MenuDefinition { ParentPath = "/project", Name = "RequirementList", Path = "/project/requirement/list", Component = "/requirement/list/index", Title = "需求列表", Icon = "lucide:list", OrderNo = 6 },
            new MenuDefinition { ParentPath = "/project", Name = "RequirementForm", Path = "/project/requirement/form", Component = "/requirement/form/index", Title = "需求表单", OrderNo = 7, HideInMenu = true },
            new MenuDefinition { ParentPath = "/project", Name = "RequirementAnalysis", Path = "/project/requirement/analysis", Component = "/requirement/analysis/index", Title = "AI 需求分析", Icon = "lucide:brain", OrderNo = 8 },
        };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly MindMapService _mindMapService;
        private readonly DocVersionService _docVersionService;
        private readonly FeatureModuleService _featureModuleService;

        public RequirementService(IFreeSql fsql, ILogger<RequirementService> logger, IHttpContextAccessor httpContextAccessor, MindMapService mindMapService, DocVersionService docVersionService, FeatureModuleService featureModuleService) : base(fsql, logger)
        {
            _fsql = fsql;
            _docVersionService = docVersionService;
            _httpContextAccessor = httpContextAccessor;
            _mindMapService = mindMapService;
            _featureModuleService = featureModuleService;
        }

        public Result GetList(int? projectId = null, RequirementStatus? status = null, RequirementType? type = null,
            string? keyword = null, string? batchId = null, int? parentId = null, bool? onlyRoot = null,
            int? customerId = null, string field = "", string order = "", int page = 1, int pageSize = 20,
            RequirementSource? source = null)
        {
            var query = Select.Include(c => c.Project).Include(c => c.Customer);
            query = query.Where(c => c.IsConfirmed == true);
            query = query.WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value);
            query = query.WhereIf(status.HasValue, c => c.Status == status.Value);
            query = query.WhereIf(type.HasValue, c => c.Type == type.Value);
            query = query.WhereIf(!string.IsNullOrEmpty(keyword), c => c.Title.Contains(keyword));
            query = query.WhereIf(!string.IsNullOrEmpty(batchId), c => c.BatchId == batchId);
            query = query.WhereIf(parentId.HasValue, c => c.ParentId == parentId.Value);
            query = query.WhereIf(onlyRoot == true, c => c.ParentId == null);
            query = query.WhereIf(customerId.HasValue, c => customerId.Value == 0 ? c.CustomerId == null : c.CustomerId == customerId.Value);
            query = query.WhereIf(source.HasValue, c => c.Source == source.Value);
            query = ApplySorting(query, field, order);

            var data = query.Count(out long dataCount).Page(page, pageSize).ToList();

            // 批量查询子需求数量
            var ids = data.Select(c => c.Id).ToList();
            var childCounts = ids.Count > 0
                ? _fsql.Select<Requirement>()
                    .Where(c => c.ParentId != null && ids.Contains(c.ParentId.Value) && c.IsConfirmed == true)
                    .GroupBy(c => c.ParentId)
                    .ToList(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToDictionary(x => x.ParentId!.Value, x => x.Count)
                : new Dictionary<int, int>();

            // 批量查询关联的功能模块
            var featureModuleMap = new Dictionary<int, object>();
            if (ids.Count > 0)
            {
                var allRels = _fsql.Select<RequirementFeatureRel>()
                    .Include(r => r.FeatureModule)
                    .Where(r => ids.Contains(r.RequirementId) && r.FeatureModule.IsDeleted == false)
                    .ToList();
                foreach (var g in allRels.GroupBy(r => r.RequirementId))
                {
                    featureModuleMap[g.Key] = g.Select(r => new { r.FeatureModule.Id, r.FeatureModule.Name }).ToList();
                }
            }

            var items = data.Select(c => new
            {
                c.Id, c.ProjectId, c.Title, c.Content, c.Source, c.SourceRef,
                c.Type, c.Status, c.Complexity, c.Assignee,
                c.EstimatedHours, c.ActualHours, c.ParentId, c.CustomerId, c.Sort,
                c.DueDate, c.CompletedDate, c.IsAiFriendly, c.IsConfirmed,
                c.BatchId, c.BatchLabel, c.BatchDescription, c.AiProviderId,
                c.CreatedTime, c.UpdatedTime,
                Project = c.Project == null ? null : new { c.Project.Id, c.Project.Name },
                Customer = c.Customer == null ? null : new { c.Customer.Id, c.Customer.Name },
                ChildCount = childCounts.TryGetValue(c.Id, out var cc) ? cc : 0,
                FeatureModules = featureModuleMap.TryGetValue(c.Id, out var fms) ? fms : (object)new List<object>(),
            }).ToList();

            return Result.Page(items, dataCount);
        }

        public override Result Detail(int id)
        {
            var entity = Select.Include(c => c.Project).Include(c => c.Customer).Where(c => c.Id == id).First();
            if (entity == null) return Result.Fail("需求不存在!");

            // 父需求信息
            object? parent = null;
            if (entity.ParentId.HasValue)
            {
                parent = Select.Where(c => c.Id == entity.ParentId.Value)
                    .First(c => new { c.Id, c.Title, c.Status, c.Type });
            }

            // 子需求列表
            var children = Select.Where(c => c.ParentId == id && c.IsConfirmed == true)
                .OrderByDescending(c => c.Id)
                .ToList(c => new { c.Id, c.Title, c.Status, c.Type, c.Complexity, c.Assignee, c.CreatedTime });

            // 关联的功能模块
            var featureModules = _fsql.Select<RequirementFeatureRel, FeatureModule>()
                .InnerJoin((r, f) => r.FeatureModuleId == f.Id)
                .Where((r, f) => r.RequirementId == id && f.IsDeleted == false)
                .ToList((r, f) => new { f.Id, f.Name });

            return Result.Success(new
            {
                entity.Id, entity.ProjectId, entity.Title, entity.Content, entity.Source, entity.SourceRef,
                entity.Type, entity.Status, entity.Complexity, entity.Assignee,
                entity.EstimatedHours, entity.ActualHours, entity.ParentId, entity.CustomerId, entity.Sort,
                entity.DueDate, entity.CompletedDate, entity.IsAiFriendly, entity.IsConfirmed,
                entity.BatchId, entity.BatchLabel, entity.BatchDescription, entity.AiProviderId,
                entity.CreatedTime, entity.UpdatedTime,
                Project = entity.Project == null ? null : new { entity.Project.Id, entity.Project.Name },
                Customer = entity.Customer == null ? null : new { entity.Customer.Id, entity.Customer.Name },
                Parent = parent,
                Children = children,
                FeatureModules = featureModules,
            });
        }

        public Result Create(Requirement req)
        {
            req.CreatedTime = DateTime.Now;
            var username = _httpContextAccessor.HttpContext?.User?.Username();
            if (!string.IsNullOrEmpty(username) && string.IsNullOrEmpty(req.Assignee))
                req.Assignee = username;
            req = this.Insert(req);
            SyncMaps(req.ProjectId);
            _docVersionService.SaveVersion("Requirement", req.Id, DocVersionService.BuildRequirementMd(req), "创建需求", username);
            return Result.Success("需求创建成功!", req);
        }

        public Result Edit(Requirement req)
        {
            req.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Title, c.Content, c.Type, c.Status, c.Complexity,
                c.Assignee, c.EstimatedHours, c.ActualHours,
                c.ParentId, c.CustomerId, c.Sort, c.DueDate, c.CompletedDate,
                c.FeatureModuleId, c.UpdatedTime
            }).SetSource(req).ExecuteAffrows();

            if (count > 0)
            {
                SyncMaps(req.ProjectId);
                var username = _httpContextAccessor.HttpContext?.User?.Username();
                _docVersionService.SaveVersion("Requirement", req.Id, DocVersionService.BuildRequirementMd(req), "编辑需求", username);
            }
            return count > 0 ? Result.Success("需求更新成功!") : Result.Fail("需求更新失败!");
        }

        public override Result Del(int id)
        {
            var req = Select.Where(c => c.Id == id).First();
            var result = base.Del(id);
            if (req != null) SyncMaps(req.ProjectId);
            return result;
        }

        public Result BatchUpdateStatus(List<int> ids, RequirementStatus status)
        {
            if (ids == null || ids.Count == 0) return Result.Fail("请选择需求!");

            int count = _fsql.Update<Requirement>()
                .Where(c => ids.Contains(c.Id))
                .Set(c => c.Status, status)
                .Set(c => c.CompletedDate, status == RequirementStatus.Done ? DateTime.Now : (DateTime?)null)
                .ExecuteAffrows();

            // 同步涉及的项目导图（主导图 + 功能地图）
            var projectIds = _fsql.Select<Requirement>().Where(c => ids.Contains(c.Id)).Distinct().ToList(c => c.ProjectId);
            foreach (var pid in projectIds) SyncMaps(pid);
            return Result.Success($"已更新 {count} 条需求状态");
        }

        public Result Kanban(int projectId)
        {
            var reqs = Select.Where(c => c.ProjectId == projectId)
                .OrderBy(c => c.Sort)
                .ToList(c => new { c.Id, c.Title, c.Status, c.Type, c.Assignee, c.DueDate });

            var groups = reqs.GroupBy(c => c.Status).ToDictionary(g => g.Key.ToString(), g => g.ToList());
            return Result.Success(groups);
        }

        /// <summary>
        /// 同步主导图 + 功能地图
        /// </summary>
        private void SyncMaps(int projectId)
        {
            _mindMapService.SyncMain(projectId);
            _featureModuleService.SyncFeatureMap(projectId);
        }

        #region AI 生成需求确认

        public Result GetPendingConfirm(string batchId)
        {
            var list = Select.Where(c => c.BatchId == batchId && c.IsConfirmed == false)
                .OrderBy(c => c.Id).ToList();
            return Result.Success(list);
        }

        public Result ConfirmBatch(string batchId)
        {
            var reqs = Select.Where(c => c.BatchId == batchId && c.IsConfirmed == false).ToList();
            int count = _fsql.Update<Requirement>()
                .Where(c => c.BatchId == batchId && c.IsConfirmed == false)
                .Set(c => c.IsConfirmed, true)
                .ExecuteAffrows();

            // 为确认的需求生成文档版本
            var username = _httpContextAccessor.HttpContext?.User?.Username();
            foreach (var req in reqs)
                _docVersionService.SaveVersion("Requirement", req.Id, DocVersionService.BuildRequirementMd(req), "AI 生成确认", username);

            return Result.Success($"已确认 {count} 条需求");
        }

        public Result ConfirmSingle(int id)
        {
            var req = Select.Where(c => c.Id == id).First();
            int count = _fsql.Update<Requirement>()
                .Where(c => c.Id == id && c.IsConfirmed == false)
                .Set(c => c.IsConfirmed, true)
                .ExecuteAffrows();

            if (count > 0 && req != null)
            {
                var username = _httpContextAccessor.HttpContext?.User?.Username();
                _docVersionService.SaveVersion("Requirement", req.Id, DocVersionService.BuildRequirementMd(req), "AI 生成确认", username);
            }
            return count > 0 ? Result.Success("需求已确认!") : Result.Fail("确认失败!");
        }

        public Result DiscardBatch(string batchId)
        {
            int count = _fsql.Update<Requirement>()
                .Where(c => c.BatchId == batchId && c.IsConfirmed == false)
                .Set(c => c.IsDeleted, true)
                .ExecuteAffrows();
            return Result.Success($"已丢弃 {count} 条未确认需求");
        }

        /// <summary>标记删除（不真删，保留在表格中显示）</summary>
        public Result RejectSingle(int id)
        {
            int count = _fsql.Update<Requirement>()
                .Where(c => c.Id == id && c.IsConfirmed == false)
                .Set(c => c.IsDeleted, true)
                .ExecuteAffrows();
            return count > 0 ? Result.Success("已标记删除") : Result.Fail("操作失败");
        }

        /// <summary>撤销标记删除</summary>
        public Result RestoreSingle(int id)
        {
            int count = _fsql.Update<Requirement>()
                .Where(c => c.Id == id && c.IsConfirmed == false)
                .DisableGlobalFilter("IsDeleted")
                .Set(c => c.IsDeleted, false)
                .ExecuteAffrows();
            return count > 0 ? Result.Success("已恢复") : Result.Fail("操作失败");
        }

        /// <summary>编辑暂存需求</summary>
        public Result EditStaging(int id, string title, string? content, RequirementType type)
        {
            int count = _fsql.Update<Requirement>()
                .Where(c => c.Id == id && c.IsConfirmed == false)
                .Set(c => c.Title, title)
                .Set(c => c.Content, content)
                .Set(c => c.Type, type)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();
            return count > 0 ? Result.Success("更新成功") : Result.Fail("更新失败");
        }

        /// <summary>获取批次所有需求（含已确认和已标记删除的）</summary>
        public Result GetPendingConfirmAll(string batchId)
        {
            var list = _fsql.Select<Requirement>()
                .DisableGlobalFilter("IsDeleted")
                .Where(c => c.BatchId == batchId)
                .OrderBy(c => c.Sort)
                .OrderBy(c => c.Id).ToList();
            return Result.Success(list);
        }

        /// <summary>批量确认（只确认未删除的）</summary>
        public Result ConfirmAll(string batchId)
        {
            var reqs = Select.Where(c => c.BatchId == batchId && c.IsConfirmed == false).ToList();
            int count = _fsql.Update<Requirement>()
                .Where(c => c.BatchId == batchId && c.IsConfirmed == false && c.IsDeleted == false)
                .Set(c => c.IsConfirmed, true)
                .ExecuteAffrows();

            // 为确认的需求生成文档版本
            var username = _httpContextAccessor.HttpContext?.User?.Username();
            foreach (var req in reqs.Where(r => !r.IsDeleted))
                _docVersionService.SaveVersion("Requirement", req.Id, DocVersionService.BuildRequirementMd(req), "AI 生成确认", username);

            // 清理已标记删除的
            _fsql.Delete<Requirement>()
                .DisableGlobalFilter("IsDeleted")
                .Where(c => c.BatchId == batchId && c.IsConfirmed == false && c.IsDeleted == true)
                .ExecuteAffrows();

            return Result.Success($"已确认 {count} 条需求");
        }

        /// <summary>
        /// AI 分析拆分需求 — 调用 AI 解析内容，逐条写入暂存库
        /// 返回 batchId，前端轮询 GetPendingConfirmAll 获取结果
        /// </summary>
        public async Task<Result> AnalyzeAndSplit(int projectId, string content, IAiProvider? aiProvider)
        {
            if (string.IsNullOrWhiteSpace(content))
                return Result.Fail("内容不能为空");

            var batchId = Guid.NewGuid().ToString("N")[..12];

            if (aiProvider == null)
                return Result.Fail("未配置 AI 提供商，请先在系统设置中配置");

            var prompt = @"你是一个需求分析专家。请分析以下内容（可能是聊天记录、需求文档或其他文本），从中提取出独立的需求条目。

要求：
1. 每条需求必须有明确的标题（简短，不超过50字）
2. 每条需求要有详细描述（content）
3. 判断需求类型：Feature(功能)、Bugfix(修复)、Improvement(优化)、Task(任务)、Research(调研)
4. 评估复杂度：Low(简单)、Medium(中等)、High(复杂)、VeryHigh(非常复杂)
5. 忽略闲聊、问候等非需求内容
6. 如果内容中包含图片地址，在描述中保留

请严格按以下 JSON 数组格式返回，不要包含其他内容：
[{""title"":""需求标题"",""content"":""详细描述"",""type"":""Feature"",""complexity"":""Medium""}]

以下是要分析的内容：
---
" + content;

            try
            {
                var response = await aiProvider.SendAsync(new AiRequest
                {
                    SystemPrompt = "你是需求分析助手，只返回 JSON 数组，不要返回其他内容。",
                    Messages = new List<AiMessage> { new() { Role = "user", Content = prompt } },
                    MaxTokens = 4096,
                    Temperature = 0.2f,
                });

                if (!response.Success)
                    return Result.Fail($"AI 调用失败: {response.Error}");

                // 解析 JSON
                var jsonContent = response.Content.Trim();
                // 处理可能的 markdown 代码块包裹
                if (jsonContent.StartsWith("```"))
                {
                    var lines = jsonContent.Split('\n');
                    jsonContent = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
                }

                var items = System.Text.Json.JsonSerializer.Deserialize<List<AnalyzedRequirement>>(jsonContent,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (items == null || items.Count == 0)
                    return Result.Fail("AI 未能从内容中提取出需求");

                int sort = 0;
                foreach (var item in items)
                {
                    var itemContent = await DownloadExternalImages(item.Content);

                    var req = new Requirement
                    {
                        ProjectId = projectId,
                        Title = item.Title ?? "未命名需求",
                        Content = itemContent,
                        Type = Enum.TryParse<RequirementType>(item.Type, true, out var t) ? t : RequirementType.Feature,
                        Complexity = Enum.TryParse<Complexity>(item.Complexity, true, out var c2) ? c2 : Complexity.Medium,
                        Status = RequirementStatus.Draft,
                        Source = RequirementSource.AI,
                        IsConfirmed = false,
                        BatchId = batchId,
                        Sort = sort++,
                        CreatedTime = DateTime.Now,
                    };
                    this.Insert(req);

                    _docVersionService.SaveVersion("Requirement", req.Id, DocVersionService.BuildRequirementMd(req), "AI 生成", "System");
                }

                return Result.Success("分析完成", new { batchId, count = items.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI 分析拆分需求失败");
                return Result.Fail($"分析失败: {ex.Message}");
            }
        }

        private class AnalyzedRequirement
        {
            public string? Title { get; set; }
            public string? Content { get; set; }
            public string? Type { get; set; }
            public string? Complexity { get; set; }
        }

        /// <summary>
        /// AI 将一条需求拆解为多个开发任务(TaskItem 暂存,等待确认)。
        /// 返回 batchId,前端凭此调 Admin/Requirement/AiConfirmTasks 确认入库。
        /// </summary>
        public async Task<Result> SplitTasks(int requirementId, IAiProvider? aiProvider)
        {
            if (aiProvider == null)
                return Result.Fail("未配置 AI 提供商，请先在系统设置中配置");

            var req = Select.Where(r => r.Id == requirementId).First();
            if (req == null) return Result.Fail("需求不存在");

            var prompt = $@"你是一个技术负责人，请将以下需求拆解为可独立执行的开发任务列表。

需求标题：{req.Title}
需求描述：
{req.Content ?? "（无描述）"}

要求：
1. 每个任务必须有明确的标题（≤50字）
2. 每个任务要有清晰的描述（content）说明做什么
3. 判断任务类型：Development(开发)、Bug(修复)、Design(设计)、Testing(测试)、Docs(文档)
4. 评估复杂度：Low(简单)、Medium(中等)、High(复杂)
5. 估算工时（estimatedHours，小数，如 2.5）

请严格按以下 JSON 数组格式返回，不要包含其他内容：
[{{""title"":""任务标题"",""content"":""详细描述"",""type"":""Development"",""complexity"":""Medium"",""estimatedHours"":2.0}}]";

            try
            {
                var response = await aiProvider.SendAsync(new AiRequest
                {
                    SystemPrompt = "你是技术负责人，只返回 JSON 数组，不要返回其他内容。",
                    Messages = new List<AiMessage> { new() { Role = "user", Content = prompt } },
                    MaxTokens = 4096,
                    Temperature = 0.2f,
                });

                if (!response.Success)
                    return Result.Fail($"AI 调用失败: {response.Error}");

                var jsonContent = response.Content.Trim();
                if (jsonContent.StartsWith("```"))
                {
                    var lines = jsonContent.Split('\n');
                    jsonContent = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
                }

                var items = System.Text.Json.JsonSerializer.Deserialize<List<SplitTaskItem>>(jsonContent,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (items == null || items.Count == 0)
                    return Result.Fail("AI 未能拆解出任务");

                var batchId = Guid.NewGuid().ToString("N")[..12];
                int sort = 0;
                foreach (var item in items)
                {
                    var task = new TaskItem
                    {
                        ProjectId = req.ProjectId,
                        RequirementId = requirementId,
                        Title = item.Title ?? "未命名任务",
                        Content = item.Content,
                        Type = Enum.TryParse<TaskItemType>(item.Type, true, out var t) ? t : TaskItemType.Development,
                        Complexity = Enum.TryParse<Complexity>(item.Complexity, true, out var c) ? c : Complexity.Medium,
                        EstimatedHours = item.EstimatedHours,
                        BatchId = batchId,
                        ClaimStatus = TaskItemClaimStatus.Pending,
                        ExecStatus = TaskItemExecStatus.Idle,
                        ExecMode = TaskItemExecMode.Api,
                        Sort = sort++,
                        CreatedTime = DateTime.Now,
                    };
                    _fsql.Insert(task).ExecuteAffrows();
                }

                return Result.Success("任务拆解完成", new { batchId, count = items.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI 拆解任务失败 requirementId={Id}", requirementId);
                return Result.Fail($"拆解失败: {ex.Message}");
            }
        }

        private class SplitTaskItem
        {
            public string? Title { get; set; }
            public string? Content { get; set; }
            public string? Type { get; set; }
            public string? Complexity { get; set; }
            public decimal? EstimatedHours { get; set; }
        }

        /// <summary>MCP 提交已分析的需求批次（AI 已完成分析，直接写入暂存）</summary>
        public async Task<Result> SubmitAnalysis(int projectId, List<SubmitAnalysisItem> items, string? batchLabel, string? batchDescription)
        {
            if (items == null || items.Count == 0)
                return Result.Fail("需求列表不能为空");

            var batchId = Guid.NewGuid().ToString("N")[..12];
            int sort = 0;
            foreach (var item in items)
            {
                // 下载外部图片到本地
                var content = await DownloadExternalImages(item.Content);

                var req = new Requirement
                {
                    ProjectId = projectId,
                    Title = item.Title ?? "未命名需求",
                    Content = content,
                    Type = Enum.TryParse<RequirementType>(item.Type, true, out var t) ? t : RequirementType.Feature,
                    Complexity = Enum.TryParse<Complexity>(item.Complexity, true, out var c) ? c : Complexity.Medium,
                    Status = RequirementStatus.Draft,
                    Source = RequirementSource.AI,
                    IsAiFriendly = true,
                    IsConfirmed = false,
                    BatchId = batchId,
                    BatchLabel = batchLabel,
                    BatchDescription = batchDescription,
                    Sort = sort++,
                    CreatedTime = DateTime.Now,
                };
                this.Insert(req);

                // 生成预览 MD 文档
                _docVersionService.SaveVersion("Requirement", req.Id, DocVersionService.BuildRequirementMd(req), "AI 提交", "ApiKey");
            }
            return Result.Success("提交成功", new { batchId, count = items.Count });
        }

        /// <summary>获取分析批次列表（按 BatchId 聚合）</summary>
        public Result GetAnalysisBatchList(int? projectId, int page = 1, int pageSize = 20)
        {
            var batchIds = _fsql.Select<Requirement>()
                .DisableGlobalFilter("IsDeleted")
                .Where(c => c.BatchId != null && c.BatchId != "")
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .GroupBy(c => c.BatchId)
                .OrderByDescending(g => g.Max(g.Value.CreatedTime))
                .Count(out var total)
                .Page(page, pageSize)
                .ToList(g => g.Key);

            var result = new List<object>();
            foreach (var bid in batchIds)
            {
                var items = _fsql.Select<Requirement>()
                    .DisableGlobalFilter("IsDeleted")
                    .Include(c => c.Project)
                    .Where(c => c.BatchId == bid)
                    .ToList();

                var first = items.FirstOrDefault();
                var active = items.Where(i => !i.IsDeleted).ToList();
                string status = active.Count == 0 ? "Discarded"
                    : active.All(i => i.IsConfirmed) ? "Confirmed"
                    : active.Any(i => i.IsConfirmed) ? "Partial"
                    : "Pending";

                result.Add(new
                {
                    batchId = bid,
                    projectId = first?.ProjectId,
                    projectName = first?.Project?.Name,
                    batchLabel = first?.BatchLabel,
                    batchDescription = first?.BatchDescription,
                    requirementCount = items.Count,
                    confirmedCount = items.Count(i => i.IsConfirmed),
                    deletedCount = items.Count(i => i.IsDeleted),
                    createdTime = items.Min(i => i.CreatedTime),
                    status,
                });
            }
            return Result.Page(result, total);
        }

        /// <summary>向已有批次追加单条需求</summary>
        public Result AddToExistingBatch(string batchId, string title, string? content, string? type)
        {
            var existing = Select.Where(c => c.BatchId == batchId).First();
            if (existing == null) return Result.Fail("批次不存在");

            var maxSort = Select.Where(c => c.BatchId == batchId).Max(c => c.Sort);
            var req = new Requirement
            {
                ProjectId = existing.ProjectId,
                Title = title,
                Content = content,
                Type = Enum.TryParse<RequirementType>(type, true, out var t) ? t : RequirementType.Feature,
                Status = RequirementStatus.Draft,
                Source = RequirementSource.AI,
                IsAiFriendly = true,
                IsConfirmed = false,
                BatchId = batchId,
                BatchLabel = existing.BatchLabel,
                BatchDescription = existing.BatchDescription,
                Sort = maxSort + 1,
                CreatedTime = DateTime.Now,
            };
            this.Insert(req);
            return Result.Success("添加成功", new { id = req.Id });
        }

        /// <summary>下载 Markdown 内容中的外部图片到本地，替换为本地路径</summary>
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly Regex _imageRegex = new(@"!\[([^\]]*)\]\((https?://[^)]+)\)", RegexOptions.Compiled);

        private async Task<string?> DownloadExternalImages(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return content;

            var matches = _imageRegex.Matches(content);
            if (matches.Count == 0) return content;

            var uploadPath = AppGlobal.AppSettings.UploadConfig.UploadPath;
            var savePath = Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot{uploadPath}");
            if (!Directory.Exists(savePath))
                Directory.CreateDirectory(savePath);

            var result = content;
            foreach (Match match in matches)
            {
                var externalUrl = match.Groups[2].Value;
                try
                {
                    var response = await _httpClient.GetAsync(externalUrl);
                    if (!response.IsSuccessStatusCode) continue;

                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                    var ext = contentType switch
                    {
                        "image/png" => ".png",
                        "image/gif" => ".gif",
                        "image/webp" => ".webp",
                        "image/bmp" => ".bmp",
                        _ => ".jpg",
                    };

                    var fileName = Guid.NewGuid().ToString("N") + ext;
                    var filePath = Path.Combine(savePath, fileName);
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(filePath, bytes);

                    var localUrl = $"{uploadPath}{fileName}";
                    result = result.Replace(externalUrl, localUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "下载外部图片失败: {Url}", externalUrl);
                }
            }
            return result;
        }

        #endregion

        #region 关联进度自动派生(#95)

        /// <summary>
        /// 按任务完成度自动派生需求状态和工时汇总。
        /// 全部 Success → Done;有 Running/Success → InProgress;全 Idle → Todo。
        /// 可在 TaskItem CRUD 后调用,也可前端/定时手动触发。
        /// </summary>
        public Result SyncStatusFromTasks(int? projectId = null)
        {
            var reqQuery = projectId.HasValue
                ? Select.Where(r => r.ProjectId == projectId.Value)
                : Select;
            var reqs = reqQuery.ToList(r => r.Id);
            if (reqs.Count == 0) return Result.Success("无需更新");

            // FreeSql GroupBy+Sum 不支持三元 — 改为先拉列表再 LINQ 聚合
            var allTasks = _fsql.Select<TaskItem>()
                .Where(t => reqs.Contains(t.RequirementId!.Value) && !t.IsDeleted)
                .ToList(t => new { t.RequirementId, t.ExecStatus, t.EstimatedHours, t.ActualHours });

            var taskStats = allTasks.GroupBy(t => t.RequirementId!.Value).Select(g => new
            {
                ReqId = g.Key,
                Total = g.Count(),
                Done = g.Count(t => t.ExecStatus == TaskItemExecStatus.Success),
                Running = g.Count(t => t.ExecStatus == TaskItemExecStatus.Running),
                EstHours = g.Sum(t => t.EstimatedHours ?? 0m),
                ActHours = g.Sum(t => t.ActualHours ?? 0m),
            }).ToList();

            int updated = 0;
            foreach (var s in taskStats)
            {
                var newStatus = s.Total == 0 ? (RequirementStatus?)null
                    : s.Done == s.Total ? RequirementStatus.Done
                    : (s.Done + s.Running) > 0 ? RequirementStatus.InProgress
                    : RequirementStatus.Todo;

                if (newStatus == null) continue;
                _fsql.Update<Requirement>().Where(r => r.Id == s.ReqId)
                    .Set(r => r.EstimatedHours, (decimal)s.EstHours)
                    .Set(r => r.ActualHours, (decimal)s.ActHours)
                    .Set(r => r.Status, newStatus.Value)
                    .Set(r => r.UpdatedTime, DateTime.Now)
                    .ExecuteAffrows();
                updated++;
            }
            return Result.Success($"已同步 {updated} 条需求状态");
        }

        #endregion
    }

    public class SubmitAnalysisItem
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Type { get; set; }
        public string? Complexity { get; set; }
    }
}

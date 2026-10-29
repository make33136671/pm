using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Ai;
using System.Text;
using System.Text.Json;

namespace Sb.Apps.Pm.WebApi.Controllers.FeatureModules
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
    public class FeatureModuleController : Controller
    {
        private readonly FeatureModuleService _service;
        private readonly RequirementService _requirementService;
        private readonly AiProviderService _aiProviderService;
        private readonly AiProviderFactory _aiFactory;

        public FeatureModuleController(FeatureModuleService service, RequirementService requirementService,
            AiProviderService aiProviderService, AiProviderFactory aiFactory)
        {
            _service = service;
            _requirementService = requirementService;
            _aiProviderService = aiProviderService;
            _aiFactory = aiFactory;
        }

        [HttpGet]
        public Result Tree(int projectId)
        {
            return _service.GetTree(projectId);
        }

        [HttpGet]
        public Result List(int projectId, string field = "", string order = "")
        {
            return _service.GetList(projectId, field, order);
        }

        [HttpGet]
        public Result Detail(int id)
        {
            return _service.GetNodeDetail(id);
        }

        [HttpPost]
        public Result Add([FromForm] FeatureModule module)
        {
            return _service.Create(module);
        }

        [HttpPost]
        public Result Edit([FromForm] FeatureModule module)
        {
            return _service.Edit(module);
        }

        [HttpPost]
        public Result Del([FromForm] int id)
        {
            return _service.Del(id);
        }

        /// <summary>
        /// 保存需求的功能模块关联
        /// </summary>
        [HttpPost]
        public Result SaveRelations(int requirementId, [FromBody] List<int> featureModuleIds)
        {
            return _service.SaveRelations(requirementId, featureModuleIds);
        }

        /// <summary>
        /// 获取需求的关联模块 ID 列表
        /// </summary>
        [HttpGet]
        public Result GetRelations(int requirementId)
        {
            return _service.GetRelations(requirementId);
        }

        /// <summary>
        /// 手动同步功能地图导图
        /// </summary>
        [HttpPost]
        public Result SyncMap([FromForm] int projectId)
        {
            return _service.SyncFeatureMap(projectId);
        }

        /// <summary>
        /// 从导图编辑回写到功能模块表（双向编辑）
        /// </summary>
        [HttpPost]
        public Result SaveFromMap(int projectId, [FromBody] string mapData)
        {
            return _service.SaveFromMap(projectId, mapData);
        }

        /// <summary>AI 生成需求（非流式，同步等待）</summary>
        [HttpPost]
        public async Task<Result> GenerateRequirements([FromBody] GenerateRequirementsRequest req)
        {
            var config = _aiProviderService.Select.Where(c => c.Id == req.AiProviderId).First();
            if (config == null) return Result.Fail("AI 提供商不存在!");

            var adapter = _aiFactory.GetAdapter(config.ProviderType);
            if (adapter == null) return Result.Fail($"未找到适配器: {config.ProviderType}");

            var modules = _service.Select.Where(c => req.FeatureModuleIds.Contains(c.Id)).ToList();
            if (modules.Count == 0) return Result.Fail("未找到选中的功能模块!");

            var batchId = Guid.NewGuid().ToString("N")[..16];
            var requirements = new List<Requirement>();

            foreach (var module in modules)
            {
                var prompt = BuildRequirementPrompt(module, req.AiFriendly);
                var result = await adapter.GenerateAsync(config, prompt);
                var parsed = ParseRequirements(result, module, batchId, req.AiFriendly, config.Id);
                requirements.AddRange(parsed);
            }

            // 写入暂存需求
            foreach (var r in requirements)
                _requirementService.Insert(r);

            // 创建关联
            CreateRelations(requirements, modules);

            return Result.Success(new { batchId, count = requirements.Count, requirements });
        }

        /// <summary>AI 生成需求（SSE 流式）</summary>
        [HttpGet]
        public async Task StreamGenerateRequirements(string featureModuleIds, int aiProviderId, bool aiFriendly, CancellationToken ct)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";

            var config = _aiProviderService.Select.Where(c => c.Id == aiProviderId).First();
            if (config == null) { await WriteSseEvent("error", new { message = "AI 提供商不存在!" }); return; }

            var adapter = _aiFactory.GetAdapter(config.ProviderType);
            if (adapter == null) { await WriteSseEvent("error", new { message = $"未找到适配器: {config.ProviderType}" }); return; }
            if (!adapter.SupportStream) { await WriteSseEvent("error", new { message = "该 AI 提供商不支持流式输出" }); return; }

            var ids = featureModuleIds.Split(',').Select(int.Parse).ToList();
            var modules = _service.Select.Where(c => ids.Contains(c.Id)).ToList();

            var batchId = Guid.NewGuid().ToString("N")[..16];
            var allRequirements = new List<Requirement>();
            int current = 0;

            foreach (var module in modules)
            {
                current++;
                await WriteSseEvent("progress", new { current, total = modules.Count, moduleId = module.Id, moduleName = module.Name });

                var prompt = BuildRequirementPrompt(module, aiFriendly);
                var fullContent = new StringBuilder();

                try
                {
                    await foreach (var chunk in adapter.StreamGenerateAsync(config, prompt, ct))
                    {
                        fullContent.Append(chunk);
                        await WriteSseEvent("chunk", new { moduleId = module.Id, text = chunk });
                    }

                    var parsed = ParseRequirements(fullContent.ToString(), module, batchId, aiFriendly, config.Id);
                    foreach (var r in parsed)
                    {
                        _requirementService.Insert(r);
                        allRequirements.Add(r);
                        await WriteSseEvent("requirement", new { moduleId = module.Id, r.Id, r.Title, r.Content, r.Type });
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    await WriteSseEvent("error", new { moduleId = module.Id, message = ex.Message });
                }
            }

            CreateRelations(allRequirements, modules);
            await WriteSseEvent("done", new { batchId, count = allRequirements.Count });
        }

        #region AI 生成辅助

        private static string BuildRequirementPrompt(FeatureModule module, bool aiFriendly)
        {
            var friendlyHint = aiFriendly
                ? "\n\n请以 AI 友好的方式编写需求，确保描述清晰、结构化、无歧义，便于 AI Agent 直接理解和执行。"
                : "";

            return $"请根据以下功能模块信息，生成详细的需求列表。每个需求用 JSON 数组格式返回。\n\n" +
                   $"功能模块名称：{module.Name}\n" +
                   $"功能模块描述：{module.Description ?? "无"}\n" +
                   friendlyHint +
                   "\n\n请返回 JSON 数组格式：[{\"title\": \"需求标题\", \"content\": \"详细描述\", \"type\": \"Feature\"}]\n" +
                   "type 可选值：Feature, Bugfix, Improvement, Task, Research";
        }

        private List<Requirement> ParseRequirements(string aiOutput, FeatureModule module, string batchId, bool aiFriendly, int aiProviderId)
        {
            var requirements = new List<Requirement>();

            try
            {
                // 尝试从 AI 输出中提取 JSON 数组
                var jsonStart = aiOutput.IndexOf('[');
                var jsonEnd = aiOutput.LastIndexOf(']');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonStr = aiOutput[jsonStart..(jsonEnd + 1)];
                    var items = JsonSerializer.Deserialize<List<RequirementGenItem>>(jsonStr,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            requirements.Add(new Requirement
                            {
                                ProjectId = module.ProjectId,
                                Title = item.Title ?? module.Name,
                                Content = item.Content,
                                Type = Enum.TryParse<RequirementType>(item.Type, true, out var t) ? t : RequirementType.Feature,
                                    Status = RequirementStatus.Draft,
                                Source = RequirementSource.AI,
                                IsAiFriendly = aiFriendly,
                                IsConfirmed = false,
                                BatchId = batchId,
                                AiProviderId = aiProviderId,
                                CreatedTime = AppTime.Now,
                            });
                        }
                    }
                }
            }
            catch
            {
                // JSON 解析失败，创建一个包含原始内容的需求
            }

            // 如果没有解析出任何需求，把原始内容作为单条需求
            if (requirements.Count == 0)
            {
                requirements.Add(new Requirement
                {
                    ProjectId = module.ProjectId,
                    Title = $"{module.Name} - AI 生成需求",
                    Content = aiOutput,
                    Type = RequirementType.Feature,
                    Status = RequirementStatus.Draft,
                    Source = RequirementSource.AI,
                    IsAiFriendly = aiFriendly,
                    IsConfirmed = false,
                    BatchId = batchId,
                    AiProviderId = aiProviderId,
                    CreatedTime = AppTime.Now,
                });
            }

            return requirements;
        }

        private void CreateRelations(List<Requirement> requirements, List<FeatureModule> modules)
        {
            foreach (var req in requirements)
            {
                // 根据 ProjectId 匹配关联的模块
                var relatedModules = modules.Where(m => m.ProjectId == req.ProjectId).ToList();
                if (relatedModules.Count > 0)
                {
                    _service.SaveRelations(req.Id, relatedModules.Select(m => m.Id).ToList());
                }
            }
        }

        private async Task WriteSseEvent(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(data);
            await Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n");
            await Response.Body.FlushAsync();
        }

        #endregion
    }

    public class GenerateRequirementsRequest
    {
        public List<int> FeatureModuleIds { get; set; } = new();
        public int AiProviderId { get; set; }
        public bool AiFriendly { get; set; }
    }

    public class RequirementGenItem
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Type { get; set; }
    }
}

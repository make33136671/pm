using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.WebApi.Controllers.Ai
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
    public class AiController : Controller
    {
        private readonly IAiService _aiService;
        private readonly AiContextBuilder _contextBuilder;
        private readonly IFreeSql _fsql;
        private readonly AiSearchService _searchService;

        public AiController(IAiService aiService, AiContextBuilder contextBuilder, IFreeSql fsql, AiSearchService searchService)
        {
            _aiService = aiService;
            _contextBuilder = contextBuilder;
            _fsql = fsql;
            _searchService = searchService;
        }

        [HttpPost]
        public async Task<Result> AnalyzeCode([FromBody] AiChatInput input)
        {
            var context = await BuildContextAsync(input);
            var result = await _aiService.AnalyzeCode(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "分析失败");
        }

        [HttpPost]
        public async Task<Result> GenerateCode([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = await _aiService.GenerateCode(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "生成失败");
        }

        [HttpPost]
        public async Task<Result> DesignFeature([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = await _aiService.DesignFeature(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "设计失败");
        }

        [HttpPost]
        public async Task<Result> ReviewCode([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = await _aiService.ReviewCode(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "Review 失败");
        }

        [HttpPost]
        public async Task<Result> Chat([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = await _aiService.Chat(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "对话失败");
        }

        /// <summary>
        /// 统一入口：根据 taskType 分发
        /// </summary>
        [HttpPost]
        public async Task<Result> Execute([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = input.TaskType?.ToLower() switch
            {
                "analyze" or "analyzecode" => await _aiService.AnalyzeCode(context),
                "generate" or "generatecode" => await _aiService.GenerateCode(context),
                "design" or "designfeature" => await _aiService.DesignFeature(context),
                "review" or "reviewcode" => await _aiService.ReviewCode(context),
                _ => await _aiService.Chat(context),
            };
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "执行失败");
        }

        /// <summary>从项目文档中提取隐含的功能需求点。</summary>
        [HttpPost]
        public async Task<Result> ExtractFeatureRequirements([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = await _aiService.ExtractFeatureRequirements(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "提取失败");
        }

        /// <summary>分析任务执行状态，生成进度概览报告。</summary>
        [HttpPost]
        public async Task<Result> AnalyzeTaskProgress([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = await _aiService.AnalyzeTaskProgress(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "分析失败");
        }

        /// <summary>对比文档与需求，分析需求覆盖情况。</summary>
        [HttpPost]
        public async Task<Result> AnalyzeRequirementCoverage([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = await _aiService.AnalyzeRequirementCoverage(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "分析失败");
        }

        /// <summary>分析项目文档优缺点，给出优化建议。</summary>
        [HttpPost]
        public async Task<Result> AnalyzeDocuments([FromBody] AiChatInput input)
        {
            var context = BuildContext(input);
            var result = await _aiService.AnalyzeDocuments(context);
            return result.Success ? Result.Success(result) : Result.Fail(result.Error ?? "分析失败");
        }

        /// <summary>AI 搜索意图解析：将自然语言搜索词解析为结构化过滤条件。scope=requirement|task</summary>
        [HttpGet]
        public Task<Result> ParseSearchIntent([FromQuery] string query, [FromQuery] string scope = "requirement")
            => _searchService.ParseIntent(query, scope);

        /// <summary>
        /// 获取任务历史
        /// </summary>
        [HttpGet]
        public Result TaskHistory(int? projectId = null, int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<AiTask>()
                .WhereIf(projectId.HasValue, t => t.ProjectId == projectId.Value)
                .OrderByDescending(t => t.CreatedTime);

            var total = query.Count();
            var items = query.Page(page, pageSize).ToList().Select(t => new
            {
                t.Id,
                t.ProjectId,
                t.RequirementId,
                t.TaskType,
                t.Status,
                t.InputData,
                t.OutputData,
                t.AiModel,
                t.TokensUsed,
                t.ErrorMessage,
                t.StartedAt,
                t.FinishedAt,
                t.CreatedTime,
                t.OperatorName,
            });

            return Result.Page(items, total);
        }

        #region 辅助

        private AiContext BuildContext(AiChatInput input)
        {
            AiContext context;
            if (input.SubProjectId.HasValue)
                context = _contextBuilder.FromSubProject(input.SubProjectId.Value, input.Instruction);
            else if (input.RequirementId.HasValue)
                context = _contextBuilder.FromRequirement(input.RequirementId.Value, input.Instruction);
            else
            {
                context = new AiContext { ProjectId = input.ProjectId, Instruction = input.Instruction };
                if (input.ProjectId.HasValue)
                {
                    var project = _fsql.Select<Project>().Where(p => p.Id == input.ProjectId.Value).First();
                    context.ProjectName = project?.Name;
                }
            }
            if (input.ProjectId.HasValue) context.ProjectId = input.ProjectId;
            if (input.FocusPaths?.Count > 0) context.FocusPaths = input.FocusPaths;
            if (input.ConversationHistory?.Count > 0)
                context.ConversationHistory = input.ConversationHistory
                    .Select(m => new AiMessage { Role = m.Role, Content = m.Content }).ToList();
            return context;
        }

        /// <summary>代码类任务额外注入 Git 信息（异步）。</summary>
        private async Task<AiContext> BuildContextAsync(AiChatInput input)
        {
            var context = BuildContext(input);
            await _contextBuilder.EnrichWithGitInfo(context);
            return context;
        }

        #endregion
    }

    public class AiChatInput
    {
        public int? ProjectId { get; set; }
        public int? SubProjectId { get; set; }
        public int? RequirementId { get; set; }
        public string Instruction { get; set; } = "";
        public string? TaskType { get; set; }
        public List<string>? FocusPaths { get; set; }
        public List<AiChatMessage>? ConversationHistory { get; set; }
    }

    public class AiChatMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";
    }
}

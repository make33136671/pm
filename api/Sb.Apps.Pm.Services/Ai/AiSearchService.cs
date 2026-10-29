using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 搜索意图解析服务。
    /// 将自然语言搜索词解析为结构化过滤条件,供需求/任务列表等前端控件直接使用。
    /// 无 AI Provider 时退化为纯关键字搜索(keyword 透传)。
    /// </summary>
    [AutoDependencyInjection]
    public class AiSearchService
    {
        private readonly IAiProvider? _provider;
        private readonly ILogger<AiSearchService> _logger;

        public AiSearchService(ILogger<AiSearchService> logger, IAiProvider? provider = null)
        {
            _logger = logger;
            _provider = provider;
        }

        /// <summary>
        /// 解析搜索意图,返回结构化过滤条件。
        /// 示例输入 "未完成的 Bug 需求" → { type:"Bugfix", status:"Draft|Todo|InProgress", keyword:"" }
        /// </summary>
        public async Task<Result> ParseIntent(string query, string scope = "requirement")
        {
            if (string.IsNullOrWhiteSpace(query))
                return Result.Fail("搜索词不能为空");

            if (_provider == null)
            {
                // 无 AI：keyword 透传
                return Result.Success(new SearchIntent { Keyword = query, Scope = scope, Raw = query });
            }

            var prompt = BuildPrompt(query, scope);
            try
            {
                var resp = await _provider.SendAsync(new AiRequest
                {
                    SystemPrompt = "你是搜索意图解析器，只返回 JSON 对象，不要包含其他内容。",
                    Messages = new List<AiMessage> { new() { Role = "user", Content = prompt } },
                    MaxTokens = 512,
                    Temperature = 0.1f,
                });

                if (!resp.Success)
                    return Result.Success(new SearchIntent { Keyword = query, Scope = scope, Raw = query });

                var json = resp.Content.Trim();
                if (json.StartsWith("```"))
                    json = string.Join('\n', json.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")));

                var intent = JsonSerializer.Deserialize<SearchIntent>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new SearchIntent { Keyword = query };

                intent.Scope = scope;
                intent.Raw = query;
                return Result.Success(intent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI 搜索意图解析失败,退化为关键字搜索");
                return Result.Success(new SearchIntent { Keyword = query, Scope = scope, Raw = query });
            }
        }

        private static string BuildPrompt(string query, string scope)
        {
            var fields = scope switch
            {
                "task" => @"- keyword: 关键字(字符串)
- type: 任务类型 Development/Bug/Design/Testing/Docs/Other(可为 null)
- complexity: 复杂度 Low/Medium/High(可为 null)
- execStatus: 执行状态 Idle/Running/Success/Failed(可为 null,多个用|分隔)",
                _ => @"- keyword: 关键字(字符串)
- type: 需求类型 Feature/Bugfix/Improvement/Task/Research(可为 null)
- status: 需求状态 Draft/Todo/InProgress/Testing/Done/Closed(可为 null,多个用|分隔)
- complexity: 复杂度 Low/Medium/High/VeryHigh(可为 null)",
            };

            return $@"请解析以下搜索词的意图，提取结构化过滤条件。

搜索词：{query}

请返回 JSON 对象，包含以下字段：
{fields}

示例：""未完成的功能需求"" → {{""keyword"":"""",""type"":""Feature"",""status"":""Draft|Todo|InProgress""}}
示例：""高复杂度的 bug"" → {{""keyword"":"""",""type"":""Bugfix"",""complexity"":""High""}}
示例：""登录相关"" → {{""keyword"":""登录""}}";
        }
    }

    public class SearchIntent
    {
        public string Keyword { get; set; } = "";
        public string? Type { get; set; }
        public string? Status { get; set; }
        public string? Complexity { get; set; }
        public string? ExecStatus { get; set; }
        public string Scope { get; set; } = "requirement";
        public string Raw { get; set; } = "";
    }
}

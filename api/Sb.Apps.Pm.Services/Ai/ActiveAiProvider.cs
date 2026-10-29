using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// 活跃 AI Provider — 从数据库中读取 Active 状态的 AiProvider 配置，
    /// 通过 AiProviderFactory 获取对应适配器，完成 AI 请求。
    /// 这是 IAiProvider 的唯一实现，供 DefaultAiService 注入使用。
    /// </summary>
    [AutoDependencyInjection]
    public class ActiveAiProvider : IAiProvider
    {
        private readonly IFreeSql _fsql;
        private readonly AiProviderFactory _factory;
        private readonly ILogger<ActiveAiProvider> _logger;

        public string Name => "ActiveProvider";

        public ActiveAiProvider(IFreeSql fsql, AiProviderFactory factory, ILogger<ActiveAiProvider> logger)
        {
            _fsql = fsql;
            _factory = factory;
            _logger = logger;
        }

        /// <summary>
        /// 获取当前活跃的 AiProvider 配置（优先 IsDefault，其次第一个 Active）
        /// </summary>
        private Sb.Apps.Pm.Models.Ai.AiProvider? GetActiveProvider()
        {
            // 优先取 IsDefault=true 的
            var provider = _fsql.Select<Sb.Apps.Pm.Models.Ai.AiProvider>()
                .Where(p => p.Status == AiProviderStatus.Active)
                .ToList();

            if (provider.Count == 0)
                return null;

            // 优先 IsDefault
            var def = provider.FirstOrDefault(p => p.IsDefault);
            return def ?? provider.First();
        }

        public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken ct = default)
        {
            var provider = GetActiveProvider();
            if (provider == null)
            {
                _logger.LogWarning("没有可用的 AI Provider");
                return new AiResponse
                {
                    Success = false,
                    Error = "没有可用的 AI Provider",
                };
            }

            var adapter = _factory.GetAdapter(provider.ProviderType);
            if (adapter == null)
            {
                _logger.LogWarning("ProviderType {Type} 没有对应的适配器", provider.ProviderType);
                return new AiResponse
                {
                    Success = false,
                    Error = $"ProviderType {provider.ProviderType} 没有对应的适配器",
                };
            }

            try
            {
                // 构建完整 prompt：SystemPrompt + 对话消息
                var promptParts = new System.Collections.Generic.List<string>();

                if (!string.IsNullOrEmpty(request.SystemPrompt))
                    promptParts.Add($"<system>\n{request.SystemPrompt}\n</system>");

                foreach (var msg in request.Messages)
                {
                    promptParts.Add($"<{msg.Role}>\n{msg.Content}\n</{msg.Role}>");
                }

                var prompt = string.Join("\n\n", promptParts);

                _logger.LogInformation("调用 AI Provider: {Name} ({Model})", provider.Name, provider.ModelName);

                var result = await adapter.GenerateAsync(provider, prompt, ct);

                // 解析 token 估算（粗略）
                var inputTokens = prompt.Length / 4;
                var outputTokens = result.Length / 4;

                return new AiResponse
                {
                    Success = true,
                    Content = result,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    ModelUsed = provider.ModelName,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Provider 调用失败: {Name}", provider.Name);
                return new AiResponse
                {
                    Success = false,
                    Error = $"AI 调用失败: {ex.Message}",
                };
            }
        }
    }
}

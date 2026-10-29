using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// llama.cpp 适配器 —— 通过 OpenAI 兼容 /v1/chat/completions 调用本地大模型。
    /// llama.cpp 在 192.168.10.80:8080 运行，支持流式输出。
    /// 地址/modelName 从 AiProvider 配置读取，运营可在 Admin UI 调整无需改代码。
    /// </summary>
    [AutoDependencyInjection]
    public class LlamaAiProviderAdapter : IAiProviderAdapter
    {
        private readonly ILogger<LlamaAiProviderAdapter> _logger;
        private readonly HttpClient? _testClient;

        public string ProviderType => "Llama";
        public string DisplayName => "Llama.cpp (本地)";
        public bool SupportStream => true;

        public LlamaAiProviderAdapter(ILogger<LlamaAiProviderAdapter> logger)
        {
            _logger = logger;
        }

        /// <summary>Test-only constructor: inject a pre-configured HttpClient with a mock handler.</summary>
        public LlamaAiProviderAdapter(HttpClient testClient, ILogger<LlamaAiProviderAdapter> logger)
        {
            _logger = logger;
            _testClient = testClient;
        }

        private HttpClient CreateHttpClient(AiProvider config)
        {
            if (_testClient is not null)
                return _testClient;

            // llama.cpp 默认无认证，但如果配了 ApiKey 也带上
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120),
            };

            var endpoint = config.Endpoint;
            if (!string.IsNullOrEmpty(endpoint))
            {
                client.BaseAddress = new Uri(endpoint);
            }
            else
            {
                _logger.LogWarning("AiProvider#{Id} Endpoint is empty — llama.cpp calls will fail", config.Id);
            }

            var apiKey = config.ApiKey;
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            return client;
        }

        public async Task<string> GenerateAsync(AiProvider config, string prompt, CancellationToken ct = default)
        {
            var extra = ParseExtra(config);
            var body = BuildRequestBody(config, extra, prompt, stream: false);

            using var http = CreateHttpClient(config);
            using var resp = await http.PostAsJsonAsync("/v1/chat/completions", body, ct);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<LlamaChatCompletionResponse>(cancellationToken: ct);
            return result?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        }

        public async IAsyncEnumerable<string> StreamGenerateAsync(
            AiProvider config, string prompt,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var extra = ParseExtra(config);
            var body = BuildRequestBody(config, extra, prompt, stream: true);

            using var http = CreateHttpClient(config);
            using var resp = await http.PostAsJsonAsync("/v1/chat/completions", body, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                if (line.StartsWith(":")) continue;              // SSE keep-alive
                if (!line.StartsWith("data: ")) continue;

                var data = line.Substring(6);
                if (data == "[DONE]") break;

                var content = TryExtractChunkContent(config, data);
                if (!string.IsNullOrEmpty(content))
                    yield return content;
            }
        }

        // ---- 内部辅助 ---------------------------------------------------

        internal static LlamaExtraConfig ParseExtra(AiProvider config, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(config.ExtraConfig))
                return new LlamaExtraConfig();
            try
            {
                return JsonSerializer.Deserialize<LlamaExtraConfig>(config.ExtraConfig)
                       ?? new LlamaExtraConfig();
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex,
                    "AiProvider#{Id} ExtraConfig parse failed, using defaults",
                    config.Id);
                return new LlamaExtraConfig();
            }
        }

        private LlamaExtraConfig ParseExtra(AiProvider config) => ParseExtra(config, _logger);

        private static object BuildRequestBody(AiProvider config, LlamaExtraConfig extra, string prompt, bool stream)
        {
            // llama.cpp OpenAI 兼容接口使用 /v1/chat/completions，格式与 OpenAI 一致
            var messages = new[]
            {
                new { role = "system", content = extra.SystemPrompt ?? "你是一个专业的技术助手。" },
                new { role = "user",   content = prompt },
            };

            if (stream)
            {
                return new
                {
                    model = config.ModelName ?? "default",
                    messages,
                    stream = true,
                    temperature = extra.Temperature ?? 0.3f,
                };
            }

            return new
            {
                model = config.ModelName ?? "default",
                messages,
                max_tokens = extra.MaxTokens ?? 4096,
                temperature = extra.Temperature ?? 0.3f,
            };
        }

        internal string? TryExtractChunkContent(AiProvider config, string data)
        {
            try
            {
                var chunk = JsonSerializer.Deserialize<LlamaSseChunk>(data);
                return chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "llama.cpp SSE chunk parse failed (provider#{Id}, len={Len}): {Snippet}",
                    config.Id, data.Length,
                    data.Length > 200 ? data.Substring(0, 200) + "…" : data);
                return null;
            }
        }
    }

    // ---- DTO --------------------------------------------------------

    /// <summary>AiProvider.ExtraConfig 反序列化目标 (JSON 字段)</summary>
    public sealed class LlamaExtraConfig
    {
        [JsonPropertyName("systemPrompt")] public string? SystemPrompt { get; set; }
        [JsonPropertyName("maxTokens")] public int? MaxTokens { get; set; }
        [JsonPropertyName("temperature")] public float? Temperature { get; set; }
    }

    /// <summary>OpenAI 兼容同步响应</summary>
    internal sealed class LlamaChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<LlamaChoice>? Choices { get; set; }
    }

    internal sealed class LlamaChoice
    {
        [JsonPropertyName("message")] public LlamaMessage? Message { get; set; }
        [JsonPropertyName("delta")] public LlamaMessage? Delta { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    internal sealed class LlamaMessage
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }

    /// <summary>SSE 流式分块 (data: {...}\n\n)</summary>
    internal sealed class LlamaSseChunk
    {
        [JsonPropertyName("choices")] public List<LlamaChoice>? Choices { get; set; }
    }
}

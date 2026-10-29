using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sb.Apps.Pm.Models.Ai;
using Sb.Apps.Pm.Services.Ai;
using Xunit;

namespace Sb.Apps.Pm.Services.Tests.Ai;

/// <summary>
/// HermesAiProviderAdapter HTTP 链路集成测试 (2026-05-31, v2.1):
/// 用 MockHttpMessageHandler 拦截请求,验证完整 JSON 请求 / SSE 解析 / 异常路径,
/// 不依赖真实 Hermes Gateway,可在 CI 重复执行。
/// </summary>
public class HermesAiProviderAdapterIntegrationTests
{
    private static AiProvider P(string? extra = null) => new()
    {
        Id = 99, Name = "test", ProviderType = "Hermes", ModelName = "qwen-7b",
        ExtraConfig = extra,
    };

    private static (HermesAiProviderAdapter adapter, MockHandler mock) Make()
    {
        var mock = new MockHandler();
        var client = new HttpClient(mock, disposeHandler: false)
        {
            BaseAddress = new Uri("http://stub.invalid/")
        };
        var adapter = new HermesAiProviderAdapter(client, NullLogger<HermesAiProviderAdapter>.Instance);
        return (adapter, mock);
    }

    // ---- 同步 ------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_HappyPath_ReturnsContent_AndSendsCorrectBody()
    {
        var (adapter, mock) = Make();
        mock.RespondJson(HttpStatusCode.OK, """
        {
          "id": "x",
          "object": "chat.completion",
          "choices": [{"message":{"role":"assistant","content":"hi there"},"finish_reason":"stop"}]
        }
        """);

        var result = await adapter.GenerateAsync(P("""{"systemPrompt":"sys","maxTokens":256,"temperature":0.5}"""),
            "你好");

        result.Should().Be("hi there");

        // 验证请求 body (System.Text.Json 默认会将非 ASCII 字符 escape 为 \uXXXX,功能上等价)
        var body = mock.LastRequestBody!;
        body.Should().Contain("\"model\":\"qwen-7b\"");
        body.Should().Contain("\"max_tokens\":256");
        body.Should().Contain("\"temperature\":0.5");
        body.Should().Contain("\"role\":\"system\"");
        body.Should().Contain("\"content\":\"sys\"");
        body.Should().Contain("\\u4F60\\u597D");   // "你好" 的 Unicode escape 形式
        mock.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/chat/completions");
        mock.LastRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task GenerateAsync_NoExtraConfig_UsesDefaults()
    {
        var (adapter, mock) = Make();
        mock.RespondJson(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}""");

        await adapter.GenerateAsync(P(null), "ping");

        var body = mock.LastRequestBody!;
        body.Should().Contain("\"max_tokens\":4096");        // 默认值
        body.Should().Contain("\"temperature\":0.3");        // 默认值
        body.Should().Contain("\\u4F60\\u662F");             // "你是" 的 Unicode escape — 默认 systemPrompt
    }

    [Fact]
    public async Task GenerateAsync_EmptyChoices_ReturnsEmptyString()
    {
        var (adapter, mock) = Make();
        mock.RespondJson(HttpStatusCode.OK, """{"choices":[]}""");

        var result = await adapter.GenerateAsync(P(), "x");
        result.Should().Be("");
    }

    [Fact]
    public async Task GenerateAsync_5xx_ThrowsHttpRequestException()
    {
        var (adapter, mock) = Make();
        mock.Respond(HttpStatusCode.InternalServerError, "boom");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => adapter.GenerateAsync(P(), "x"));
    }

    [Fact]
    public async Task GenerateAsync_4xx_ThrowsHttpRequestException_NotRetried()
    {
        // 注意:此测试不经过 Polly retry(直接用 HttpClient),所以单次 4xx 即抛
        var (adapter, mock) = Make();
        mock.Respond(HttpStatusCode.BadRequest, "bad model");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => adapter.GenerateAsync(P(), "x"));

        mock.CallCount.Should().Be(1);                       // 单次 (Polly 不在测试链路上)
    }

    // ---- 流式 ------------------------------------------------------

    [Fact]
    public async Task StreamGenerateAsync_MultipleChunks_YieldsContentInOrder()
    {
        var (adapter, mock) = Make();
        var sse = string.Join("\n",
            """data: {"choices":[{"delta":{"role":"assistant","content":"hello"}}]}""",
            """data: {"choices":[{"delta":{"content":" "}}]}""",
            """data: {"choices":[{"delta":{"content":"world"}}]}""",
            "data: [DONE]",
            "");
        mock.Respond(HttpStatusCode.OK, sse, contentType: "text/event-stream");

        var collected = new List<string>();
        await foreach (var c in adapter.StreamGenerateAsync(P(), "x"))
            collected.Add(c);

        collected.Should().Equal("hello", " ", "world");

        // 流式请求体应含 "stream":true
        mock.LastRequestBody!.Should().Contain("\"stream\":true");
    }

    [Fact]
    public async Task StreamGenerateAsync_KeepAliveCommentLines_IgnoredGracefully()
    {
        var (adapter, mock) = Make();
        var sse = string.Join("\n",
            ": heartbeat",
            "",
            """data: {"choices":[{"delta":{"content":"a"}}]}""",
            ": another heartbeat",
            """data: {"choices":[{"delta":{"content":"b"}}]}""",
            "data: [DONE]",
            "");
        mock.Respond(HttpStatusCode.OK, sse, contentType: "text/event-stream");

        var collected = new List<string>();
        await foreach (var c in adapter.StreamGenerateAsync(P(), "x"))
            collected.Add(c);

        collected.Should().Equal("a", "b");
    }

    [Fact]
    public async Task StreamGenerateAsync_MalformedChunk_LogsAndContinues()
    {
        var (adapter, mock) = Make();
        var sse = string.Join("\n",
            """data: {"choices":[{"delta":{"content":"good"}}]}""",
            "data: { broken json",                            // 坏 chunk 不应中断流
            """data: {"choices":[{"delta":{"content":"end"}}]}""",
            "data: [DONE]",
            "");
        mock.Respond(HttpStatusCode.OK, sse, contentType: "text/event-stream");

        var collected = new List<string>();
        await foreach (var c in adapter.StreamGenerateAsync(P(), "x"))
            collected.Add(c);

        collected.Should().Equal("good", "end");
    }

    [Fact]
    public async Task StreamGenerateAsync_DoneOnly_YieldsNothing()
    {
        var (adapter, mock) = Make();
        mock.Respond(HttpStatusCode.OK, "data: [DONE]\n", contentType: "text/event-stream");

        var collected = new List<string>();
        await foreach (var c in adapter.StreamGenerateAsync(P(), "x"))
            collected.Add(c);

        collected.Should().BeEmpty();
    }

    [Fact]
    public async Task StreamGenerateAsync_EmptyDeltaContent_Skipped()
    {
        var (adapter, mock) = Make();
        var sse = string.Join("\n",
            """data: {"choices":[{"delta":{"role":"assistant"}}]}""",   // 首 chunk 只有 role,无 content
            """data: {"choices":[{"delta":{"content":""}}]}""",          // 显式空字符串也跳过
            """data: {"choices":[{"delta":{"content":"x"}}]}""",
            "data: [DONE]",
            "");
        mock.Respond(HttpStatusCode.OK, sse, contentType: "text/event-stream");

        var collected = new List<string>();
        await foreach (var c in adapter.StreamGenerateAsync(P(), "x"))
            collected.Add(c);

        collected.Should().Equal("x");
    }

    // ---- 辅助 ------------------------------------------------------

    /// <summary>记录请求 / 按队列响应。流式响应直接返回 StringContent("text/event-stream").</summary>
    private sealed class MockHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int CallCount { get; private set; }
        private (HttpStatusCode code, string body, string ct)? _next;

        public void Respond(HttpStatusCode code, string body, string contentType = "application/json")
            => _next = (code, body, contentType);

        public void RespondJson(HttpStatusCode code, string json)
            => _next = (code, json, "application/json");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync(ct);

            if (_next == null)
                throw new InvalidOperationException("MockHandler: 调用前未设置响应");

            var (code, body, contentType) = _next.Value;
            return new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            };
        }
    }
}

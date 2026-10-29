using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sb.Apps.Pm.Models.Ai;
using Sb.Apps.Pm.Services.Ai;
using Xunit;

namespace Sb.Apps.Pm.Services.Tests.Ai;

public class HermesAiProviderAdapterTests
{
    private static AiProvider P(string? extra = null) => new()
    {
        Id = 1,
        Name = "hermes-test",
        ProviderType = "Hermes",
        ModelName = "default",
        ExtraConfig = extra,
    };

    // ---- ParseExtra ----

    [Fact]
    public void ParseExtra_NullExtraConfig_ReturnsAllNullDefaults()
    {
        var extra = HermesAiProviderAdapter.ParseExtra(P(null));

        extra.SystemPrompt.Should().BeNull();
        extra.MaxTokens.Should().BeNull();
        extra.Temperature.Should().BeNull();
    }

    [Fact]
    public void ParseExtra_EmptyExtraConfig_ReturnsAllNullDefaults()
    {
        var extra = HermesAiProviderAdapter.ParseExtra(P("   "));

        extra.SystemPrompt.Should().BeNull();
        extra.MaxTokens.Should().BeNull();
        extra.Temperature.Should().BeNull();
    }

    [Fact]
    public void ParseExtra_ValidJson_ParsesAllFields()
    {
        var json = """{"systemPrompt":"hi","maxTokens":2048,"temperature":0.7}""";

        var extra = HermesAiProviderAdapter.ParseExtra(P(json));

        extra.SystemPrompt.Should().Be("hi");
        extra.MaxTokens.Should().Be(2048);
        extra.Temperature.Should().BeApproximately(0.7f, 0.001f);
    }

    [Fact]
    public void ParseExtra_PartialJson_KeepsOmittedFieldsNull()
    {
        var json = """{"maxTokens":1024}""";

        var extra = HermesAiProviderAdapter.ParseExtra(P(json));

        extra.SystemPrompt.Should().BeNull();
        extra.MaxTokens.Should().Be(1024);
        extra.Temperature.Should().BeNull();
    }

    [Fact]
    public void ParseExtra_InvalidJson_ReturnsDefaultsAndDoesNotThrow()
    {
        var bad = "this is not json {";

        Action act = () => HermesAiProviderAdapter.ParseExtra(P(bad));

        act.Should().NotThrow();
        var extra = HermesAiProviderAdapter.ParseExtra(P(bad));
        extra.SystemPrompt.Should().BeNull();
        extra.MaxTokens.Should().BeNull();
        extra.Temperature.Should().BeNull();
    }

    // ---- TryExtractChunkContent (流式 SSE 解析) ----

    private static HermesAiProviderAdapter MakeAdapter()
    {
        return new HermesAiProviderAdapter(NullLogger<HermesAiProviderAdapter>.Instance);
    }

    [Fact]
    public void TryExtractChunkContent_ValidDeltaChunk_ReturnsContent()
    {
        var adapter = MakeAdapter();
        var data = """{"choices":[{"delta":{"role":"assistant","content":"hello"}}]}""";

        var content = adapter.TryExtractChunkContent(P(), data);

        content.Should().Be("hello");
    }

    [Fact]
    public void TryExtractChunkContent_EmptyDelta_ReturnsNull()
    {
        var adapter = MakeAdapter();
        var data = """{"choices":[{"delta":{"role":"assistant"}}]}""";

        var content = adapter.TryExtractChunkContent(P(), data);

        content.Should().BeNull();
    }

    [Fact]
    public void TryExtractChunkContent_NoChoices_ReturnsNull()
    {
        var adapter = MakeAdapter();
        var data = """{"id":"x","object":"chat.completion.chunk"}""";

        var content = adapter.TryExtractChunkContent(P(), data);

        content.Should().BeNull();
    }

    [Fact]
    public void TryExtractChunkContent_InvalidJson_ReturnsNullAndDoesNotThrow()
    {
        var adapter = MakeAdapter();
        var data = "}{ broken";

        Action act = () => adapter.TryExtractChunkContent(P(), data);

        act.Should().NotThrow();
        adapter.TryExtractChunkContent(P(), data).Should().BeNull();
    }

    // ---- 元数据 ----

    [Fact]
    public void Metadata_ProviderType_IsHermesAndSupportsStream()
    {
        var adapter = MakeAdapter();

        adapter.ProviderType.Should().Be("Hermes");
        adapter.DisplayName.Should().Be("Hermes Agent");
        adapter.SupportStream.Should().BeTrue();
    }
}

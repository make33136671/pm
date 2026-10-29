namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 模型适配器接口 — 最底层，纯粹的模型调用
    /// 后期对接 Ollama / Claude API / OpenAI 等，只需实现此接口
    /// </summary>
    public interface IAiProvider
    {
        /// <summary>提供商名称，如 "ollama", "claude", "openai"</summary>
        string Name { get; }

        /// <summary>发送请求到 AI 模型</summary>
        Task<AiResponse> SendAsync(AiRequest request, CancellationToken ct = default);
    }

    /// <summary>AI 请求</summary>
    public class AiRequest
    {
        /// <summary>系统提示词（定义 AI 角色和行为）</summary>
        public string SystemPrompt { get; set; } = "";

        /// <summary>对话消息列表</summary>
        public List<AiMessage> Messages { get; set; } = new();

        /// <summary>模型名称，如 "claude-3.5-sonnet", "qwen2.5:32b"</summary>
        public string? Model { get; set; }

        /// <summary>最大输出 Token 数</summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>温度（0-1，越低越确定）</summary>
        public float Temperature { get; set; } = 0.3f;
    }

    /// <summary>对话消息</summary>
    public class AiMessage
    {
        /// <summary>角色: "user" / "assistant" / "system"</summary>
        public string Role { get; set; } = "user";

        /// <summary>消息内容</summary>
        public string Content { get; set; } = "";
    }

    /// <summary>AI 响应</summary>
    public class AiResponse
    {
        /// <summary>AI 返回的内容</summary>
        public string Content { get; set; } = "";

        /// <summary>输入 Token 数</summary>
        public int InputTokens { get; set; }

        /// <summary>输出 Token 数</summary>
        public int OutputTokens { get; set; }

        /// <summary>是否成功</summary>
        public bool Success { get; set; }

        /// <summary>错误信息</summary>
        public string? Error { get; set; }

        /// <summary>实际使用的模型名称</summary>
        public string? ModelUsed { get; set; }
    }
}

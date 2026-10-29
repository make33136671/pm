namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 任务执行结果 — 所有 AI 操作的统一输出格式
    /// </summary>
    public class AiTaskResult
    {
        /// <summary>是否成功</summary>
        public bool Success { get; set; }

        /// <summary>错误信息</summary>
        public string? Error { get; set; }

        // ---- 文本输出 ----

        /// <summary>完整的 AI 回复内容（Markdown 格式）</summary>
        public string Content { get; set; } = "";

        /// <summary>摘要（用于列表展示、通知等）</summary>
        public string? Summary { get; set; }

        // ---- 代码变更（GenerateCode 时使用） ----

        /// <summary>代码变更列表</summary>
        public List<AiCodeChange>? CodeChanges { get; set; }

        // ---- Token 统计 ----

        /// <summary>输入 Token 数</summary>
        public int InputTokens { get; set; }

        /// <summary>输出 Token 数</summary>
        public int OutputTokens { get; set; }

        /// <summary>实际使用的模型</summary>
        public string? ModelUsed { get; set; }

        // ---- 关联数据 ----

        /// <summary>关联的 AiTask 记录 ID</summary>
        public int? TaskId { get; set; }

        /// <summary>创建成功结果</summary>
        public static AiTaskResult Ok(string content, string? summary = null) => new()
        {
            Success = true,
            Content = content,
            Summary = summary,
        };

        /// <summary>创建失败结果</summary>
        public static AiTaskResult Fail(string error) => new()
        {
            Success = false,
            Error = error,
            Content = "",
        };
    }

    /// <summary>
    /// AI 产出的代码变更
    /// </summary>
    public class AiCodeChange
    {
        /// <summary>文件相对路径</summary>
        public string FilePath { get; set; } = "";

        /// <summary>操作类型: "create" / "update" / "delete" / "rename"</summary>
        public string Action { get; set; } = "update";

        /// <summary>新文件内容（create / update 时使用）</summary>
        public string? Content { get; set; }

        /// <summary>变更 diff（可选，用于展示）</summary>
        public string? Diff { get; set; }

        /// <summary>变更说明</summary>
        public string? Description { get; set; }

        /// <summary>rename 时的原路径</summary>
        public string? OldFilePath { get; set; }
    }
}

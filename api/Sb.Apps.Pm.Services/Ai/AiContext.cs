namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 操作上下文 — 包含 AI 执行任务所需的所有信息
    /// </summary>
    public class AiContext
    {
        // ---- 项目信息 ----

        /// <summary>项目 ID</summary>
        public int? ProjectId { get; set; }

        /// <summary>子项目 ID</summary>
        public int? SubProjectId { get; set; }

        /// <summary>项目名称</summary>
        public string? ProjectName { get; set; }

        /// <summary>子项目名称</summary>
        public string? SubProjectName { get; set; }

        /// <summary>工程目录路径（服务器上的本地路径）</summary>
        public string? WorkingDirectory { get; set; }

        // ---- 文件上下文 ----

        /// <summary>相关代码文件列表（AI 可读取的文件内容）</summary>
        public List<AiFileContext> Files { get; set; } = new();

        // ---- 需求/任务来源 ----

        /// <summary>关联的需求 ID</summary>
        public int? RequirementId { get; set; }

        /// <summary>需求文档内容</summary>
        public string? RequirementContent { get; set; }

        // ---- 用户指令 ----

        /// <summary>用户想让 AI 做的事（文字说明、任务描述）</summary>
        public string Instruction { get; set; } = "";

        // ---- 历史记忆 ----

        /// <summary>项目相关的 AI 记忆</summary>
        public List<AiMemoryItem> Memories { get; set; } = new();

        // ---- 对话历史（多轮追问） ----

        /// <summary>对话历史，支持 AI 结果不满意时追问修改</summary>
        public List<AiMessage> ConversationHistory { get; set; } = new();

        // ---- 约束与规范 ----

        /// <summary>编码规范（从 CLAUDE.md / .editorconfig 等读取）</summary>
        public string? CodingConventions { get; set; }

        /// <summary>技术栈描述</summary>
        public string? TechStack { get; set; }

        /// <summary>重点关注的目录/文件路径（优先加载这些文件给 AI）</summary>
        public List<string>? FocusPaths { get; set; }

        // ---- Git 信息 ----

        /// <summary>当前分支</summary>
        public string? CurrentBranch { get; set; }

        /// <summary>AI 工作分支（如 ai/task-123）</summary>
        public string? WorkBranch { get; set; }

        /// <summary>关联 GitRepository 记录 Id</summary>
        public int? GitRepoId { get; set; }

        /// <summary>仓库地址</summary>
        public string? GitRepoUrl { get; set; }

        /// <summary>最近提交摘要（供 AI 了解代码变化背景）</summary>
        public List<GitCommitSummary> RecentCommits { get; set; } = new();
    }

    public class GitCommitSummary
    {
        public string Sha { get; set; } = "";
        public string Message { get; set; } = "";
        public string Author { get; set; } = "";
        public DateTime CommittedAt { get; set; }
    }

    /// <summary>文件上下文</summary>
    public class AiFileContext
    {
        /// <summary>文件相对路径</summary>
        public string FilePath { get; set; } = "";

        /// <summary>文件内容</summary>
        public string Content { get; set; } = "";

        /// <summary>编程语言标识（cs, ts, vue, json 等）</summary>
        public string? Language { get; set; }

        /// <summary>文件大小（字节）</summary>
        public long Size { get; set; }
    }

    /// <summary>AI 记忆条目</summary>
    public class AiMemoryItem
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Category { get; set; } = "";
        public int Importance { get; set; }
    }
}

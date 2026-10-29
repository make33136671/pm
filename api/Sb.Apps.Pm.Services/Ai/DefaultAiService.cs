using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 服务默认实现 — 编排 IAiProvider 调用，管理任务记录和记忆沉淀
    /// 无 Provider 注册时返回提示信息（不会报错）
    /// </summary>
    [AutoDependencyInjection]
    public class DefaultAiService : IAiService
    {
        private readonly IFreeSql _fsql;
        private readonly IAiProvider? _provider;
        private readonly AiContextBuilder _contextBuilder;
        private readonly AiTaskService _taskService;
        private readonly AiMemoryService _memoryService;
        private readonly ILogger<DefaultAiService> _logger;

        public DefaultAiService(
            IFreeSql fsql,
            AiContextBuilder contextBuilder,
            AiTaskService taskService,
            AiMemoryService memoryService,
            ILogger<DefaultAiService> logger,
            IAiProvider? provider = null)
        {
            _fsql = fsql;
            _contextBuilder = contextBuilder;
            _taskService = taskService;
            _memoryService = memoryService;
            _logger = logger;
            _provider = provider;
        }

        public Task<AiTaskResult> ExtractFeatureRequirements(AiContext context)
            => Execute("ExtractFeatureRequirements", AiTaskType.ExtractRequirement, context);

        public Task<AiTaskResult> AnalyzeTaskProgress(AiContext context)
            => Execute("AnalyzeTaskProgress", AiTaskType.AnalyzeChat, context);

        public Task<AiTaskResult> AnalyzeRequirementCoverage(AiContext context)
            => Execute("AnalyzeRequirementCoverage", AiTaskType.AnalyzeChat, context);

        public Task<AiTaskResult> AnalyzeDocuments(AiContext context)
            => Execute("AnalyzeDocuments", AiTaskType.AnalyzeChat, context);

        public Task<AiTaskResult> AnalyzeCode(AiContext context) => ExecuteWithCodeContext("AnalyzeCode", AiTaskType.AnalyzeCode, context);
        public Task<AiTaskResult> GenerateCode(AiContext context) => ExecuteWithCodeContext("GenerateCode", AiTaskType.GenerateCode, context);
        public Task<AiTaskResult> DesignFeature(AiContext context) => ExecuteWithCodeContext("DesignFeature", AiTaskType.DesignFeature, context);
        public Task<AiTaskResult> ReviewCode(AiContext context) => ExecuteWithCodeContext("ReviewCode", AiTaskType.ReviewCode, context);
        public Task<AiTaskResult> Chat(AiContext context) => Execute("Chat", AiTaskType.AnalyzeChat, context);

        /// <summary>代码类任务自动注入文件列表 + Git 信息后再执行。</summary>
        private async Task<AiTaskResult> ExecuteWithCodeContext(string taskType, AiTaskType aiTaskType, AiContext context)
        {
            if (!string.IsNullOrEmpty(context.WorkingDirectory))
                _contextBuilder.EnrichWithFiles(context);
            await _contextBuilder.EnrichWithGitInfo(context);
            return await Execute(taskType, aiTaskType, context);
        }

        private async Task<AiTaskResult> Execute(string taskType, AiTaskType aiTaskType, AiContext context)
        {
            // 1. 自动丰富上下文
            _contextBuilder.EnrichWithMemory(context);
            _contextBuilder.EnrichWithConventions(context);

            // 2. 创建任务记录
            var task = new AiTask
            {
                ProjectId = context.ProjectId ?? 0,
                RequirementId = context.RequirementId,
                TaskType = aiTaskType,
                InputData = System.Text.Json.JsonSerializer.Serialize(new
                {
                    taskType,
                    instruction = context.Instruction,
                    filesCount = context.Files.Count,
                    memoriesCount = context.Memories.Count,
                }),
                OperatorName = "System",
            };
            _taskService.Create(task);

            // 3. 检查 Provider
            if (_provider == null)
            {
                _logger.LogWarning("未注册 IAiProvider，返回 mock 结果");

                UpdateTaskStatus(task.Id, AiTaskStatus.Completed, "无 AI Provider，返回提示", 0, 0, "none");

                return AiTaskResult.Ok(
                    "AI 服务尚未配置。请在系统中注册 IAiProvider 实现（如 Ollama、Claude API 等）。\n\n" +
                    $"**任务类型**: {taskType}\n" +
                    $"**指令**: {context.Instruction}\n" +
                    $"**上下文文件数**: {context.Files.Count}\n" +
                    $"**记忆条目数**: {context.Memories.Count}",
                    "AI 服务未配置"
                );
            }

            // 4. 构建请求
            try
            {
                UpdateTaskStatus(task.Id, AiTaskStatus.Running);

                var systemPrompt = AiPromptTemplates.GetTemplate(taskType);

                // 注入编码规范到 System Prompt
                if (!string.IsNullOrEmpty(context.CodingConventions))
                {
                    systemPrompt += $"\n\n## 项目编码规范\n\n{context.CodingConventions}";
                }

                // 构建用户消息
                var userMessage = BuildUserMessage(context);

                var messages = new List<AiMessage>();
                // 添加对话历史
                messages.AddRange(context.ConversationHistory);
                // 添加当前消息
                messages.Add(new AiMessage { Role = "user", Content = userMessage });

                var request = new AiRequest
                {
                    SystemPrompt = systemPrompt,
                    Messages = messages,
                    MaxTokens = taskType == "GenerateCode" ? 8192 : 4096,
                    Temperature = taskType == "GenerateCode" ? 0.2f : 0.3f,
                };

                // 5. 调用 AI
                var response = await _provider.SendAsync(request);

                if (!response.Success)
                {
                    UpdateTaskStatus(task.Id, AiTaskStatus.Failed, response.Error, response.InputTokens, response.OutputTokens, response.ModelUsed);
                    return AiTaskResult.Fail(response.Error ?? "AI 调用失败");
                }

                // 6. 解析结果
                var result = new AiTaskResult
                {
                    Success = true,
                    Content = response.Content,
                    InputTokens = response.InputTokens,
                    OutputTokens = response.OutputTokens,
                    ModelUsed = response.ModelUsed,
                    TaskId = task.Id,
                };

                // GenerateCode 时尝试解析代码变更
                if (taskType == "GenerateCode")
                {
                    result.CodeChanges = ParseCodeChanges(response.Content);
                }

                // 7. 更新任务状态
                UpdateTaskStatus(task.Id, AiTaskStatus.Completed, response.Content, response.InputTokens, response.OutputTokens, response.ModelUsed);

                // 8. 设计文档类结果自动沉淀到记忆
                if (taskType == "DesignFeature" && context.ProjectId.HasValue)
                {
                    try
                    {
                        _memoryService.Create(new AiMemory
                        {
                            ProjectId = context.ProjectId.Value,
                            Scope = AiMemoryScope.Project,
                            Category = "设计文档",
                            Title = $"[AI] {context.Instruction}",
                            Content = response.Content,
                            Source = "Auto",
                            Importance = 5,
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "保存 AI 记忆失败");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI 任务执行失败: {TaskType}", taskType);
                UpdateTaskStatus(task.Id, AiTaskStatus.Failed, ex.Message);
                return AiTaskResult.Fail(ex.Message);
            }
        }

        #region 辅助方法

        private string BuildUserMessage(AiContext context)
        {
            var parts = new List<string>();

            // 项目信息
            if (!string.IsNullOrEmpty(context.ProjectName))
                parts.Add($"## 项目: {context.ProjectName}" + (context.SubProjectName != null ? $" / {context.SubProjectName}" : ""));

            // 技术栈
            if (!string.IsNullOrEmpty(context.TechStack))
                parts.Add($"## 技术栈\n{context.TechStack}");

            // 需求内容
            if (!string.IsNullOrEmpty(context.RequirementContent))
                parts.Add($"## 相关需求\n{context.RequirementContent}");

            // 记忆
            if (context.Memories.Count > 0)
            {
                var memoryText = string.Join("\n\n", context.Memories.Select(m => $"### {m.Title}\n{m.Content}"));
                parts.Add($"## 项目记忆（历史上下文）\n{memoryText}");
            }

            // 文件内容
            if (context.Files.Count > 0)
            {
                var filesText = string.Join("\n\n", context.Files.Select(f =>
                    $"### {f.FilePath}\n```{f.Language}\n{f.Content}\n```"));
                parts.Add($"## 相关代码文件\n{filesText}");
            }

            // 用户指令
            parts.Add($"## 任务指令\n{context.Instruction}");

            return string.Join("\n\n", parts);
        }

        /// <summary>
        /// 尝试从 AI 输出中解析代码变更块
        /// </summary>
        private List<AiCodeChange>? ParseCodeChanges(string content)
        {
            // 解析 ```change ... ``` 格式的代码变更块
            var changes = new List<AiCodeChange>();
            var blocks = content.Split("```change", StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < blocks.Length; i++)
            {
                var block = blocks[i];
                var endIdx = block.IndexOf("```");
                if (endIdx > 0) block = block[..endIdx];

                var lines = block.Split('\n');
                var change = new AiCodeChange();

                int contentStart = 0;
                for (int j = 0; j < lines.Length; j++)
                {
                    var line = lines[j].Trim();
                    if (line.StartsWith("文件:") || line.StartsWith("file:"))
                        change.FilePath = line.Split(':', 2)[1].Trim();
                    else if (line.StartsWith("操作:") || line.StartsWith("action:"))
                        change.Action = line.Split(':', 2)[1].Trim().ToLower();
                    else if (line.StartsWith("说明:") || line.StartsWith("description:"))
                        change.Description = line.Split(':', 2)[1].Trim();
                    else if (line == "---")
                    {
                        contentStart = j + 1;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(change.FilePath) && contentStart > 0)
                {
                    change.Content = string.Join('\n', lines[contentStart..]);
                    changes.Add(change);
                }
            }

            return changes.Count > 0 ? changes : null;
        }

        private void UpdateTaskStatus(int taskId, AiTaskStatus status, string? output = null, int inputTokens = 0, int outputTokens = 0, string? model = null)
        {
            try
            {
                var update = _fsql.Update<AiTask>().Where(t => t.Id == taskId)
                    .Set(t => t.Status, status);

                if (status == AiTaskStatus.Running)
                    update = update.Set(t => t.StartedAt, DateTime.Now);

                if (status == AiTaskStatus.Completed || status == AiTaskStatus.Failed)
                {
                    update = update.Set(t => t.FinishedAt, DateTime.Now);
                    if (output != null) update = update.Set(t => t.OutputData, output.Length > 65000 ? output[..65000] : output);
                    if (inputTokens > 0) update = update.Set(t => t.TokensUsed, inputTokens + outputTokens);
                    if (model != null) update = update.Set(t => t.AiModel, model);
                }

                if (status == AiTaskStatus.Failed && output != null)
                    update = update.Set(t => t.ErrorMessage, output.Length > 500 ? output[..500] : output);

                update.ExecuteAffrows();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "更新 AiTask 状态失败: TaskId={TaskId}", taskId);
            }
        }

        #endregion
    }
}

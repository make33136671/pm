using FreeSql;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Ai;
using Sb.Apps.Pm.Services.Tasks;

namespace Sb.Apps.Pm.Services.Pipelines
{
    /// <summary>
    /// AI 代码生成器服务 (#133-137)
    /// 流程：读任务 → 构建 prompt → AI 生成 → 解析变更列表 → 建分支 → 写文件 → 创建 MR
    /// </summary>
    [AutoDependencyInjection]
    public class AiCodeGeneratorService
    {
        private readonly IFreeSql _fsql;
        private readonly GitLabApiService _gitlab;
        private readonly IAiProvider _aiProvider;
        private readonly TaskItemService _taskItemService;
        private readonly ILogger<AiCodeGeneratorService> _logger;

        public AiCodeGeneratorService(IFreeSql fsql, GitLabApiService gitlab, IAiProvider aiProvider,
            TaskItemService taskItemService, ILogger<AiCodeGeneratorService> logger)
        {
            _fsql = fsql;
            _gitlab = gitlab;
            _aiProvider = aiProvider;
            _taskItemService = taskItemService;
            _logger = logger;
        }

        /// <summary>
        /// 为指定任务触发 AI 代码生成，自动建分支 + 写文件 + 创 MR
        /// </summary>
        /// <param name="taskItemId">任务 ID</param>
        /// <param name="gitlabProjectId">GitLab Project ID</param>
        /// <param name="targetBranch">目标分支（默认 main）</param>
        /// <returns>MR 信息或错误</returns>
        public async Task<AiCodeGenResult> GenerateCodeForTask(int taskItemId, int gitlabProjectId, string targetBranch = "main")
        {
            // 安全边界 (#137): 只允许对处于 Running 状态的任务进行代码生成
            var task = _fsql.Select<TaskItem>().Where(t => t.Id == taskItemId).First();
            if (task == null)
                return AiCodeGenResult.Fail("任务不存在");

            if (task.ExecStatus != TaskItemExecStatus.Running)
                return AiCodeGenResult.Fail($"任务当前状态为 {task.ExecStatus}，只允许对 Running 状态的任务生成代码");

            _logger.LogInformation("AI 代码生成: taskId={TaskId} project={ProjectId}", taskItemId, gitlabProjectId);

            try
            {
                // 1. 构建 prompt (#133)
                var prompt = BuildCodeGenPrompt(task);

                // 2. 调用 AI (#133)
                var aiRequest = new AiRequest
                {
                    SystemPrompt = BuildCodeGenSystemPrompt(),
                    Messages =
                    [
                        new AiMessage { Role = "user", Content = prompt }
                    ],
                    MaxTokens = 8192,
                };
                var response = await _aiProvider.SendAsync(aiRequest);
                if (!response.Success)
                    return AiCodeGenResult.Fail($"AI 调用失败: {response.Error}");

                // 3. 解析 AI 输出为文件变更列表 (#134)
                var changes = ParseAiOutput(response.Content ?? "");
                if (changes.Count == 0)
                    return AiCodeGenResult.Fail("AI 未输出有效的代码变更，请检查任务描述是否足够详细");

                // 4. 建分支 (#135)
                var branchName = $"ai/task-{taskItemId}-{DateTime.Now:yyyyMMddHHmmss}";
                await _gitlab.CreateBranch(gitlabProjectId, branchName, targetBranch);
                _logger.LogInformation("已创建分支: {Branch}", branchName);

                // 5. 批量写文件 (#135)
                var commitActions = changes.Select(c => new CommitAction
                {
                    Action = c.Action,
                    FilePath = c.FilePath,
                    Content = c.Content,
                }).ToList();

                var commitMsg = $"feat(ai): 任务 #{taskItemId} - {task.Title}\n\nAI 自动生成代码，任务 ID: {taskItemId}";
                await _gitlab.CreateCommit(gitlabProjectId, branchName, commitMsg, commitActions);
                _logger.LogInformation("已提交 {Count} 个文件变更", commitActions.Count);

                // 6. 创建 MR + 写 AI 说明 (#136)
                var mrTitle = $"[AI] 任务 #{taskItemId}: {task.Title}";
                var mrDescription = BuildMrDescription(task, changes, response.Content ?? "");
                var mr = await _gitlab.CreateMergeRequest(gitlabProjectId, branchName, targetBranch, mrTitle, mrDescription);

                _logger.LogInformation("AI 代码生成完成: taskId={TaskId} branch={Branch}", taskItemId, branchName);

                return new AiCodeGenResult
                {
                    Success = true,
                    BranchName = branchName,
                    ChangedFiles = changes,
                    MrInfo = mr,
                    AiContent = response.Content ?? "",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI 代码生成失败: taskId={TaskId}", taskItemId);
                return AiCodeGenResult.Fail($"代码生成失败: {ex.Message}");
            }
        }

        private static string BuildCodeGenSystemPrompt() =>
            """
            你是一个资深全栈工程师，擅长根据需求文档生成高质量代码。
            你必须严格按照以下 JSON 格式输出文件变更列表，不要输出任何其他内容：

            ```json
            {
              "summary": "本次改动的简要说明",
              "changes": [
                {
                  "action": "create",
                  "filePath": "src/xxx/yyy.cs",
                  "content": "文件完整内容"
                },
                {
                  "action": "update",
                  "filePath": "src/xxx/zzz.cs",
                  "content": "文件完整内容（更新后）"
                }
              ]
            }
            ```

            action 只能是: create（新建文件）、update（修改文件）、delete（删除文件，content 可为空）。
            filePath 必须是相对于仓库根目录的路径。
            content 是文件的完整内容（不是 diff）。
            """;

        private static string BuildCodeGenPrompt(TaskItem task)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"## 任务标题");
            sb.AppendLine(task.Title);
            sb.AppendLine();
            sb.AppendLine("## 任务描述");
            sb.AppendLine(task.Content ?? "（无详细描述）");
            sb.AppendLine();
            sb.AppendLine($"## 任务类型");
            sb.AppendLine(task.Type.ToString());

            if (!string.IsNullOrEmpty(task.FilePathHint))
            {
                sb.AppendLine();
                sb.AppendLine("## 相关文件（供参考）");
                sb.AppendLine(task.FilePathHint);
            }

            if (!string.IsNullOrEmpty(task.RouteHint))
            {
                sb.AppendLine();
                sb.AppendLine("## 相关路由");
                sb.AppendLine(task.RouteHint);
            }

            if (!string.IsNullOrEmpty(task.LineHint))
            {
                sb.AppendLine();
                sb.AppendLine("## 代码位置说明");
                sb.AppendLine(task.LineHint);
            }

            sb.AppendLine();
            sb.AppendLine("请根据以上任务要求生成代码，严格按照要求的 JSON 格式输出。");
            return sb.ToString();
        }

        /// <summary>解析 AI 输出中的 JSON 代码变更列表 (#134)</summary>
        private List<AiCodeChange> ParseAiOutput(string aiContent)
        {
            try
            {
                // 提取 ```json ... ``` 块
                var start = aiContent.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
                var end = start >= 0 ? aiContent.IndexOf("```", start + 7, StringComparison.OrdinalIgnoreCase) : -1;

                string jsonStr;
                if (start >= 0 && end > start)
                {
                    jsonStr = aiContent.Substring(start + 7, end - start - 7).Trim();
                }
                else
                {
                    // 尝试直接解析整个内容
                    jsonStr = aiContent.Trim();
                }

                var obj = JObject.Parse(jsonStr);
                var changes = obj["changes"] as JArray ?? new JArray();
                return changes.Select(c => new AiCodeChange
                {
                    Action = c["action"]?.ToString() ?? "create",
                    FilePath = c["filePath"]?.ToString() ?? "",
                    Content = c["content"]?.ToString() ?? "",
                }).Where(c => !string.IsNullOrEmpty(c.FilePath)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 AI 输出失败，原始内容长度: {Length}", aiContent.Length);
                return new List<AiCodeChange>();
            }
        }

        private static string BuildMrDescription(TaskItem task, List<AiCodeChange> changes, string aiSummary)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"## 🤖 AI 自动生成 — 任务 #{task.Id}");
            sb.AppendLine();
            sb.AppendLine($"**任务标题**: {task.Title}");
            sb.AppendLine();
            sb.AppendLine("## 变更文件");
            sb.AppendLine();
            foreach (var c in changes)
            {
                var icon = c.Action switch { "create" => "➕", "delete" => "❌", _ => "✏️" };
                sb.AppendLine($"- {icon} `{c.FilePath}` ({c.Action})");
            }
            sb.AppendLine();
            sb.AppendLine("## AI 说明");
            sb.AppendLine();
            // 截取 summary 部分
            try
            {
                var start = aiSummary.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
                var end = start >= 0 ? aiSummary.IndexOf("```", start + 7, StringComparison.OrdinalIgnoreCase) : -1;
                if (start >= 0 && end > start)
                {
                    var jsonPart = aiSummary.Substring(start + 7, end - start - 7).Trim();
                    var obj = JObject.Parse(jsonPart);
                    var summary = obj["summary"]?.ToString();
                    if (!string.IsNullOrEmpty(summary))
                    {
                        sb.AppendLine(summary);
                        goto done;
                    }
                }
            }
            catch { }
            sb.AppendLine("（AI 未提供摘要）");
            done:
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("*由 PM AI Code Generator 自动生成，请人工 Review 后合并*");
            return sb.ToString();
        }
    }

    /// <summary>AI 代码生成结果</summary>
    public class AiCodeGenResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? BranchName { get; set; }
        public List<AiCodeChange> ChangedFiles { get; set; } = new();
        public object? MrInfo { get; set; }
        public string? AiContent { get; set; }

        public static AiCodeGenResult Fail(string message) => new() { Success = false, ErrorMessage = message };
    }

    /// <summary>AI 生成的单个文件变更</summary>
    public class AiCodeChange
    {
        /// <summary>操作: create / update / delete</summary>
        public string Action { get; set; } = "create";
        /// <summary>文件路径（相对仓库根目录）</summary>
        public string FilePath { get; set; } = "";
        /// <summary>文件完整内容</summary>
        public string Content { get; set; } = "";
    }
}

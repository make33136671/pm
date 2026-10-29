using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.Services.Pipelines
{
    /// <summary>
    /// AI Code Review 服务 — 读取 GitLab MR diff，构建 prompt，调 AI，将结果写回 MR 评论 (#128-131)
    /// </summary>
    [AutoDependencyInjection]
    public class AiCodeReviewService
    {
        private readonly GitLabApiService _gitlab;
        private readonly IAiProvider _aiProvider;
        private readonly ILogger<AiCodeReviewService> _logger;

        public AiCodeReviewService(GitLabApiService gitlab, IAiProvider aiProvider, ILogger<AiCodeReviewService> logger)
        {
            _gitlab = gitlab;
            _aiProvider = aiProvider;
            _logger = logger;
        }

        /// <summary>
        /// 对指定 MR 执行 AI Code Review，并将结果以评论形式写回 GitLab
        /// </summary>
        /// <param name="gitlabProjectId">GitLab Project ID</param>
        /// <param name="mrIid">MR IID（项目内序号）</param>
        /// <returns>AI 评审结果文本</returns>
        public async Task<(bool Success, string Result)> ReviewMergeRequest(int gitlabProjectId, int mrIid)
        {
            try
            {
                // 1. 读取 MR diff (#129)
                var changes = await _gitlab.GetMergeRequestDiff(gitlabProjectId, mrIid);
                if (changes.Count == 0)
                {
                    return (false, "MR 没有文件变更，无需 Review");
                }

                // 2. 构建 prompt (#130)
                var prompt = BuildReviewPrompt(gitlabProjectId, mrIid, changes);

                // 3. 调用 AI (#130)
                _logger.LogInformation("AI Code Review: project={ProjectId} mr={MrIid} 共 {Count} 个文件", gitlabProjectId, mrIid, changes.Count);
                var aiRequest = new AiRequest
                {
                    SystemPrompt = "你是一个资深代码审查工程师，擅长发现代码质量问题、安全漏洞、性能隐患和逻辑错误。请用中文输出结构化的 Code Review 报告。",
                    Messages =
                    [
                        new AiMessage { Role = "user", Content = prompt }
                    ]
                };

                var response = await _aiProvider.SendAsync(aiRequest);
                if (!response.Success)
                {
                    return (false, $"AI 调用失败: {response.Error}");
                }

                // 4. 写回 MR 评论 (#131)
                var comment = BuildCommentBody(response.Content ?? "");
                await _gitlab.AddMergeRequestNote(gitlabProjectId, mrIid, comment);

                _logger.LogInformation("AI Code Review 完成: project={ProjectId} mr={MrIid}", gitlabProjectId, mrIid);
                return (true, response.Content ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Code Review 失败: project={ProjectId} mr={MrIid}", gitlabProjectId, mrIid);
                return (false, $"Review 失败: {ex.Message}");
            }
        }

        private static string BuildReviewPrompt(int projectId, int mrIid, List<MrChangedFile> changes)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"请对以下 GitLab MR (Project #{projectId}, MR !{mrIid}) 进行 Code Review。");
            sb.AppendLine();
            sb.AppendLine("## 变更文件列表");
            sb.AppendLine();

            foreach (var file in changes)
            {
                var label = file.NewFile ? "[新增]" : file.DeletedFile ? "[删除]" : file.RenamedFile ? $"[重命名] {file.OldPath} →" : "[修改]";
                sb.AppendLine($"### {label} `{file.NewPath}`");
                sb.AppendLine("```diff");
                // diff 可能很长，截断到 3000 字符避免超出 token 限制
                var diff = file.Diff.Length > 3000 ? file.Diff[..3000] + "\n... (已截断)" : file.Diff;
                sb.AppendLine(diff);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            sb.AppendLine("## 请按以下维度输出 Review 报告");
            sb.AppendLine();
            sb.AppendLine("1. **总体评价** — 一句话概括本次变更质量");
            sb.AppendLine("2. **问题列表** — 每个问题写明：文件名、行号（如能判断）、问题描述、严重程度（Critical/High/Medium/Low）");
            sb.AppendLine("3. **安全风险** — 是否存在安全漏洞或注入风险");
            sb.AppendLine("4. **改进建议** — 可选优化项");
            sb.AppendLine("5. **结论** — 是否建议合并（Approved / Changes Required / Rejected）");

            return sb.ToString();
        }

        private static string BuildCommentBody(string aiContent)
        {
            return $"## 🤖 AI Code Review\n\n{aiContent}\n\n---\n*由 PM AI Code Review 自动生成*";
        }
    }
}

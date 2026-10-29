using System.Text.RegularExpressions;
using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// 分析批次管理
    /// 批次 = 标签/容器，关联消息和需求，不影响各自独立性
    /// </summary>
    [AutoDependencyInjection]
    public class ChatAnalysisBatchService : BaseCrudService<ChatBatch>
    {
        private readonly IFreeSql _fsql;
        private readonly AiPipelineExecutor _executor;

        public ChatAnalysisBatchService(IFreeSql fsql, AiPipelineExecutor executor,
            ILogger<ChatAnalysisBatchService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
            _executor = executor;
        }

        /// <summary>批次列表</summary>
        public virtual Result GetList(int? projectId = null, ChatBatchStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .Include(b => b.Project)
                .WhereIf(projectId.HasValue, b => b.ProjectId == projectId.Value)
                .WhereIf(status.HasValue, b => b.Status == status.Value);
            query = ApplySorting(query, field, order);

            var data = query.Count(out long total).Page(page, pageSize).ToList();

            // 附加每个批次的消息数、需求数、任务数
            var result = data.Select(b => new
            {
                b.Id, b.Title, b.ProjectId, ProjectName = b.Project?.Name, b.GroupBindingId, b.Status,
                b.ResultSummary, b.CreatedBy, b.CreatedTime, b.CompletedTime,
                MessageCount = _fsql.Select<ChatBatchMessage>().Where(m => m.BatchId == b.Id).Count(),
                RequirementCount = _fsql.Select<ChatBatchRequirement>().Where(r => r.BatchId == b.Id).Count(),
                TaskCount = _fsql.Select<ChatBatchTask>().Where(t => t.BatchId == b.Id).Count(),
            }).ToList();

            return Result.Page(result, total);
        }

        /// <summary>批次详情（含关联的消息、AI需求、正式需求、任务）</summary>
        public virtual Result BatchDetail(int id)
        {
            var batch = Select.Where(b => b.Id == id).First();
            if (batch == null) return Result.Fail("批次不存在");

            // 关联消息
            var messageIds = _fsql.Select<ChatBatchMessage>()
                .Where(m => m.BatchId == id).ToList(m => m.MessageId);
            var messages = messageIds.Count > 0
                ? _fsql.Select<ChatMessage>().Where(m => messageIds.Contains(m.Id))
                    .IncludeMany(m => m.MediaList).OrderBy(m => m.Id).ToList()
                : new List<ChatMessage>();

            // 关联 AI 提取需求
            var chatReqIds = _fsql.Select<ChatBatchRequirement>()
                .Where(r => r.BatchId == id).ToList(r => r.RequirementId);
            var chatRequirements = chatReqIds.Count > 0
                ? _fsql.Select<ChatRequirement>().Where(r => chatReqIds.Contains(r.Id)).OrderBy(r => r.Id).ToList()
                : new List<ChatRequirement>();

            // 关联正式需求（通过 Requirement.BatchId）
            var batchIdStr = id.ToString();
            var requirements = _fsql.Select<Requirement>()
                .Where(r => r.BatchId == batchIdStr && !r.IsDeleted)
                .OrderBy(r => r.Id).ToList();

            // 关联任务
            var taskIds = _fsql.Select<ChatBatchTask>()
                .Where(t => t.BatchId == id).ToList(t => t.TaskId);
            var tasks = taskIds.Count > 0
                ? _fsql.Select<TaskItem>().Where(t => taskIds.Contains(t.Id) && !t.IsDeleted).OrderBy(t => t.Id).ToList()
                : new List<TaskItem>();

            // 发送人角色映射（platformUserId -> role）
            var senderIds = messages.Select(m => m.SenderId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            var senderRoles = senderIds.Count > 0
                ? _fsql.Select<ChatUserBinding>()
                    .Where(u => senderIds.Contains(u.PlatformUserId) && u.ProjectId == batch.ProjectId && u.Status == ChatUserBindingStatus.Active)
                    .ToDictionary(u => u.PlatformUserId, u => u.Role.ToString())
                : new Dictionary<string, string>();

            return Result.Success(new { batch, messages, chatRequirements, requirements, tasks, senderRoles });
        }

        /// <summary>创建批次</summary>
        public virtual Result CreateBatch(int projectId, int? groupBindingId, string title)
        {
            var batch = new ChatBatch
            {
                Title = title,
                ProjectId = projectId,
                GroupBindingId = groupBindingId,
                Status = ChatBatchStatus.Pending,
                CreatedTime = DateTime.Now
            };
            batch = this.Insert(batch);
            return Result.Success("批次创建成功", batch);
        }

        /// <summary>导入消息到批次（自动去重）</summary>
        public virtual Result ImportMessages(int batchId, List<int> messageIds)
        {
            var batch = Select.Where(b => b.Id == batchId).First();
            if (batch == null) return Result.Fail("批次不存在");

            // 查已有的消息 ID，去重
            var existingIds = _fsql.Select<ChatBatchMessage>()
                .Where(m => m.BatchId == batchId)
                .ToList(m => m.MessageId);

            var newIds = messageIds.Where(id => !existingIds.Contains(id)).ToList();
            if (newIds.Count == 0) return Result.Success("所有消息已存在，无需导入");

            var records = newIds.Select(id => new ChatBatchMessage { BatchId = batchId, MessageId = id }).ToList();
            _fsql.Insert(records).ExecuteAffrows();

            var totalCount = existingIds.Count + newIds.Count;
            return Result.Success($"导入 {newIds.Count} 条消息（跳过 {messageIds.Count - newIds.Count} 条重复），共 {totalCount} 条");
        }

        /// <summary>AI 接单分析</summary>
        public virtual async Task<Result> Analyze(int batchId)
        {
            var batch = Select.Where(b => b.Id == batchId).First();
            if (batch == null) return Result.Fail("批次不存在");

            var messageIds = _fsql.Select<ChatBatchMessage>()
                .Where(m => m.BatchId == batchId).ToList(m => m.MessageId);
            if (messageIds.Count == 0) return Result.Fail("批次没有消息");

            var messages = _fsql.Select<ChatMessage>()
                .Where(m => messageIds.Contains(m.Id))
                .OrderBy(m => m.Id)
                .ToList();

            // 找群组绑定
            ChatGroupBinding? groupBinding = null;
            if (batch.GroupBindingId.HasValue)
            {
                groupBinding = _fsql.Select<ChatGroupBinding>()
                    .Where(g => g.Id == batch.GroupBindingId.Value).First();
            }
            if (groupBinding == null)
            {
                // 从消息中找
                var firstWithGroup = messages.FirstOrDefault(m => m.GroupBindingId.HasValue);
                if (firstWithGroup?.GroupBindingId != null)
                {
                    groupBinding = _fsql.Select<ChatGroupBinding>()
                        .Where(g => g.Id == firstWithGroup.GroupBindingId.Value).First();
                }
            }
            if (groupBinding == null) return Result.Fail("无法确定关联群组");

            // 更新状态
            _fsql.Update<ChatBatch>().Set(b => b.Status, ChatBatchStatus.Running)
                .Where(b => b.Id == batchId).ExecuteAffrows();

            try
            {
                var requirements = await _executor.ProcessSelectedMessages(messages, groupBinding);

                // 关联需求到批次
                if (requirements.Count > 0)
                {
                    var links = requirements.Select(r => new ChatBatchRequirement
                    {
                        BatchId = batchId, RequirementId = r.Id
                    }).ToList();
                    _fsql.Insert(links).ExecuteAffrows();
                }

                var summary = $"分析完成，提取 {requirements.Count} 条需求";
                _fsql.Update<ChatBatch>()
                    .Set(b => b.Status, ChatBatchStatus.Completed)
                    .Set(b => b.ResultSummary, summary)
                    .Set(b => b.CompletedTime, DateTime.Now)
                    .Where(b => b.Id == batchId).ExecuteAffrows();

                return Result.Success(summary, new { requirements, batchId });
            }
            catch (Exception ex)
            {
                _fsql.Update<ChatBatch>()
                    .Set(b => b.Status, ChatBatchStatus.Failed)
                    .Set(b => b.ResultSummary, $"失败: {ex.Message}")
                    .Where(b => b.Id == batchId).ExecuteAffrows();

                return Result.Fail($"分析失败: {ex.Message}");
            }
        }

        /// <summary>关联任务到批次（去重，同时写入 TaskItem.BatchId）</summary>
        public virtual Result LinkTasks(int batchId, List<int> taskIds)
        {
            var existingIds = _fsql.Select<ChatBatchTask>()
                .Where(t => t.BatchId == batchId).ToList(t => t.TaskId);
            var newIds = taskIds.Where(id => !existingIds.Contains(id)).ToList();
            if (newIds.Count == 0) return Result.Success("所有任务已关联");

            var records = newIds.Select(id => new ChatBatchTask { BatchId = batchId, TaskId = id }).ToList();
            _fsql.Insert(records).ExecuteAffrows();

            // 同步写入 TaskItem.BatchId
            var batchIdStr = batchId.ToString();
            _fsql.Update<TaskItem>()
                .Set(t => t.BatchId, batchIdStr)
                .Where(t => newIds.Contains(t.Id))
                .ExecuteAffrows();

            return Result.Success($"关联 {newIds.Count} 个任务");
        }

        /// <summary>关联正式需求到批次（写入 Requirement.BatchId）</summary>
        public virtual Result LinkRequirements(int batchId, List<int> requirementIds)
        {
            var batchIdStr = batchId.ToString();
            _fsql.Update<Requirement>()
                .Set(r => r.BatchId, batchIdStr)
                .Where(r => requirementIds.Contains(r.Id))
                .ExecuteAffrows();
            return Result.Success($"关联 {requirementIds.Count} 个需求");
        }

        /// <summary>从 Markdown 导入需求到批次</summary>
        public virtual Result ImportRequirementsFromMd(int batchId, string mdContent)
        {
            var batch = Select.Where(b => b.Id == batchId).First();
            if (batch == null) return Result.Fail("批次不存在");

            // 解析 ## REQ 块
            var reqs = ParseRequirementsFromMd(mdContent);
            if (reqs.Count == 0) return Result.Fail("未找到有效需求（需要 ## REQ 格式）");

            var created = new List<ChatRequirement>();
            foreach (var req in reqs)
            {
                var chatReq = new ChatRequirement
                {
                    GroupBindingId = batch.GroupBindingId ?? 0,
                    ProjectId = batch.ProjectId,
                    Title = req.Title,
                    Description = req.Description,
                    SourceMessageIds = req.SourceMessageIds.Count > 0
                        ? JsonSerializer.Serialize(req.SourceMessageIds) : null,
                    Status = ChatRequirementStatus.Draft,
                    CreatedTime = DateTime.Now
                };
                chatReq.Id = (int)_fsql.Insert(chatReq).ExecuteIdentity();
                created.Add(chatReq);

                // 关联到批次
                _fsql.Insert(new ChatBatchRequirement { BatchId = batchId, RequirementId = chatReq.Id }).ExecuteAffrows();
            }

            return Result.Success($"导入 {created.Count} 条需求", new { count = created.Count, requirements = created });
        }

        /// <summary>解析 MD 中的需求块（兼容多种格式）</summary>
        private static List<ParsedRequirement> ParseRequirementsFromMd(string md)
        {
            var results = new List<ParsedRequirement>();
            var lines = md.Split('\n');

            // 匹配: ## REQ 标题 / ### REQ-1 标题 / ## 需求1: 标题 / ### 1. 标题（带 REQ 关键字的各种变体）
            var headerPattern = new Regex(@"^#{2,4}\s+(?:REQ(?:[_\-]?\d*)?[:\s：]+)(.+)$", RegexOptions.IgnoreCase);
            // 匹配: 来源消息: #96 / **来源消息:** #96, #97 / 来源: #96 / Source: #96
            var sourcePattern = new Regex(@"^\*{0,2}(?:来源消息|来源|Source Messages?):?\*{0,2}\s*(.+)$", RegexOptions.IgnoreCase);

            ParsedRequirement? current = null;
            var descLines = new List<string>();
            var inCodeBlock = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');

                // 跳过代码块、HTML 注释、引用块（> 开头）中的内容
                if (line.TrimStart().StartsWith("```")) { inCodeBlock = !inCodeBlock; continue; }
                if (inCodeBlock) continue;
                if (line.TrimStart().StartsWith("<!--") || line.TrimStart().StartsWith("-->")) continue;
                if (line.TrimStart().StartsWith(">")) { if (current != null) descLines.Add(line); continue; }

                var headerMatch = headerPattern.Match(line);
                if (headerMatch.Success)
                {
                    if (current != null)
                    {
                        current.Description = CleanDescription(descLines);
                        results.Add(current);
                    }
                    current = new ParsedRequirement { Title = headerMatch.Groups[1].Value.Trim() };
                    descLines.Clear();
                    continue;
                }

                if (current == null) continue;

                var sourceMatch = sourcePattern.Match(line);
                if (sourceMatch.Success)
                {
                    var idMatches = Regex.Matches(sourceMatch.Groups[1].Value, @"#(\d+)");
                    foreach (Match m in idMatches)
                        current.SourceMessageIds.Add(int.Parse(m.Groups[1].Value));
                    continue;
                }

                descLines.Add(line);
            }

            if (current != null)
            {
                current.Description = CleanDescription(descLines);
                results.Add(current);
            }

            return results;
        }

        /// <summary>清理描述文本：去掉首尾空行和分隔线</summary>
        private static string CleanDescription(List<string> lines)
        {
            // 去掉尾部的 --- 分隔线和空行
            while (lines.Count > 0 && (string.IsNullOrWhiteSpace(lines[^1]) || lines[^1].Trim() == "---"))
                lines.RemoveAt(lines.Count - 1);
            // 去掉头部空行和元数据行（**优先级:** / **类型:** 等）
            while (lines.Count > 0 && (string.IsNullOrWhiteSpace(lines[0])
                || Regex.IsMatch(lines[0], @"^\*{2}(优先级|类型|Priority|Type):?\*{2}")))
                lines.RemoveAt(0);
            return string.Join("\n", lines).Trim();
        }

        private class ParsedRequirement
        {
            public string Title { get; set; } = "";
            public string? Description { get; set; }
            public List<int> SourceMessageIds { get; set; } = new();
        }
    }
}

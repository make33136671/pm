using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// 聊天消息存储与查询
    /// </summary>
    [AutoDependencyInjection]
    public class ChatMessageService : BaseCrudService<ChatMessage>
    {
        private readonly IFreeSql _fsql;

        public ChatMessageService(IFreeSql fsql, ILogger<ChatMessageService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public virtual Result GetList(int? groupBindingId = null, string? keyword = null, ChatMessageType? messageType = null,
            bool? aiProcessed = null, string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .Include(m => m.GroupBinding)
                .IncludeMany(m => m.MediaList)
                .WhereIf(groupBindingId.HasValue && groupBindingId.Value > 0, m => m.GroupBindingId == groupBindingId.Value)
                .WhereIf(groupBindingId.HasValue && groupBindingId.Value == 0, m => m.GroupBindingId == null)
                .WhereIf(!string.IsNullOrEmpty(keyword), m => m.Content!.Contains(keyword!) || m.SenderName!.Contains(keyword!))
                .WhereIf(messageType.HasValue, m => m.MessageType == messageType.Value)
                .WhereIf(aiProcessed.HasValue, m => m.AiProcessed == aiProcessed.Value);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public override Result Detail(int id)
        {
            var entity = Select
                .Include(m => m.GroupBinding)
                .IncludeMany(m => m.MediaList)
                .Where(m => m.Id == id).First();
            if (entity == null) return Result.Fail("消息不存在!");
            return Result.Success(entity);
        }

        /// <summary>
        /// 保存消息记录
        /// </summary>
        public virtual ChatMessage SaveMessage(ChatMessage message)
        {
            message.CreatedTime = DateTime.Now;
            return this.Insert(message);
        }

        /// <summary>
        /// 获取群组未处理的消息（AI 批量分析用）
        /// </summary>
        public virtual List<ChatMessage> GetUnprocessedMessages(int groupBindingId)
        {
            return Select
                .Where(m => m.GroupBindingId == groupBindingId && !m.AiProcessed)
                .OrderBy(m => m.Id)
                .ToList();
        }

        /// <summary>
        /// 生成消息导出 Markdown 内容
        /// </summary>
        public virtual string GenerateExportMd(List<int> messageIds, string baseUrl)
        {
            var messages = _fsql.Select<ChatMessage>()
                .Where(m => messageIds.Contains(m.Id))
                .IncludeMany(m => m.MediaList)
                .OrderBy(m => m.Id)
                .ToList();

            var lines = new List<string>
            {
                "# 聊天记录导出",
                "",
                $"> 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ",
                $"> 消息数量: {messages.Count}  ",
                $"> 消息ID: {string.Join(", ", messages.Select(m => m.Id))}",
                "",
                "---",
                ""
            };

            foreach (var msg in messages)
            {
                var time = msg.CreatedTime.ToString("MM-dd HH:mm");
                lines.Add($"### #{msg.Id} {msg.SenderName} ({time})");
                lines.Add("");

                if (!string.IsNullOrEmpty(msg.Content))
                {
                    lines.Add(msg.Content);
                    lines.Add("");
                }

                if (msg.MediaList != null)
                {
                    foreach (var media in msg.MediaList)
                    {
                        var mediaUrl = $"{baseUrl}/api/chat/media/{media.Id}";
                        if (media.MimeType?.StartsWith("image/") == true)
                            lines.Add($"![{media.FileName ?? "图片"}]({mediaUrl})");
                        else if (media.MimeType?.StartsWith("video/") == true)
                            lines.Add($"[视频: {media.FileName ?? "video"}]({mediaUrl})");
                        else
                            lines.Add($"[文件: {media.FileName ?? "file"}]({mediaUrl})");
                        lines.Add("");
                    }
                }

                lines.Add("---");
                lines.Add("");
            }

            // AI 需求分析区
            lines.Add("");
            lines.Add("---");
            lines.Add("");
            lines.Add("# AI 需求分析");
            lines.Add("");
            lines.Add("> **导入说明：** 本文件可直接导入系统。系统会解析所有 `## REQ` 开头的标题块作为需求。");
            lines.Add("> 你可以将本文件交给 AI 分析，在下方填写需求，然后导入。也可以手动编写。");
            lines.Add("> 文件中其他内容（标题、表格、说明文字等）会被忽略，只提取 `## REQ` 块。");
            lines.Add("");
            lines.Add("**格式要求（每条需求必须包含）：**");
            lines.Add("");
            lines.Add("- 标题行：`##` 或 `###` 开头，包含 `REQ` 关键字，后面是需求标题");
            lines.Add("- 来源行：包含 `来源消息` 字样，用 `#数字` 引用消息 ID（可选）");
            lines.Add("- 描述：标题行之后、下一个 REQ 之前的所有内容");
            lines.Add("- 代码块（` ``` `）内的内容会被跳过");
            lines.Add("");
            lines.Add("**AI Prompt 建议（可直接复制给 AI）：**");
            lines.Add("");
            lines.Add("> 请分析以上聊天记录，提取功能需求。每条需求严格按以下格式输出：");
            lines.Add("> ");
            lines.Add("> ## REQ 需求标题");
            lines.Add("> 来源消息: #消息ID, #消息ID");
            lines.Add("> ");
            lines.Add("> 需求的详细描述");
            lines.Add("");
            lines.Add("---");
            lines.Add("");
            lines.Add("<!-- 在此下方填写需求 -->");
            lines.Add("");

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 标记消息为已处理
        /// </summary>
        public virtual void MarkAsProcessed(List<int> messageIds)
        {
            if (messageIds.Count == 0) return;
            _fsql.Update<ChatMessage>()
                .Set(m => m.AiProcessed, true)
                .Where(m => messageIds.Contains(m.Id))
                .ExecuteAffrows();
        }
    }
}

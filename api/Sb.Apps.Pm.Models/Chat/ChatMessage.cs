using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// 聊天消息记录
    /// </summary>
    [Table(Name = "sb_pm_chat_message")]
    public class ChatMessage : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>关联群组绑定（私聊消息为 null）</summary>
        [Column(Name = "GroupBindingId")]
        public int? GroupBindingId { get; set; }
        [Navigate(nameof(GroupBindingId))]
        public ChatGroupBinding? GroupBinding { get; set; }

        /// <summary>通过哪个账号采集</summary>
        [Column(Name = "AccountId")]
        public int AccountId { get; set; }

        /// <summary>平台消息 ID</summary>
        [StringLength(50)]
        [Column(Name = "PlatformMessageId")]
        public string? PlatformMessageId { get; set; }

        /// <summary>发送人平台 ID</summary>
        [StringLength(50)]
        [Column(Name = "SenderId")]
        public string? SenderId { get; set; }

        /// <summary>发送人名称</summary>
        [StringLength(100)]
        [Column(Name = "SenderName")]
        public string? SenderName { get; set; }

        /// <summary>消息类型</summary>
        [Column(Name = "MessageType", MapType = typeof(string), StringLength = 20)]
        public ChatMessageType MessageType { get; set; } = ChatMessageType.Text;

        /// <summary>文本内容</summary>
        [Column(Name = "Content", StringLength = -1)]
        public string? Content { get; set; }

        /// <summary>回复的消息 ID</summary>
        [StringLength(50)]
        [Column(Name = "ReplyToMessageId")]
        public string? ReplyToMessageId { get; set; }

        /// <summary>原始消息 JSON</summary>
        [Column(Name = "RawData", StringLength = -1)]
        public string? RawData { get; set; }

        /// <summary>是否已 AI 处理</summary>
        [Column(Name = "AiProcessed")]
        public bool AiProcessed { get; set; } = false;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        /// <summary>媒体附件</summary>
        [Navigate(nameof(ChatMedia.MessageId))]
        public List<ChatMedia>? MediaList { get; set; }
    }
}

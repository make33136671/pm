using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// 聊天媒体文件
    /// </summary>
    [Table(Name = "sb_pm_chat_media")]
    public class ChatMedia : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>关联消息</summary>
        [Column(Name = "MessageId")]
        public int MessageId { get; set; }
        [Navigate(nameof(MessageId))]
        public ChatMessage? Message { get; set; }

        /// <summary>媒体类型</summary>
        [Column(Name = "MediaType", MapType = typeof(string), StringLength = 20)]
        public ChatMessageType MediaType { get; set; }

        /// <summary>平台文件 ID</summary>
        [StringLength(200)]
        [Column(Name = "PlatformFileId")]
        public string? PlatformFileId { get; set; }

        /// <summary>本地存储路径</summary>
        [StringLength(500)]
        [Column(Name = "LocalPath")]
        public string? LocalPath { get; set; }

        /// <summary>文件名</summary>
        [StringLength(200)]
        [Column(Name = "FileName")]
        public string? FileName { get; set; }

        /// <summary>文件大小（字节）</summary>
        [Column(Name = "FileSize")]
        public long FileSize { get; set; }

        /// <summary>MIME 类型</summary>
        [StringLength(100)]
        [Column(Name = "MimeType")]
        public string? MimeType { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

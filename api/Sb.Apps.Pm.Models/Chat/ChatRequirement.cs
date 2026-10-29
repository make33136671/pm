using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// AI 提炼的需求
    /// </summary>
    [Table(Name = "sb_pm_chat_requirement")]
    public class ChatRequirement : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>来源群组绑定</summary>
        [Column(Name = "GroupBindingId")]
        public int GroupBindingId { get; set; }
        [Navigate(nameof(GroupBindingId))]
        public ChatGroupBinding? GroupBinding { get; set; }

        /// <summary>关联项目</summary>
        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }

        /// <summary>需求标题</summary>
        [Required, StringLength(200)]
        [Column(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>需求描述</summary>
        [Column(Name = "Description", StringLength = -1)]
        public string? Description { get; set; }

        /// <summary>来源消息 ID 数组 JSON</summary>
        [Column(Name = "SourceMessageIds", StringLength = -1)]
        public string? SourceMessageIds { get; set; }

        /// <summary>状态</summary>
        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ChatRequirementStatus Status { get; set; } = ChatRequirementStatus.Draft;

        /// <summary>确认时间</summary>
        [Column(Name = "ConfirmedTime")]
        public DateTime? ConfirmedTime { get; set; }

        /// <summary>确认人 ID</summary>
        [Column(Name = "ConfirmedBy")]
        public int? ConfirmedBy { get; set; }

        /// <summary>确认后关联的任务 ID</summary>
        [Column(Name = "TaskId")]
        public int? TaskId { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

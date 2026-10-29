using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// 群组绑定（关联平台账号 ↔ 项目）
    /// </summary>
    [Table(Name = "sb_pm_chat_group_binding")]
    public class ChatGroupBinding : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>关联账号</summary>
        [Column(Name = "AccountId")]
        public int AccountId { get; set; }
        [Navigate(nameof(AccountId))]
        public ChatAccount? Account { get; set; }

        /// <summary>关联项目</summary>
        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project? Project { get; set; }

        /// <summary>平台群组 ID</summary>
        [StringLength(50)]
        [Column(Name = "PlatformGroupId")]
        public string? PlatformGroupId { get; set; }

        /// <summary>群组名称</summary>
        [StringLength(200)]
        [Column(Name = "GroupName")]
        public string? GroupName { get; set; }

        /// <summary>绑定码（6位，绑定后清空）</summary>
        [StringLength(10)]
        [Column(Name = "BindCode")]
        public string? BindCode { get; set; }

        /// <summary>AI 分析总开关</summary>
        [Column(Name = "AiEnabled")]
        public bool AiEnabled { get; set; } = false;

        /// <summary>AI 实时分析开关</summary>
        [Column(Name = "AiRealtimeEnabled")]
        public bool AiRealtimeEnabled { get; set; } = false;

        /// <summary>自动从聊天消息创建需求（AI 分析置信度 > 0.7 时静默建需求）</summary>
        [Column(Name = "AutoCreateRequirement", IsNullable = false, InsertValueSql = "false")]
        public bool AutoCreateRequirement { get; set; } = false;

        /// <summary>绑定状态</summary>
        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ChatGroupBindingStatus Status { get; set; } = ChatGroupBindingStatus.Pending;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

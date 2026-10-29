using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// 用户绑定（系统账号 ↔ 平台用户）
    /// </summary>
    [Table(Name = "sb_pm_chat_user_binding")]
    public class ChatUserBinding : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>平台类型</summary>
        [Required, StringLength(20)]
        [Column(Name = "PlatformType")]
        public string PlatformType { get; set; } = "Telegram";

        /// <summary>平台用户 ID</summary>
        [Required, StringLength(50)]
        [Column(Name = "PlatformUserId")]
        public string PlatformUserId { get; set; } = string.Empty;

        /// <summary>平台用户名</summary>
        [StringLength(100)]
        [Column(Name = "PlatformUserName")]
        public string? PlatformUserName { get; set; }

        /// <summary>关联项目</summary>
        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }

        /// <summary>关联系统内部用户 ID（nullable = 外部客户）</summary>
        [Column(Name = "AdminId")]
        public int? AdminId { get; set; }

        /// <summary>外部客户姓名</summary>
        [StringLength(100)]
        [Column(Name = "ExternalName")]
        public string? ExternalName { get; set; }

        /// <summary>外部客户公司</summary>
        [StringLength(200)]
        [Column(Name = "ExternalCompany")]
        public string? ExternalCompany { get; set; }

        /// <summary>角色</summary>
        [Column(Name = "Role", MapType = typeof(string), StringLength = 20)]
        public ChatUserRole Role { get; set; } = ChatUserRole.Client;

        /// <summary>验证码</summary>
        [StringLength(10)]
        [Column(Name = "VerifyCode")]
        public string? VerifyCode { get; set; }

        /// <summary>验证码过期时间</summary>
        [Column(Name = "VerifyExpireTime")]
        public DateTime? VerifyExpireTime { get; set; }

        /// <summary>状态</summary>
        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ChatUserBindingStatus Status { get; set; } = ChatUserBindingStatus.PendingVerify;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

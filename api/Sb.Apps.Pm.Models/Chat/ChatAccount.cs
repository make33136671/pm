using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// 聊天平台账号配置（Bot / 个人号）
    /// </summary>
    [Table(Name = "sb_pm_chat_account")]
    public class ChatAccount : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>平台类型（Telegram / WeChat / ...）</summary>
        [Required, StringLength(20)]
        [Column(Name = "PlatformType")]
        public string PlatformType { get; set; } = "Telegram";

        /// <summary>账号类型</summary>
        [Column(Name = "AccountType", MapType = typeof(string), StringLength = 20)]
        public ChatAccountType AccountType { get; set; } = ChatAccountType.Bot;

        /// <summary>显示名称</summary>
        [Required, StringLength(100)]
        [Column(Name = "AccountName")]
        public string AccountName { get; set; } = string.Empty;

        /// <summary>连接参数（加密存储 JSON：BotToken / ApiId+ApiHash+Phone 等）</summary>
        [Column(Name = "ConnectionConfig", StringLength = -1)]
        public string? ConnectionConfig { get; set; }

        /// <summary>运行环境（All=所有环境, Development=仅开发, Production=仅生产）</summary>
        [Column(Name = "Env", MapType = typeof(string), StringLength = 20)]
        public AppEnv Env { get; set; } = AppEnv.All;

        /// <summary>状态</summary>
        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ChatAccountStatus Status { get; set; } = ChatAccountStatus.Active;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

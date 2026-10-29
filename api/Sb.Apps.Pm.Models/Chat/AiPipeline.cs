using SB.Framework.Models;
using FreeSql.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// AI 管线配置（每个群组独立）
    /// </summary>
    [Table(Name = "sb_pm_ai_pipeline")]
    public class AiPipeline : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>关联群组绑定</summary>
        [Column(Name = "GroupBindingId")]
        public int GroupBindingId { get; set; }
        [Navigate(nameof(GroupBindingId))]
        public ChatGroupBinding? GroupBinding { get; set; }

        /// <summary>总开关</summary>
        [Column(Name = "Enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>实时分析开关</summary>
        [Column(Name = "RealtimeEnabled")]
        public bool RealtimeEnabled { get; set; } = false;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

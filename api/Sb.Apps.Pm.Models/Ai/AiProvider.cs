using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Ai
{
    [Table(Name = "sb_pm_ai_provider")]
    public class AiProvider : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Required, StringLength(30)]
        [Column(Name = "ProviderType")]
        public string ProviderType { get; set; }

        [Column(Name = "Endpoint", StringLength = 500)]
        public string? Endpoint { get; set; }

        [Column(Name = "ApiKey", StringLength = 500)]
        public string? ApiKey { get; set; }

        [Column(Name = "ModelName", StringLength = 100)]
        public string? ModelName { get; set; }

        [Column(Name = "ExtraConfig", StringLength = -1)]
        public string? ExtraConfig { get; set; }

        [Column(Name = "IsDefault", IsNullable = false, InsertValueSql = "false")]
        public bool IsDefault { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public AiProviderStatus Status { get; set; } = AiProviderStatus.Active;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }
    }
}

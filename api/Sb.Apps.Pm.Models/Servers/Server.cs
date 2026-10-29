using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Servers
{
    [Table(Name = "sb_pm_server")]
    public class Server : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        // ---- 基本信息 ----

        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Column(Name = "Description", StringLength = 500)]
        public string? Description { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ServerStatus Status { get; set; } = ServerStatus.Active;

        [Column(Name = "Tags", StringLength = 500)]
        public string? Tags { get; set; }

        // ---- 运营信息 ----

        [Column(Name = "Provider", StringLength = 100)]
        public string? Provider { get; set; }

        [Column(Name = "ProviderContact", StringLength = 500)]
        public string? ProviderContact { get; set; }

        [Column(Name = "Price", DbType = "decimal(10,2)")]
        public decimal? Price { get; set; }

        [Column(Name = "PaymentCycle", MapType = typeof(string), StringLength = 20)]
        public PaymentCycle? PaymentCycle { get; set; }

        [Column(Name = "ExpiryDate")]
        public DateTime? ExpiryDate { get; set; }

        // ---- 硬件信息 ----

        [Column(Name = "ServerType", MapType = typeof(string), StringLength = 20)]
        public ServerType ServerType { get; set; } = ServerType.Cloud;

        [Column(Name = "IsVirtual", IsNullable = false, InsertValueSql = "false")]
        public bool IsVirtual { get; set; }

        [Column(Name = "PhysicalHost", StringLength = 200)]
        public string? PhysicalHost { get; set; }

        // ---- SSH ----

        [Required, StringLength(200)]
        [Column(Name = "Host")]
        public string Host { get; set; }

        [Column(Name = "SshPort")]
        public int SshPort { get; set; } = 22;

        [Column(Name = "SshUsername", StringLength = 50)]
        public string? SshUsername { get; set; }

        [Column(Name = "SshPassword", StringLength = 500)]
        public string? SshPassword { get; set; }

        // ---- 宝塔面板 ----

        [Column(Name = "BtPanelUrl", StringLength = 500)]
        public string? BtPanelUrl { get; set; }

        [Column(Name = "BtApiKey", StringLength = 500)]
        public string? BtApiKey { get; set; }

        [Column(Name = "BtUsername", StringLength = 100)]
        public string? BtUsername { get; set; }

        [Column(Name = "BtPassword", StringLength = 500)]
        public string? BtPassword { get; set; }

        // ---- 数据库 ----

        [Column(Name = "DbType", MapType = typeof(string), StringLength = 20)]
        public DatabaseType? DbType { get; set; }

        [Column(Name = "DbHost", StringLength = 200)]
        public string? DbHost { get; set; }

        [Column(Name = "DbPort")]
        public int? DbPort { get; set; }

        [Column(Name = "DbRootUsername", StringLength = 50)]
        public string? DbRootUsername { get; set; }

        [Column(Name = "DbRootPassword", StringLength = 500)]
        public string? DbRootPassword { get; set; }

        [Column(Name = "DbVersion", StringLength = 50)]
        public string? DbVersion { get; set; }

        // ---- FTP ----

        [Column(Name = "FtpHost", StringLength = 200)]
        public string? FtpHost { get; set; }

        [Column(Name = "FtpPort")]
        public int? FtpPort { get; set; }

        [Column(Name = "FtpUsername", StringLength = 100)]
        public string? FtpUsername { get; set; }

        [Column(Name = "FtpPassword", StringLength = 500)]
        public string? FtpPassword { get; set; }

        // ---- 探测 ----

        [Column(Name = "LastDiscoveryTime")]
        public DateTime? LastDiscoveryTime { get; set; }

        [Column(Name = "DiscoveryLog", StringLength = -1)]
        public string? DiscoveryLog { get; set; }

        // ---- 标准字段 ----

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

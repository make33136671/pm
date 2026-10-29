using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_release_backup")]
    public class ReleaseBackup : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }

        [Column(Name = "EnvironmentId")]
        public int EnvironmentId { get; set; }

        [Column(Name = "ReleaseRecordId")]
        public int? ReleaseRecordId { get; set; }

        [Column(Name = "Version", StringLength = 50)]
        public string? Version { get; set; }

        [Required, StringLength(500)]
        [Column(Name = "BackupPath")]
        public string BackupPath { get; set; }

        [Column(Name = "BackupSize")]
        public long? BackupSize { get; set; }

        [Column(Name = "BackupType", MapType = typeof(string), StringLength = 20)]
        public BackupType BackupType { get; set; } = BackupType.Auto;

        [Column(Name = "Checksum", StringLength = 100)]
        public string? Checksum { get; set; }

        [Column(Name = "Remark", StringLength = 500)]
        public string? Remark { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "ExpireTime")]
        public DateTime? ExpireTime { get; set; }
    }
}

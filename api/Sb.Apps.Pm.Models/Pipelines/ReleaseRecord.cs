using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_release_record")]
    public class ReleaseRecord : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Column(Name = "EnvironmentId")]
        public int EnvironmentId { get; set; }

        [Column(Name = "BuildRecordId")]
        public int? BuildRecordId { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "Version")]
        public string Version { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ReleaseStatus Status { get; set; }

        [Column(Name = "ApprovalStatus", MapType = typeof(string), StringLength = 20)]
        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.None;

        [Column(Name = "ApprovedBy", StringLength = 100)]
        public string? ApprovedBy { get; set; }

        [Column(Name = "ApprovedAt")]
        public DateTime? ApprovedAt { get; set; }

        [Column(Name = "DeployMethod", MapType = typeof(string), StringLength = 20)]
        public DeployMethod? DeployMethod { get; set; }

        [Column(Name = "DeployLog", StringLength = -1)]
        public string? DeployLog { get; set; }

        [Column(Name = "BackupId")]
        public int? BackupId { get; set; }

        [Column(Name = "ReleaseNote", StringLength = -1)]
        public string? ReleaseNote { get; set; }

        [Column(Name = "StartedAt")]
        public DateTime? StartedAt { get; set; }

        [Column(Name = "FinishedAt")]
        public DateTime? FinishedAt { get; set; }

        [Column(Name = "RollbackFromId")]
        public int? RollbackFromId { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "OperatorName", StringLength = 100)]
        public string? OperatorName { get; set; }
    }
}

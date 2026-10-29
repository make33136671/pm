using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.DbMigration
{
    [Table(Name = "sb_pm_db_migration_script")]
    public class DbMigrationScript : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Column(Name = "EnvironmentId")]
        public int? EnvironmentId { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "Version")]
        public string Version { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "Title")]
        public string Title { get; set; }

        [Required]
        [Column(Name = "SqlContent", StringLength = -1)]
        public string SqlContent { get; set; }

        [Column(Name = "RollbackSql", StringLength = -1)]
        public string? RollbackSql { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public MigrationStatus Status { get; set; } = MigrationStatus.Pending;

        [Column(Name = "ExecutedAt")]
        public DateTime? ExecutedAt { get; set; }

        [Column(Name = "ExecutedBy", StringLength = 100)]
        public string? ExecutedBy { get; set; }

        [Column(Name = "ReleaseRecordId")]
        public int? ReleaseRecordId { get; set; }

        [Column(Name = "Remark", StringLength = 500)]
        public string? Remark { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

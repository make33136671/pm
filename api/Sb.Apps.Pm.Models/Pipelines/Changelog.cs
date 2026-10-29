using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_changelog")]
    public class Changelog : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "Version")]
        public string Version { get; set; }

        [Required]
        [Column(Name = "Content", StringLength = -1)]
        public string Content { get; set; }

        [Column(Name = "GeneratedBy", MapType = typeof(string), StringLength = 20)]
        public GeneratedBy GeneratedBy { get; set; } = GeneratedBy.Manual;

        [Column(Name = "ReleaseRecordId")]
        public int? ReleaseRecordId { get; set; }

        [Column(Name = "PublishedAt")]
        public DateTime? PublishedAt { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

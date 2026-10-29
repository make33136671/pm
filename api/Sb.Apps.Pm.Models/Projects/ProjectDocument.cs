using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_document")]
    public class ProjectDocument : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "Title")]
        public string Title { get; set; }

        [Column(Name = "Category", StringLength = 50)]
        public string? Category { get; set; }

        [Column(Name = "FilePath", StringLength = 500)]
        public string? FilePath { get; set; }

        [Column(Name = "FileSize")]
        public long? FileSize { get; set; }

        [Column(Name = "FileType", StringLength = 20)]
        public string? FileType { get; set; }

        [Column(Name = "Content", StringLength = -1)]
        public string? Content { get; set; }

        /// <summary>扩展元数据（JSON），不同分类存储不同键值对</summary>
        [Column(Name = "Metadata", StringLength = -1)]
        public string? Metadata { get; set; }

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "OperatorName", StringLength = 100)]
        public string? OperatorName { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Documents
{
    [Table(Name = "sb_pm_notebook")]
    public class Notebook : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "AdminId")]
        public int AdminId { get; set; }

        [Column(Name = "Title", StringLength = 200)]
        public string? Title { get; set; }

        [Required]
        [Column(Name = "Content", StringLength = -1)]
        public string Content { get; set; }

        [Column(Name = "Color", StringLength = 20)]
        public string Color { get; set; } = "#ffffff";

        [Column(Name = "IsPinned")]
        public bool IsPinned { get; set; } = false;

        [Column(Name = "Tags", StringLength = 500)]
        public string? Tags { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

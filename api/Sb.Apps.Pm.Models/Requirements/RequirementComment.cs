using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Requirements
{
    [Table(Name = "sb_pm_requirement_comment")]
    public class RequirementComment : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "RequirementId")]
        public int RequirementId { get; set; }
        [Navigate(nameof(RequirementId))]
        public Requirement Requirement { get; set; }

        [Required]
        [Column(Name = "Content", StringLength = -1)]
        public string Content { get; set; }

        [Column(Name = "AuthorName", StringLength = 100)]
        public string? AuthorName { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

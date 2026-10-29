using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_approval_record")]
    public class ApprovalRecord : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "TargetType")]
        public string TargetType { get; set; }

        [Column(Name = "TargetId")]
        public int TargetId { get; set; }

        [Required, StringLength(20)]
        [Column(Name = "Action")]
        public string Action { get; set; }

        [Column(Name = "Comment", StringLength = -1)]
        public string? Comment { get; set; }

        [Required, StringLength(100)]
        [Column(Name = "OperatorName")]
        public string OperatorName { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

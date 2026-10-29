using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_activity")]
    public class ProjectActivity : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "TargetType")]
        public string TargetType { get; set; }

        [Column(Name = "TargetId")]
        public int TargetId { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "Action")]
        public string Action { get; set; }

        [Required, StringLength(500)]
        [Column(Name = "Summary")]
        public string Summary { get; set; }

        [Column(Name = "Detail", StringLength = -1)]
        public string? Detail { get; set; }

        [Column(Name = "OperatorName", StringLength = 100)]
        public string? OperatorName { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

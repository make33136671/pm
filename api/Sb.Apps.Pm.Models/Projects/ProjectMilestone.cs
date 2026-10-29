using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_milestone")]
    public class ProjectMilestone : IEntity, ISoftDelete
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

        [Column(Name = "Description", StringLength = -1)]
        public string? Description { get; set; }

        [Column(Name = "DueDate")]
        public DateTime? DueDate { get; set; }

        [Column(Name = "CompletedDate")]
        public DateTime? CompletedDate { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public MilestoneStatus Status { get; set; } = MilestoneStatus.Open;

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_environment")]
    public class ProjectEnvironment : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Required, StringLength(20)]
        [Column(Name = "Code")]
        public string Code { get; set; }

        [Column(Name = "Description", StringLength = 500)]
        public string? Description { get; set; }

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

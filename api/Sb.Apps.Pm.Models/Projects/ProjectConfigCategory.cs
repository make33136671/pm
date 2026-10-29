using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_config_category")]
    public class ProjectConfigCategory : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "Code")]
        public string Code { get; set; }

        [Column(Name = "Icon", StringLength = 100)]
        public string? Icon { get; set; }

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "Description", StringLength = 500)]
        public string? Description { get; set; }
    }
}

using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_template_config")]
    public class ProjectTemplateConfig : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "TemplateId")]
        public int TemplateId { get; set; }
        [Navigate(nameof(TemplateId))]
        public ProjectTemplate Template { get; set; }

        [Column(Name = "CategoryId")]
        public int CategoryId { get; set; }
        [Navigate(nameof(CategoryId))]
        public ProjectConfigCategory Category { get; set; }

        [Required, StringLength(100)]
        [Column(Name = "ConfigKey")]
        public string ConfigKey { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "ConfigName")]
        public string ConfigName { get; set; }

        [Column(Name = "DefaultValue", StringLength = -1)]
        public string? DefaultValue { get; set; }

        [Column(Name = "IsSensitive")]
        public bool IsSensitive { get; set; } = false;

        [Column(Name = "DisplayType")]
        public int DisplayType { get; set; } = 0;

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "Remark", StringLength = 500)]
        public string? Remark { get; set; }
    }
}

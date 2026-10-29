using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_config")]
    public class ProjectConfig : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Column(Name = "EnvironmentId")]
        public int? EnvironmentId { get; set; }
        [Navigate(nameof(EnvironmentId))]
        public ProjectEnvironment? Environment { get; set; }

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

        [Column(Name = "ConfigValue", StringLength = -1)]
        public string? ConfigValue { get; set; }

        [Column(Name = "IsSensitive")]
        public bool IsSensitive { get; set; } = false;

        [Column(Name = "DisplayType")]
        public ConfigDisplayType DisplayType { get; set; } = ConfigDisplayType.Input;

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "Remark", StringLength = 500)]
        public string? Remark { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

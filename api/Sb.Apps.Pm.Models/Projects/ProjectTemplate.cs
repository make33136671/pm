using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_template")]
    public class ProjectTemplate : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Column(Name = "Description", StringLength = -1)]
        public string? Description { get; set; }

        [Column(Name = "TechStack", StringLength = 500)]
        public string? TechStack { get; set; }

        [Column(Name = "DefaultEnvs", StringLength = 500)]
        public string DefaultEnvs { get; set; } = "dev,test,prod";

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

using SB.Framework.Models;
using FreeSql.DataAnnotations;

namespace Sb.Apps.Pm.Models.Requirements
{
    [Table(Name = "sb_pm_requirement_feature_rel")]
    public class RequirementFeatureRel : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "RequirementId")]
        public int RequirementId { get; set; }
        [Navigate(nameof(RequirementId))]
        public Requirement Requirement { get; set; }

        [Column(Name = "FeatureModuleId")]
        public int FeatureModuleId { get; set; }
        [Navigate(nameof(FeatureModuleId))]
        public FeatureModule FeatureModule { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

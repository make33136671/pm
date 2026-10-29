using SB.Framework.Models;
using FreeSql.DataAnnotations;
using Sb.Apps.Pm.Models.FeatureModules;

namespace Sb.Apps.Pm.Models.ApiEndpoints
{
    /// <summary>接口 ↔ 功能模块 多对多关联</summary>
    [Table(Name = "sb_pm_api_endpoint_feature_rel")]
    public class ApiEndpointFeatureRel : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ApiEndpointId")]
        public int ApiEndpointId { get; set; }
        [Navigate(nameof(ApiEndpointId))]
        public ApiEndpoint ApiEndpoint { get; set; }

        [Column(Name = "FeatureModuleId")]
        public int FeatureModuleId { get; set; }
        [Navigate(nameof(FeatureModuleId))]
        public FeatureModule FeatureModule { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

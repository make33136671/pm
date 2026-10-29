using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Models.FeatureModules
{
    [Table(Name = "sb_pm_feature_module")]
    public class FeatureModule : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Column(Name = "ParentId")]
        public int? ParentId { get; set; }

        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Column(Name = "Description", StringLength = 500)]
        public string? Description { get; set; }

        /// <summary>
        /// 手动覆盖的实现状态（可空）。null=按关联需求自动派生，非空=手动锁定。
        /// 注意：不用 MapType=string——FreeSql 对「可空枚举+字符串映射」读 NULL 会错误返回枚举默认值，
        /// 用默认 int? 映射，NULL 语义正确。
        /// </summary>
        [Column(Name = "StatusInt")]
        public FeatureModuleStatus? Status { get; set; }

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

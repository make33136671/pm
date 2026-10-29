using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Requirements
{
    [Table(Name = "sb_pm_requirement")]
    public class Requirement : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Column(Name = "CustomerId")]
        public int? CustomerId { get; set; }
        [Navigate(nameof(CustomerId))]
        public ProjectCustomer Customer { get; set; }

        /// <summary>关联功能模块（可空）— 三层关联链：功能模块 ← 需求 ← 任务</summary>
        [Column(Name = "FeatureModuleId")]
        public int? FeatureModuleId { get; set; }
        [Navigate(nameof(FeatureModuleId))]
        public FeatureModule? FeatureModule { get; set; }

        [Required, StringLength(500)]
        [Column(Name = "Title")]
        public string Title { get; set; }

        [Column(Name = "Content", StringLength = -1)]
        public string? Content { get; set; }

        [Column(Name = "Source", MapType = typeof(string), StringLength = 20)]
        public RequirementSource Source { get; set; } = RequirementSource.Manual;

        [Column(Name = "SourceRef", StringLength = 500)]
        public string? SourceRef { get; set; }

        [Column(Name = "Type", MapType = typeof(string), StringLength = 20)]
        public RequirementType Type { get; set; } = RequirementType.Feature;

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public RequirementStatus Status { get; set; } = RequirementStatus.Draft;

        [Column(Name = "Complexity", MapType = typeof(string), StringLength = 20)]
        public Complexity Complexity { get; set; } = Complexity.Medium;

        [Column(Name = "Assignee", StringLength = 100)]
        public string? Assignee { get; set; }

        [Column(Name = "EstimatedHours", DbType = "decimal(8,2)")]
        public decimal? EstimatedHours { get; set; }

        [Column(Name = "ActualHours", DbType = "decimal(8,2)")]
        public decimal? ActualHours { get; set; }

        [Column(Name = "ParentId")]
        public int? ParentId { get; set; }

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "DueDate")]
        public DateTime? DueDate { get; set; }

        [Column(Name = "CompletedDate")]
        public DateTime? CompletedDate { get; set; }

        // ---- AI 生成相关 ----

        [Column(Name = "IsAiFriendly", IsNullable = false, InsertValueSql = "false")]
        public bool IsAiFriendly { get; set; }

        [Column(Name = "IsConfirmed", IsNullable = false, InsertValueSql = "true")]
        public bool IsConfirmed { get; set; } = true;

        [Column(Name = "BatchId", StringLength = 50)]
        public string? BatchId { get; set; }

        [Column(Name = "BatchLabel", StringLength = 200)]
        public string? BatchLabel { get; set; }

        [Column(Name = "BatchDescription", StringLength = -1)]
        public string? BatchDescription { get; set; }

        [Column(Name = "AiProviderId")]
        public int? AiProviderId { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

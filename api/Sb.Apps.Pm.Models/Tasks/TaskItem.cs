using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Tasks
{
    [Table(Name = "sb_pm_task_item")]
    public class TaskItem : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Column(Name = "RequirementId")]
        public int? RequirementId { get; set; }
        [Navigate(nameof(RequirementId))]
        public Requirement? Requirement { get; set; }

        [Column(Name = "FeatureModuleId")]
        public int? FeatureModuleId { get; set; }
        [Navigate(nameof(FeatureModuleId))]
        public FeatureModule? FeatureModule { get; set; }

        [Column(Name = "ParentId")]
        public int? ParentId { get; set; }

        // ---- 任务信息 ----

        [Required, StringLength(200)]
        [Column(Name = "Title")]
        public string Title { get; set; }

        [Column(Name = "Content", StringLength = -1)]
        public string? Content { get; set; }

        [Column(Name = "Type", MapType = typeof(string), StringLength = 20)]
        public TaskItemType Type { get; set; } = TaskItemType.Development;

        [Column(Name = "Complexity", MapType = typeof(string), StringLength = 20)]
        public Complexity Complexity { get; set; } = Complexity.Medium;

        // ---- 接单 ----

        [Column(Name = "ClaimStatus", MapType = typeof(string), StringLength = 20)]
        public TaskItemClaimStatus ClaimStatus { get; set; } = TaskItemClaimStatus.Pending;

        [Column(Name = "ClaimedBy", StringLength = 100)]
        public string? ClaimedBy { get; set; }

        [Column(Name = "ClaimedTime")]
        public DateTime? ClaimedTime { get; set; }

        // ---- 来源 / 自动化 ----

        /// <summary>是否允许 Agent 自动认领并执行（配合 Hermes Worker 推送闭环）</summary>
        [Column(Name = "AutoAssign", IsNullable = false, InsertValueSql = "false")]
        public bool AutoAssign { get; set; } = false;

        /// <summary>任务来源: manual / tg_bot / hermes / ai_auto</summary>
        [Column(Name = "Source", StringLength = 20)]
        public string? Source { get; set; }

        /// <summary>来源引用（TG 消息 ID / Webhook 来源等）</summary>
        [Column(Name = "SourceRef", StringLength = 500)]
        public string? SourceRef { get; set; }

        // ---- 执行 ----

        [Column(Name = "ExecMode", MapType = typeof(string), StringLength = 20)]
        public TaskItemExecMode ExecMode { get; set; } = TaskItemExecMode.Api;

        [Column(Name = "ExecStatus", MapType = typeof(string), StringLength = 20)]
        public TaskItemExecStatus ExecStatus { get; set; } = TaskItemExecStatus.Idle;

        [Column(Name = "AiProviderId")]
        public int? AiProviderId { get; set; }

        [Column(Name = "FailCount", IsNullable = false, InsertValueSql = "0")]
        public int FailCount { get; set; } = 0;

        [Column(Name = "MaxRetryCount", IsNullable = false, InsertValueSql = "3")]
        public int MaxRetryCount { get; set; } = 3;

        [Column(Name = "TimeoutSeconds", IsNullable = false, InsertValueSql = "300")]
        public int TimeoutSeconds { get; set; } = 300;

        [Column(Name = "ExecStartTime")]
        public DateTime? ExecStartTime { get; set; }

        [Column(Name = "ExecEndTime")]
        public DateTime? ExecEndTime { get; set; }

        // ---- 工时 ----

        [Column(Name = "Assignee", StringLength = 100)]
        public string? Assignee { get; set; }

        [Column(Name = "EstimatedHours", DbType = "decimal(8,2)")]
        public decimal? EstimatedHours { get; set; }

        [Column(Name = "ActualHours", DbType = "decimal(8,2)")]
        public decimal? ActualHours { get; set; }

        [Column(Name = "DueDate")]
        public DateTime? DueDate { get; set; }

        [Column(Name = "CompletedDate")]
        public DateTime? CompletedDate { get; set; }

        // ---- 验收 (#102 #104 #105) ----

        /// <summary>执行者提交验收时填写的修复说明</summary>
        [Column(Name = "FixedDescription", StringLength = -1)]
        public string? FixedDescription { get; set; }

        /// <summary>验收是否通过</summary>
        [Column(Name = "Verified", IsNullable = false, InsertValueSql = "false")]
        public bool Verified { get; set; } = false;

        /// <summary>退回原因</summary>
        [Column(Name = "RejectReason", StringLength = 500)]
        public string? RejectReason { get; set; }

        /// <summary>关联路由（任务发布时标注，辅助 AI 定位）</summary>
        [Column(Name = "RouteHint", StringLength = 200)]
        public string? RouteHint { get; set; }

        /// <summary>关联代码文件路径列表（逗号分隔）</summary>
        [Column(Name = "FilePathHint", StringLength = 1000)]
        public string? FilePathHint { get; set; }

        /// <summary>关联代码行号说明</summary>
        [Column(Name = "LineHint", StringLength = 200)]
        public string? LineHint { get; set; }

        // ---- Git 提交关联（交付时上报，#任务交付追溯） ----

        /// <summary>交付时关联的 Git commit SHA</summary>
        [Column(Name = "CommitSha", StringLength = 100)]
        public string? CommitSha { get; set; }

        /// <summary>提交所在分支</summary>
        [Column(Name = "CommitBranch", StringLength = 200)]
        public string? CommitBranch { get; set; }

        /// <summary>该 commit 改动的文件列表（JSON：[{path,status}]）</summary>
        [Column(Name = "ChangedFilesList", StringLength = -1)]
        public string? ChangedFilesList { get; set; }

        /// <summary>提交者</summary>
        [Column(Name = "CommitAuthor", StringLength = 100)]
        public string? CommitAuthor { get; set; }

        // ---- 批次 ----

        [Column(Name = "BatchId", StringLength = 50)]
        public string? BatchId { get; set; }

        // ---- 通用 ----

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

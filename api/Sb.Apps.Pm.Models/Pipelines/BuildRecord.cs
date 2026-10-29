using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_build_record")]
    public class BuildRecord : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project? Project { get; set; }

        /// <summary>
        /// 由哪个 PipelineProfile 预设触发
        /// </summary>
        [Column(Name = "PipelineProfileId")]
        public int? PipelineProfileId { get; set; }
        [Navigate(nameof(PipelineProfileId))]
        public PipelineProfile? PipelineProfile { get; set; }

        [Column(Name = "EnvironmentId")]
        public int? EnvironmentId { get; set; }

        /// <summary>
        /// GitLab Pipeline ID，用于查询状态和日志
        /// </summary>
        [Column(Name = "GitLabPipelineId")]
        public int? GitLabPipelineId { get; set; }

        [Column(Name = "Version", StringLength = 50)]
        public string? Version { get; set; }

        [Column(Name = "BuildType", MapType = typeof(string), StringLength = 20)]
        public BuildType BuildType { get; set; } = BuildType.Manual;

        [Column(Name = "TriggerRef", StringLength = 200)]
        public string? TriggerRef { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public BuildStatus Status { get; set; }

        [Column(Name = "ArtifactPath", StringLength = 500)]
        public string? ArtifactPath { get; set; }

        [Column(Name = "ArtifactSize")]
        public long? ArtifactSize { get; set; }

        [Column(Name = "BuildLog", StringLength = -1)]
        public string? BuildLog { get; set; }

        [Column(Name = "CommitSha", StringLength = 50)]
        public string? CommitSha { get; set; }

        [Column(Name = "StartedAt")]
        public DateTime? StartedAt { get; set; }

        [Column(Name = "FinishedAt")]
        public DateTime? FinishedAt { get; set; }

        [Column(Name = "Duration")]
        public int? Duration { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "OperatorName", StringLength = 100)]
        public string? OperatorName { get; set; }

        /// <summary>
        /// CI 结果回写：关联到哪个需求（可空）
        /// </summary>
        [Column(Name = "RequirementId")]
        public int? RequirementId { get; set; }

        /// <summary>
        /// CI 结果回写：关联到哪个任务（可空）
        /// </summary>
        [Column(Name = "TaskItemId")]
        public int? TaskItemId { get; set; }

        /// <summary>
        /// 测试报告汇总缓存（JSON：total/passed/failed/skipped 等），避免每次都打 GitLab。
        /// 用例明细按需实时拉取，不落库。
        /// </summary>
        [Column(Name = "TestSummary", StringLength = -1)]
        public string? TestSummary { get; set; }
    }
}

using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Ai
{
    [Table(Name = "sb_pm_ai_task")]
    public class AiTask : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int? ProjectId { get; set; }

        [Column(Name = "RequirementId")]
        public int? RequirementId { get; set; }

        [Column(Name = "TaskType", MapType = typeof(string), StringLength = 50)]
        public AiTaskType TaskType { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public AiTaskStatus Status { get; set; } = AiTaskStatus.Pending;

        [Column(Name = "InputData", StringLength = -1)]
        public string? InputData { get; set; }

        [Column(Name = "OutputData", StringLength = -1)]
        public string? OutputData { get; set; }

        [Column(Name = "AiModel", StringLength = 50)]
        public string? AiModel { get; set; }

        [Column(Name = "TokensUsed")]
        public int? TokensUsed { get; set; }

        [Column(Name = "ErrorMessage", StringLength = -1)]
        public string? ErrorMessage { get; set; }

        [Column(Name = "StartedAt")]
        public DateTime? StartedAt { get; set; }

        [Column(Name = "FinishedAt")]
        public DateTime? FinishedAt { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "OperatorName", StringLength = 100)]
        public string? OperatorName { get; set; }
    }
}

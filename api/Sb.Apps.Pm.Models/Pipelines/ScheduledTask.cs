using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_scheduled_task")]
    public class ScheduledTask : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Column(Name = "TaskType", MapType = typeof(string), StringLength = 50)]
        public ScheduledTaskType TaskType { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "CronExpression")]
        public string CronExpression { get; set; }

        [Column(Name = "ProjectId")]
        public int? ProjectId { get; set; }

        [Column(Name = "Config", StringLength = -1)]
        public string? Config { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ScheduledTaskStatus Status { get; set; } = ScheduledTaskStatus.Active;

        [Column(Name = "LastRunTime")]
        public DateTime? LastRunTime { get; set; }

        [Column(Name = "LastRunStatus", StringLength = 20)]
        public string? LastRunStatus { get; set; }

        [Column(Name = "LastRunLog", StringLength = -1)]
        public string? LastRunLog { get; set; }

        [Column(Name = "NextRunTime")]
        public DateTime? NextRunTime { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

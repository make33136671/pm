using SB.Framework.Models;
using FreeSql.DataAnnotations;

namespace Sb.Apps.Pm.Models.Tasks
{
    [Table(Name = "sb_pm_task_item_log")]
    public class TaskItemLog : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "TaskItemId")]
        public int TaskItemId { get; set; }
        [Navigate(nameof(TaskItemId))]
        public TaskItem TaskItem { get; set; }

        [Column(Name = "Action", MapType = typeof(string), StringLength = 20)]
        public TaskItemLogAction Action { get; set; }

        [Column(Name = "Message", StringLength = -1)]
        public string? Message { get; set; }

        [Column(Name = "Operator", StringLength = 100)]
        public string? Operator { get; set; }

        [Column(Name = "Duration")]
        public int? Duration { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

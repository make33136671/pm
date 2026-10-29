using FreeSql.DataAnnotations;

namespace Sb.Apps.Pm.Models.Tasks
{
    /// <summary>任务附件 — 支持验收/提交/退回时上传文件 (#103)</summary>
    [Table(Name = "sb_pm_task_attachment")]
    public class TaskItemAttachment
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "TaskItemId")]
        public int TaskItemId { get; set; }

        /// <summary>附件关联阶段: submit_review / approve / reject</summary>
        [Column(Name = "Stage", StringLength = 30)]
        public string Stage { get; set; } = "submit_review";

        [Column(Name = "FileName", StringLength = 200)]
        public string FileName { get; set; } = "";

        [Column(Name = "FileUrl", StringLength = 500)]
        public string FileUrl { get; set; } = "";

        [Column(Name = "FileSize")]
        public long FileSize { get; set; }

        [Column(Name = "MimeType", StringLength = 100)]
        public string MimeType { get; set; } = "";

        [Column(Name = "UploadedBy", StringLength = 100)]
        public string? UploadedBy { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

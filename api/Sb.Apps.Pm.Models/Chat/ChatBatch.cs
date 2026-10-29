using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// 分析批次 — 消息和需求的桥梁
    /// 消息通过批次关联，AI 分析批次内的消息，生成的需求也关联到批次
    /// 不影响消息和需求各自的独立性和项目关联
    /// </summary>
    [Table(Name = "sb_pm_chat_batch")]
    public class ChatBatch : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>批次名称</summary>
        [Required, StringLength(200)]
        [Column(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>关联项目</summary>
        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project? Project { get; set; }

        /// <summary>关联群组绑定（可选）</summary>
        [Column(Name = "GroupBindingId")]
        public int? GroupBindingId { get; set; }

        /// <summary>批次状态</summary>
        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ChatBatchStatus Status { get; set; } = ChatBatchStatus.Pending;

        /// <summary>分析结果摘要</summary>
        [Column(Name = "ResultSummary", StringLength = -1)]
        public string? ResultSummary { get; set; }

        /// <summary>创建人</summary>
        [Column(Name = "CreatedBy")]
        public int? CreatedBy { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "CompletedTime")]
        public DateTime? CompletedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }

    /// <summary>
    /// 批次-消息关联（多对多）
    /// </summary>
    [Table(Name = "sb_pm_chat_batch_message")]
    public class ChatBatchMessage
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "BatchId")]
        public int BatchId { get; set; }

        [Column(Name = "MessageId")]
        public int MessageId { get; set; }
    }

    /// <summary>
    /// 批次-需求关联（多对多：AI 分析生成的需求关联到批次）
    /// </summary>
    [Table(Name = "sb_pm_chat_batch_requirement")]
    public class ChatBatchRequirement
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "BatchId")]
        public int BatchId { get; set; }

        [Column(Name = "RequirementId")]
        public int RequirementId { get; set; }
    }

    /// <summary>
    /// 批次-任务关联（多对多：需求确认后创建的任务关联到批次）
    /// </summary>
    [Table(Name = "sb_pm_chat_batch_task")]
    public class ChatBatchTask
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "BatchId")]
        public int BatchId { get; set; }

        [Column(Name = "TaskId")]
        public int TaskId { get; set; }
    }
}

using SB.Framework.Models;
using FreeSql.DataAnnotations;

namespace Sb.Apps.Pm.Models.Documents
{
    /// <summary>
    /// 文档版本 —— 记录 Requirement / TaskItem 每次内容变更，支持回档
    /// </summary>
    [Table(Name = "sb_pm_doc_version")]
    public class DocVersion : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>来源类型: Requirement / TaskItem</summary>
        [Column(Name = "SourceType", StringLength = 20)]
        public string SourceType { get; set; } = "";

        /// <summary>来源 ID</summary>
        [Column(Name = "SourceId")]
        public int SourceId { get; set; }

        /// <summary>版本号（同一来源自增）</summary>
        [Column(Name = "Version")]
        public int Version { get; set; }

        /// <summary>该版本的完整 MD 内容</summary>
        [Column(Name = "Content", StringLength = -1)]
        public string? Content { get; set; }

        /// <summary>变更说明</summary>
        [Column(Name = "ChangeNote", StringLength = 200)]
        public string? ChangeNote { get; set; }

        /// <summary>操作人</summary>
        [Column(Name = "Operator", StringLength = 100)]
        public string? Operator { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

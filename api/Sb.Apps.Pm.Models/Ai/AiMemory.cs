using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Ai
{
    [Table(Name = "sb_pm_ai_memory")]
    public class AiMemory : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int? ProjectId { get; set; }

        [Column(Name = "Scope", MapType = typeof(string), StringLength = 20)]
        public AiMemoryScope Scope { get; set; }

        [Column(Name = "Category", StringLength = 100)]
        public string? Category { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "Title")]
        public string Title { get; set; }

        [Required]
        [Column(Name = "Content", StringLength = -1)]
        public string Content { get; set; }

        [Column(Name = "Source", StringLength = 50)]
        public string Source { get; set; } = "Manual";

        [Column(Name = "Importance")]
        public int Importance { get; set; } = 5;

        [Column(Name = "Tags", StringLength = 500)]
        public string? Tags { get; set; }

        [Column(Name = "LastAccessedAt")]
        public DateTime? LastAccessedAt { get; set; }

        [Column(Name = "AccessCount")]
        public int AccessCount { get; set; } = 0;

        [Column(Name = "ExpiresAt")]
        public DateTime? ExpiresAt { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

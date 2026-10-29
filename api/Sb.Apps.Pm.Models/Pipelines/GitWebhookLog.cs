using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_git_webhook_log")]
    public class GitWebhookLog : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "RepositoryId")]
        public int RepositoryId { get; set; }
        [Navigate(nameof(RepositoryId))]
        public GitRepository Repository { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "EventType")]
        public string EventType { get; set; }

        [Required]
        [Column(Name = "Payload", StringLength = -1)]
        public string Payload { get; set; }

        [Column(Name = "Branch", StringLength = 100)]
        public string? Branch { get; set; }

        [Column(Name = "CommitSha", StringLength = 50)]
        public string? CommitSha { get; set; }

        [Column(Name = "AuthorName", StringLength = 100)]
        public string? AuthorName { get; set; }

        [Column(Name = "Summary", StringLength = 500)]
        public string? Summary { get; set; }

        [Column(Name = "Processed")]
        public bool Processed { get; set; } = false;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

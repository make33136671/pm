using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_git_pipeline")]
    public class GitPipeline : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "RepositoryId")]
        public int RepositoryId { get; set; }
        [Navigate(nameof(RepositoryId))]
        public GitRepository Repository { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "PipelineId")]
        public string PipelineId { get; set; }

        [Column(Name = "Branch", StringLength = 100)]
        public string? Branch { get; set; }

        [Column(Name = "CommitSha", StringLength = 50)]
        public string? CommitSha { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public PipelineStatus Status { get; set; }

        [Column(Name = "StartedAt")]
        public DateTime? StartedAt { get; set; }

        [Column(Name = "FinishedAt")]
        public DateTime? FinishedAt { get; set; }

        [Column(Name = "Duration")]
        public int? Duration { get; set; }

        [Column(Name = "WebUrl", StringLength = 500)]
        public string? WebUrl { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}

using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    [Table(Name = "sb_pm_git_repository")]
    public class GitRepository : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Column(Name = "Platform", MapType = typeof(string), StringLength = 20)]
        public GitPlatform Platform { get; set; }

        [Required, StringLength(500)]
        [Column(Name = "RepoUrl")]
        public string RepoUrl { get; set; }

        [Column(Name = "RepoFullName", StringLength = 200)]
        public string? RepoFullName { get; set; }

        [Column(Name = "RepoId", StringLength = 50)]
        public string? RepoId { get; set; }

        [Column(Name = "AccessToken", StringLength = -1)]
        public string? AccessToken { get; set; }

        [Column(Name = "WebhookSecret", StringLength = 200)]
        public string? WebhookSecret { get; set; }

        [Column(Name = "WebhookId", StringLength = 50)]
        public string? WebhookId { get; set; }

        [Column(Name = "DefaultBranch", StringLength = 100)]
        public string DefaultBranch { get; set; } = "main";

        [Column(Name = "LastSyncTime")]
        public DateTime? LastSyncTime { get; set; }

        [Column(Name = "SyncStatus", MapType = typeof(string), StringLength = 20)]
        public GitSyncStatus SyncStatus { get; set; } = GitSyncStatus.Idle;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

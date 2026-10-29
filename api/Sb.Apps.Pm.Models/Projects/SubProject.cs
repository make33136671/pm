using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_sub_project")]
    public class SubProject : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Column(Name = "Code", StringLength = 50)]
        public string? Code { get; set; }

        [Column(Name = "Description", StringLength = 500)]
        public string? Description { get; set; }

        /// <summary>
        /// 基座仓库地址（选择的已有仓库）
        /// </summary>
        [Column(Name = "BaseRepoUrl", StringLength = 500)]
        public string? BaseRepoUrl { get; set; }

        /// <summary>
        /// GitLab 项目 ID
        /// </summary>
        [Column(Name = "GitlabProjectId")]
        public int? GitlabProjectId { get; set; }

        /// <summary>
        /// GitLab 项目路径（如 sb-pm/pm-api）
        /// </summary>
        [Column(Name = "GitlabProjectPath", StringLength = 200)]
        public string? GitlabProjectPath { get; set; }

        /// <summary>
        /// 子项目仓库地址
        /// </summary>
        [Column(Name = "GitlabProjectUrl", StringLength = 500)]
        public string? GitlabProjectUrl { get; set; }

        [Column(Name = "DefaultBranch", StringLength = 100)]
        public string DefaultBranch { get; set; } = "main";

        /// <summary>
        /// 服务器上的工作目录路径（默认自动拼接，可手动覆盖）
        /// </summary>
        [Column(Name = "LocalPath", StringLength = 500)]
        public string? LocalPath { get; set; }

        /// <summary>
        /// 状态: Planning, Active, Testing, Deployed, Archived
        /// </summary>
        [Column(Name = "Status", StringLength = 20)]
        public string Status { get; set; } = "Planning";

        /// <summary>
        /// 进度百分比 0-100
        /// </summary>
        [Column(Name = "Progress")]
        public int Progress { get; set; } = 0;

        /// <summary>
        /// 最近部署环境
        /// </summary>
        [Column(Name = "DeployEnv", StringLength = 50)]
        public string? DeployEnv { get; set; }

        /// <summary>
        /// 最近部署时间
        /// </summary>
        [Column(Name = "LastDeployTime")]
        public DateTime? LastDeployTime { get; set; }

        /// <summary>
        /// 部署状态: None, Success, Failed, Deploying
        /// </summary>
        [Column(Name = "DeployStatus", StringLength = 20)]
        public string DeployStatus { get; set; } = "None";

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Pipelines
{
    /// <summary>
    /// 流水线配置预设 — 串联仓库、环境、服务器，实现"一键触发"
    /// </summary>
    [Table(Name = "sb_pm_pipeline_profile")]
    public class PipelineProfile : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project? Project { get; set; }

        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; } = "";

        /// <summary>
        /// 归属子项目(null = 项目级方案,向后兼容)。一键部署按子项目过滤方案。
        /// </summary>
        [Column(Name = "SubProjectId")]
        public int? SubProjectId { get; set; }
        [Navigate(nameof(SubProjectId))]
        public SubProject? SubProject { get; set; }

        [Column(Name = "GitRepositoryId")]
        public int GitRepositoryId { get; set; }
        [Navigate(nameof(GitRepositoryId))]
        public GitRepository? GitRepository { get; set; }

        [Column(Name = "DefaultBranch", StringLength = 100)]
        public string DefaultBranch { get; set; } = "main";

        [Column(Name = "EnvironmentId")]
        public int? EnvironmentId { get; set; }
        [Navigate(nameof(EnvironmentId))]
        public ProjectEnvironment? Environment { get; set; }

        [Column(Name = "VersionStrategy", MapType = typeof(string), StringLength = 20)]
        public VersionStrategy VersionStrategy { get; set; } = VersionStrategy.AutoIncrement;

        [Column(Name = "TriggerMode", MapType = typeof(string), StringLength = 20)]
        public TriggerMode TriggerMode { get; set; } = TriggerMode.Manual;

        /// <summary>
        /// 传给 GitLab Pipeline 的覆盖变量（JSON 键值对）
        /// </summary>
        [Column(Name = "PipelineVariables", MapType = typeof(string), StringLength = -1)]
        public Dictionary<string, string>? PipelineVariables { get; set; }

        [Column(Name = "DeployMode", MapType = typeof(string), StringLength = 20)]
        public DeployMode DeployMode { get; set; } = DeployMode.None;

        /// <summary>
        /// 部署产物来源:GitLabCi(从 GitLab 下产物) / LocalBuild(本机构建) / LastArtifact(复用上次)
        /// </summary>
        [Column(Name = "BuildSource", MapType = typeof(string), StringLength = 20)]
        public BuildSource BuildSource { get; set; } = BuildSource.GitLabCi;

        /// <summary>
        /// 部署策略(怎么切换上线,与部署通道 DeployMethod 正交)
        /// </summary>
        [Column(Name = "DeployStrategy", MapType = typeof(string), StringLength = 20)]
        public DeployStrategy DeployStrategy { get; set; } = DeployStrategy.Replace;

        /// <summary>
        /// 是否需要人工审批后才部署(false = 一键直发)
        /// </summary>
        [Column(Name = "RequireApproval")]
        public bool RequireApproval { get; set; } = false;

        /// <summary>
        /// LocalBuild 时在工作目录执行的构建命令(如 dotnet publish ... / pnpm build)。
        /// 工作目录优先取 SubProject.LocalPath,其次 Project.LocalPath。
        /// </summary>
        [Column(Name = "BuildCommand", StringLength = -1)]
        public string? BuildCommand { get; set; }

        /// <summary>
        /// LocalBuild 时构建产物目录(相对工作目录),整目录打包为 zip 部署。如 bin/Release/publish
        /// </summary>
        [Column(Name = "ArtifactDir", StringLength = 500)]
        public string? ArtifactDir { get; set; }

        /// <summary>
        /// GitLabDeploy 时 deploy Pipeline 的触发 ref
        /// </summary>
        [Column(Name = "DeployPipelineRef", StringLength = 200)]
        public string? DeployPipelineRef { get; set; }

        /// <summary>
        /// 传给 deploy Pipeline 的额外变量（JSON 键值对）
        /// </summary>
        [Column(Name = "DeployPipelineVars", MapType = typeof(string), StringLength = -1)]
        public Dictionary<string, string>? DeployPipelineVars { get; set; }

        /// <summary>
        /// SbpmDeploy 时的目标服务器 ID 列表（JSON 数组）
        /// </summary>
        [Column(Name = "DeployServerIds", MapType = typeof(string), StringLength = -1)]
        public List<int>? DeployServerIds { get; set; }

        /// <summary>
        /// 构建成功后自动提交部署审批（不跳过审批）
        /// </summary>
        [Column(Name = "AutoDeployOnSuccess")]
        public bool AutoDeployOnSuccess { get; set; } = false;

        /// <summary>
        /// GitLab CI 中产出 Artifact 的 Job 名称
        /// </summary>
        [Column(Name = "ArtifactJobName", StringLength = 100)]
        public string? ArtifactJobName { get; set; }

        /// <summary>
        /// 构建超时（分钟）
        /// </summary>
        [Column(Name = "BuildTimeout")]
        public int BuildTimeout { get; set; } = 30;

        [Column(Name = "NotifyOnSuccess")]
        public bool NotifyOnSuccess { get; set; } = true;

        [Column(Name = "NotifyOnFailure")]
        public bool NotifyOnFailure { get; set; } = true;

        [Column(Name = "SortOrder")]
        public int SortOrder { get; set; } = 0;

        [Column(Name = "IsEnabled")]
        public bool IsEnabled { get; set; } = true;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime")]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

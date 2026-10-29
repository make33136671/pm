using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Servers;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_server")]
    public class ProjectServer : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        // ---- 关联 ----

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Column(Name = "SubProjectId")]
        public int? SubProjectId { get; set; }
        [Navigate(nameof(SubProjectId))]
        public SubProject? SubProject { get; set; }

        [Column(Name = "ServerId")]
        public int ServerId { get; set; }
        [Navigate(nameof(ServerId))]
        public Server Server { get; set; }

        [Column(Name = "EnvironmentId")]
        public int? EnvironmentId { get; set; }
        [Navigate(nameof(EnvironmentId))]
        public ProjectEnvironment? Environment { get; set; }

        // ---- 标签 ----

        [Column(Name = "Label", StringLength = 100)]
        public string? Label { get; set; }

        [Column(Name = "IsDefault", IsNullable = false, InsertValueSql = "false")]
        public bool IsDefault { get; set; }

        // ---- 部署方式 ----

        [Column(Name = "PreferredDeployMethod", MapType = typeof(string), StringLength = 20)]
        public DeployMethod? PreferredDeployMethod { get; set; }

        [Column(Name = "AvailableDeployMethods", StringLength = 200)]
        public string? AvailableDeployMethods { get; set; }

        [Column(Name = "BuiltInApiUrl", StringLength = 500)]
        public string? BuiltInApiUrl { get; set; }

        [Column(Name = "BuiltInApiKey", StringLength = 500)]
        public string? BuiltInApiKey { get; set; }

        // ---- 部署目录 ----

        [Column(Name = "ApiDeployPath", StringLength = 500)]
        public string? ApiDeployPath { get; set; }

        [Column(Name = "FrontendDeployPath", StringLength = 500)]
        public string? FrontendDeployPath { get; set; }

        [Column(Name = "FtpDeployPath", StringLength = 500)]
        public string? FtpDeployPath { get; set; }

        // ---- 数据库 ----

        [Column(Name = "DatabaseName", StringLength = 100)]
        public string? DatabaseName { get; set; }

        // ---- 端口（项目级） ----

        [Column(Name = "ApiPort")]
        public int? ApiPort { get; set; }

        [Column(Name = "FrontendPort")]
        public int? FrontendPort { get; set; }

        [Column(Name = "ExtraPorts", StringLength = 500)]
        public string? ExtraPorts { get; set; }

        // ---- 服务 ----

        [Column(Name = "SystemdServiceName", StringLength = 100)]
        public string? SystemdServiceName { get; set; }

        [Column(Name = "BtSiteName", StringLength = 200)]
        public string? BtSiteName { get; set; }

        // ---- 备注 ----

        [Column(Name = "Remark", StringLength = 500)]
        public string? Remark { get; set; }

        [Column(Name = "Sort")]
        public int Sort { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

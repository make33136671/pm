using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project")]
    public class Project : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Column(Name = "Code", StringLength = 50)]
        public string? Code { get; set; }

        [Column(Name = "Description", StringLength = -1)]
        public string? Description { get; set; }

        /// <summary>
        /// 项目 Logo 的访问 URL（相对路径，如 /storage/uploads/project-logos/xxx.png）
        /// </summary>
        [Column(Name = "LogoUrl", StringLength = 500)]
        public string? LogoUrl { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ProjectStatus Status { get; set; } = ProjectStatus.Planning;

        [Column(Name = "Tags", StringLength = 500)]
        public string? Tags { get; set; }

        [Column(Name = "PortRangeStart")]
        public int? PortRangeStart { get; set; }

        [Column(Name = "PortRangeEnd")]
        public int? PortRangeEnd { get; set; }

        [Column(Name = "GitlabGroupId")]
        public int? GitlabGroupId { get; set; }

        [Column(Name = "GitlabGroupPath", StringLength = 200)]
        public string? GitlabGroupPath { get; set; }

        [Column(Name = "GitlabGroupUrl", StringLength = 500)]
        public string? GitlabGroupUrl { get; set; }

        /// <summary>
        /// 项目在本机的绝对路径（手动设置时优先于 workspace_root 自动拼接）
        /// </summary>
        [Column(Name = "LocalPath", StringLength = 500)]
        public string? LocalPath { get; set; }

        [Column(Name = "TemplateId")]
        public int? TemplateId { get; set; }

        [Column(Name = "StartDate")]
        public DateTime? StartDate { get; set; }

        [Column(Name = "DueDate")]
        public DateTime? DueDate { get; set; }

        [Column(Name = "CompletedDate")]
        public DateTime? CompletedDate { get; set; }

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

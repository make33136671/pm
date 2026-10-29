using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Models.ApiEndpoints
{
    /// <summary>
    /// 接口（API）记录 — 每个项目的接口清单。
    /// Side 区分后端提供(Api) / admin 调用 / h5 调用。
    /// 通过 ApiEndpointFeatureRel 与功能模块多对多关联。
    /// </summary>
    [Table(Name = "sb_pm_api_endpoint")]
    public class ApiEndpoint : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        /// <summary>接口名称/用途</summary>
        [Required, StringLength(200)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        /// <summary>接口路径，如 /admin/TaskItem/List</summary>
        [Required, StringLength(500)]
        [Column(Name = "Path")]
        public string Path { get; set; }

        /// <summary>HTTP 方法</summary>
        [Column(Name = "Method", MapType = typeof(string), StringLength = 10)]
        public ApiMethod Method { get; set; } = ApiMethod.GET;

        /// <summary>归属端：Api/Admin/H5</summary>
        [Column(Name = "Side", MapType = typeof(string), StringLength = 10)]
        public ApiSide Side { get; set; } = ApiSide.Api;

        /// <summary>实现状态</summary>
        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public ApiEndpointStatus Status { get; set; } = ApiEndpointStatus.Planned;

        /// <summary>调用方式/请求参数/返回说明（Markdown）</summary>
        [Column(Name = "Description", StringLength = -1)]
        public string? Description { get; set; }

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

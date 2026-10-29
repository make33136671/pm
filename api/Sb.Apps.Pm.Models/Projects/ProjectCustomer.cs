using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Projects
{
    [Table(Name = "sb_pm_project_customer")]
    public class ProjectCustomer : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        [Column(Name = "ProductName", StringLength = 200)]
        public string? ProductName { get; set; }

        [Column(Name = "Contact", StringLength = 200)]
        public string? Contact { get; set; }

        [Column(Name = "BranchName", StringLength = 100)]
        public string? BranchName { get; set; }

        [Column(Name = "ProdServer", StringLength = 500)]
        public string? ProdServer { get; set; }

        [Column(Name = "TestServer", StringLength = 500)]
        public string? TestServer { get; set; }

        [Column(Name = "ExpireDate")]
        public DateTime? ExpireDate { get; set; }

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public CustomerStatus Status { get; set; } = CustomerStatus.Active;

        [Column(Name = "Remark", StringLength = 500)]
        public string? Remark { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

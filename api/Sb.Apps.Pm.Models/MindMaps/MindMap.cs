using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.MindMaps
{
    [Table(Name = "sb_pm_mind_map")]
    public class MindMap : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "ProjectId")]
        public int ProjectId { get; set; }
        [Navigate(nameof(ProjectId))]
        public Project Project { get; set; }

        [Required, StringLength(200)]
        [Column(Name = "Title")]
        public string Title { get; set; }

        [Column(Name = "Type", MapType = typeof(string), StringLength = 20)]
        public MindMapType Type { get; set; }

        [Required]
        [Column(Name = "MapData", StringLength = -1)]
        public string MapData { get; set; }

        [Column(Name = "IsMain", IsNullable = false, InsertValueSql = "false")]
        public bool IsMain { get; set; }

        [Column(Name = "GeneratedBy", MapType = typeof(string), StringLength = 20)]
        public GeneratedBy GeneratedBy { get; set; } = GeneratedBy.Manual;

        [Column(Name = "SourceSnapshot", StringLength = -1)]
        public string? SourceSnapshot { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

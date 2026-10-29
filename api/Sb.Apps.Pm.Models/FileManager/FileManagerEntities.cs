using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Models.FileManager
{
    /// <summary>
    /// 回收站记录。删除文件时把条目移入回收站目录并写一条记录，
    /// 恢复 / 永久删除都靠它定位。
    ///
    /// 为什么记数据库而不是只靠文件名：
    ///   1. 原始路径可能很长，塞进文件名会超长；
    ///   2. 需要记录删除人、删除时间（面板要展示，审计也要）；
    ///   3. 同名文件多次删除要能区分（用 Id 做回收站内文件名）。
    /// </summary>
    [Table(Name = "sb_pm_file_recycle")]
    public class FileRecycleItem : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>原始绝对路径（恢复目标）。</summary>
        [Column(Name = "OriginalPath", StringLength = 1000, IsNullable = false)]
        public string OriginalPath { get; set; } = string.Empty;

        /// <summary>回收站内的存储名（= Id + 原扩展名，便于人工识别）。</summary>
        [Column(Name = "StorageName", StringLength = 200, IsNullable = false)]
        public string StorageName { get; set; } = string.Empty;

        /// <summary>原始文件名。</summary>
        [Column(Name = "Name", StringLength = 500, IsNullable = false)]
        public string Name { get; set; } = string.Empty;

        /// <summary>是否目录。</summary>
        [Column(Name = "IsDirectory")]
        public bool IsDirectory { get; set; }

        /// <summary>删除时的大小（目录为整树统计值）。</summary>
        [Column(Name = "Size")]
        public long Size { get; set; }

        /// <summary>删除时间（UTC）。</summary>
        [Column(Name = "DeletedAt")]
        public DateTime DeletedAt { get; set; }

        /// <summary>删除操作人。</summary>
        [Column(Name = "DeletedBy", StringLength = 100)]
        public string DeletedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 文件备注 / 收藏。按路径唯一（物理路径变了备注就失效，这是有意的——
    /// 跟着路径走比跟着 inode 走更符合用户直觉）。
    /// </summary>
    [Table(Name = "sb_pm_file_note")]
    public class FileNote : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        // 唯一性由应用层保证（删了再插），不建唯一索引：
        // MySQL 的 unique + 1000 长度 varchar 会超索引字节上限
        [Column(Name = "Path", StringLength = 1000, IsNullable = false)]
        public string Path { get; set; } = string.Empty;

        /// <summary>备注文本。</summary>
        [Column(Name = "Note", DbType = "varchar(1000)", IsNullable = true)]
        public string? Note { get; set; }

        /// <summary>是否收藏。</summary>
        [Column(Name = "Favorite")]
        public bool Favorite { get; set; }

        [Column(Name = "CreatedBy", StringLength = 100)]
        public string CreatedBy { get; set; } = string.Empty;

        [Column(Name = "UpdatedTime")]
        public DateTime UpdatedTime { get; set; }
    }
}

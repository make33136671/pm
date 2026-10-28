namespace SB.Feature.FileManager.Models
{
    /// <summary>单个目录条目（文件或子目录）。</summary>
    public sealed class FileEntryDto
    {
        /// <summary>文件名（不含父路径）。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>完整绝对路径。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>true = 目录。</summary>
        public bool IsDirectory { get; set; }

        /// <summary>true = 符号链接（可能是链接到目录的软链）。</summary>
        public bool IsSymlink { get; set; }

        /// <summary>字节数，目录为 0。</summary>
        public long Size { get; set; }

        /// <summary>最后修改时间（UTC，ISO8601 带 Z）。</summary>
        public string LastModified { get; set; } = string.Empty;

        /// <summary>最后访问时间（UTC）。</summary>
        public string LastAccessed { get; set; } = string.Empty;

        /// <summary>八进制权限串，如 "0644"；无权限读取时为 "-"。</summary>
        public string Mode { get; set; } = "-";

        /// <summary>属主用户（数字 uid 或用户名，取决于解析）。</summary>
        public string Owner { get; set; } = "-";

        /// <summary>属组。</summary>
        public string Group { get; set; } = "-";

        /// <summary>是否可在线编辑（文本 + 大小未超限）。</summary>
        public bool Editable { get; set; }

        /// <summary>扩展名（小写，不含点）。</summary>
        public string Extension { get; set; } = string.Empty;
    }

    /// <summary>目录列举结果。</summary>
    public sealed class DirectoryListingDto
    {
        /// <summary>当前目录绝对路径。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>父目录绝对路径；已在根 / 或无权限时为空串。</summary>
        public string ParentPath { get; set; } = string.Empty;

        /// <summary>面包屑（从 / 到当前目录）。</summary>
        public List<BreadcrumbDto> Breadcrumbs { get; set; } = [];

        /// <summary>子目录（排在前面）。</summary>
        public List<FileEntryDto> Directories { get; set; } = [];

        /// <summary>文件。</summary>
        public List<FileEntryDto> Files { get; set; } = [];

        /// <summary>是否因 MaxListEntries 截断。</summary>
        public bool Truncated { get; set; }

        /// <summary>条目总数（含被截断的）。</summary>
        public int Total { get; set; }

        /// <summary>当前目录自身信息。</summary>
        public FileEntryDto? Self { get; set; }
    }

    /// <summary>面包屑节点。</summary>
    public sealed class BreadcrumbDto
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    /// <summary>文件内容（编辑用）。</summary>
    public sealed class FileContentDto
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>文本内容（UTF-8）。二进制或超限时不返回内容。</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>文件大小（字节）。</summary>
        public long Size { get; set; }

        /// <summary>true = 判定为二进制，不可在线编辑。</summary>
        public bool IsBinary { get; set; }

        /// <summary>true = 超出 MaxEditFileSizeMB，不可在线编辑。</summary>
        public bool TooLarge { get; set; }

        /// <summary>是否可编辑（= !IsBinary && !TooLarge && 无只读属性）。</summary>
        public bool Editable { get; set; }

        /// <summary>检测到的换行符：LF / CRLF / MIXED / NONE。</summary>
        public string LineEnding { get; set; } = "LF";

        /// <summary>行数。</summary>
        public int LineCount { get; set; }

        /// <summary>最后修改时间（UTC）。</summary>
        public string LastModified { get; set; } = string.Empty;

        /// <summary>权限位。</summary>
        public string Mode { get; set; } = "-";

        /// <summary>
        /// 保存时的并发保护：内容哈希。保存请求需回传它，
        /// 若磁盘文件在此期间被他人改动则拒绝覆盖（409 语义，用 Fail 返回）。
        /// </summary>
        public string Hash { get; set; } = string.Empty;
    }

    /// <summary>文件操作（写 / 新建 / 重命名 / 删除 / chmod）统一返回。</summary>
    public sealed class FileOperationResultDto
    {
        public string Path { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>磁盘使用情况。</summary>
    public sealed class DiskUsageDto
    {
        public string Path { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public long UsedBytes { get; set; }
        public double UsedPercent { get; set; }
    }
}

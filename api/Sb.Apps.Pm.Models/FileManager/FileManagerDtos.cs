namespace Sb.Apps.Pm.Models.FileManager
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

    /// <summary>回收站条目。</summary>
    public sealed class RecycleBinItemDto
    {
        /// <summary>回收站内的唯一名（用于恢复 / 永久删除）。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>原始完整路径（恢复目标）。</summary>
        public string OriginalPath { get; set; } = string.Empty;

        /// <summary>原始文件名。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>删除时的大小（字节）。</summary>
        public long Size { get; set; }

        /// <summary>true = 目录。</summary>
        public bool IsDirectory { get; set; }

        /// <summary>删除时间（UTC）。</summary>
        public string DeletedAt { get; set; } = string.Empty;

        /// <summary>删除操作人。</summary>
        public string DeletedBy { get; set; } = string.Empty;
    }

    /// <summary>回收站列表。</summary>
    public sealed class RecycleBinListDto
    {
        public List<RecycleBinItemDto> Items { get; set; } = [];
        public long TotalSize { get; set; }
        public int Count { get; set; }
        /// <summary>回收站总容量上限（MB），0 = 不限。</summary>
        public int QuotaMb { get; set; }
    }

    /// <summary>压缩请求。</summary>
    public sealed class CompressRequestDto
    {
        /// <summary>要压缩的源路径（可多个）。</summary>
        public List<string> Paths { get; set; } = [];

        /// <summary>输出目录（默认为第一个源文件的父目录）。</summary>
        public string? OutputDir { get; set; }

        /// <summary>输出文件名（不含扩展名）；不传则用第一个源文件名或 "archive"。</summary>
        public string? Name { get; set; }

        /// <summary>格式：zip / tar / tar.gz / tgz / gz / tar.bz2 / tar.xz / 7z。</summary>
        public string Format { get; set; } = "zip";

        /// <summary>压缩等级 0-9（仅 zip / 7z 生效，0 = 存储不压缩）。</summary>
        public int Level { get; set; } = 6;

        /// <summary>zip 时是否加密（7z 命令仅）；true 时必须给 Password。</summary>
        public bool Encrypt { get; set; }

        /// <summary>加密口令。</summary>
        public string? Password { get; set; }
    }

    /// <summary>解压请求。</summary>
    public sealed class ExtractRequestDto
    {
        /// <summary>压缩包路径。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>解压目标目录；不传则解到压缩包所在目录的新建同名目录。</summary>
        public string? OutputDir { get; set; }

        /// <summary>有密码的压缩包需要提供。</summary>
        public string? Password { get; set; }

        /// <summary>是否覆盖同名文件。</summary>
        public bool Overwrite { get; set; }

        /// <summary>zip 内非 UTF-8 文件名（Windows 打的包）时用 GBK 解码。</summary>
        public bool UseGbkNames { get; set; }
    }

    /// <summary>压缩包条目预览。</summary>
    public sealed class ArchiveEntryDto
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public long CompressedSize { get; set; }
        public bool IsDirectory { get; set; }
        public string LastModified { get; set; } = string.Empty;
        public bool Encrypted { get; set; }
    }

    /// <summary>压缩包预览结果。</summary>
    public sealed class ArchivePreviewDto
    {
        public string Path { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int TotalEntries { get; set; }
        public long TotalSize { get; set; }
        /// <summary>超过 MaxArchivePreviewEntries 时只返回前 N 条。</summary>
        public bool Truncated { get; set; }
        public List<ArchiveEntryDto> Entries { get; set; } = [];
    }

    /// <summary>批量操作单项结果。</summary>
    public sealed class BatchItemResultDto
    {
        public string Path { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>批量操作汇总。</summary>
    public sealed class BatchResultDto
    {
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public List<BatchItemResultDto> Items { get; set; } = [];
    }

    /// <summary>路径大小统计结果。</summary>
    public sealed class PathSizeDto
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public long FileCount { get; set; }
        public long DirCount { get; set; }
        /// <summary>是否因条目太多而提前中止（此时 Size 是下限）。</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>全局搜索请求。</summary>
    public sealed class SearchRequestDto
    {
        /// <summary>搜索起点目录。</summary>
        public string Path { get; set; } = "/";

        /// <summary>文件名关键词（子串匹配，不区分大小写）；支持通配符 * 和 ?。</summary>
        public string Pattern { get; set; } = "*";

        /// <summary>是否递归子目录。</summary>
        public bool Recursive { get; set; } = true;

        /// <summary>内容关键词（可选，会读取文本文件匹配）。</summary>
        public string? ContentContains { get; set; }

        /// <summary>最大结果数，防止全盘搜索撑爆内存。</summary>
        public int MaxResults { get; set; } = 500;

        /// <summary>扫描条目上限（含目录），超出即停。</summary>
        public int MaxScan { get; set; } = 200_000;
    }

    /// <summary>搜索响应。</summary>
    public sealed class SearchResultDto
    {
        public List<FileEntryDto> Items { get; set; } = [];
        /// <summary>是否因上限提前终止。</summary>
        public bool Truncated { get; set; }
        /// <summary>实际扫描的条目数。</summary>
        public long Scanned { get; set; }
        /// <summary>耗时（毫秒）。</summary>
        public long ElapsedMs { get; set; }
    }

    /// <summary>文件备注（存在数据库，按路径索引）。</summary>
    public sealed class FileNoteDto
    {
        public string Path { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public bool Favorite { get; set; }
        public string UpdatedTime { get; set; } = string.Empty;
    }

    /// <summary>系统用户（chown 用）。</summary>
    public sealed class SystemUserDto
    {
        public string Name { get; set; } = string.Empty;
        public int Uid { get; set; }
        public int Gid { get; set; }
    }
}

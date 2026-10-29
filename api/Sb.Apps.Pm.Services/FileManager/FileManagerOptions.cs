namespace Sb.Apps.Pm.Services.FileManager
{
    /// <summary>
    /// 文件管理器配置（appsettings 的 "FileManager" 节）。
    ///
    /// 与 webterm 的 "WebTerminal" 节并列 —— PM 自己读配置，不走框架的 Features 机制，
    /// 因为这些是 PM 业务参数，不是框架插件开关。
    ///
    /// 权限模型与网页终端**对等**：webterm 能执行任意命令（相当于 root），
    /// 所以文件管理器默认同样全盘开放，不额外做业务级权限细分。
    /// </summary>
    public sealed class FileManagerOptions
    {
        /// <summary>配置节名。</summary>
        public const string SectionName = "FileManager";

        /// <summary>
        /// 可访问的根目录白名单。为空 = 整个文件系统（与 webterm 对等，默认值）。
        /// 填了则任何越界路径（含 ../ 穿越、符号链接逃逸）都会被拒绝。
        /// </summary>
        public string[] AllowedRoots { get; set; } = [];

        /// <summary>一律拒绝的路径前缀，优先级高于白名单（伪文件系统，读了没意义且可能卡死）。</summary>
        public string[] DeniedPaths { get; set; } = ["/proc", "/sys", "/dev", "/run"];

        /// <summary>在线编辑器允许打开的最大文件（MB）。超出只允许下载。</summary>
        public int MaxEditFileSizeMB { get; set; } = 5;

        /// <summary>单次上传文件大小上限（MB）。</summary>
        public int MaxUploadSizeMB { get; set; } = 512;

        /// <summary>目录列表单页上限，防止 /usr/lib 这类大目录卡死浏览器。</summary>
        public int MaxListEntries { get; set; } = 5000;

        /// <summary>二进制嗅探长度（字节）。</summary>
        public int BinarySniffBytes { get; set; } = 8192;

        /// <summary>是否允许 chmod。</summary>
        public bool AllowChmod { get; set; } = true;

        /// <summary>是否把写操作（写文件 / 新建 / 重命名 / 删除 / chmod / 上传）记入审计日志。</summary>
        public bool AuditLogEnabled { get; set; } = true;

        /// <summary>审计日志路径（相对 ContentRootPath）。</summary>
        public string AuditLogPath { get; set; } = "logs/file-manager/audit.log";

        /// <summary>列表是否隐藏点文件（.git / .env 等）。</summary>
        public bool HideDotFiles { get; set; }

        /// <summary>新建文件 / 目录是否归一为 644 / 755，避免 umask 产生不可读文件。</summary>
        public bool NormalizePermissions { get; set; } = true;

        // ════════════════ 回收站 ════════════════

        /// <summary>
        /// 是否启用回收站。开启后删除 = 移到回收站目录，可恢复。
        /// 关掉则删除立即不可逆——默认开启，防误删。
        /// </summary>
        public bool RecycleBinEnabled { get; set; } = true;

        /// <summary>
        /// 回收站存放目录（相对 ContentRootPath，或绝对路径）。
        /// 必须与业务数据在同一块盘上，否则 move 会退化为跨盘复制（大文件很慢）。
        /// </summary>
        public string RecycleBinPath { get; set; } = "files-recycle";

        /// <summary>回收站总容量上限（MB），0 = 不限。超限时清理最旧的条目。</summary>
        public int RecycleBinQuotaMB { get; set; } = 2048;

        /// <summary>回收站保留天数，0 = 不自动清理。</summary>
        public int RecycleBinRetentionDays { get; set; } = 30;

        // ════════════════ 压缩 / 解压 ════════════════

        /// <summary>单个压缩包解压后的体积上限（MB），防 zip 炸弹。0 = 不限制。</summary>
        public int MaxExtractSizeMB { get; set; } = 8192;

        /// <summary>解压时允许的最大条目数，防条目炸弹。</summary>
        public int MaxExtractEntries { get; set; } = 200_000;

        /// <summary>压缩包预览最多返回条目数。</summary>
        public int MaxArchivePreviewEntries { get; set; } = 2000;

        /// <summary>外部命令超时（秒）。xz / 7z / unrar 走子进程，必须有超时。</summary>
        public int ExternalCommandTimeoutSeconds { get; set; } = 1800;

        // ════════════════ 搜索 / 批量 ════════════════

        /// <summary>全局搜索默认最大结果数。</summary>
        public int MaxSearchResults { get; set; } = 500;

        /// <summary>全局搜索默认最多扫描条目数。</summary>
        public int MaxSearchScan { get; set; } = 200_000;

        /// <summary>批量操作单次最多处理条目数。</summary>
        public int MaxBatchItems { get; set; } = 200;

        /// <summary>是否允许 chown（改属主）。需要进程有 CAP_CHOWN（通常是 root）。</summary>
        public bool AllowChown { get; set; } = true;
    }
}

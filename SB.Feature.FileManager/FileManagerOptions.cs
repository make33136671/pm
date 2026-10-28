namespace SB.Feature.FileManager
{
    /// <summary>
    /// 文件管理器强类型配置。框架自动绑定到 appsettings 的 "Features:FileManager" 节。
    /// </summary>
    public sealed class FileManagerOptions
    {
        /// <summary>
        /// 可访问的根目录白名单。为空 = 整个文件系统（与 web shell 对等）。
        ///
        /// 生产环境建议显式收敛，例如 ["/opt/app", "/var/log", "/root/deploy"]。
        /// 一旦配置，任何越界路径（含 ../ 穿越、符号链接逃逸）都会被 403 拒绝。
        /// </summary>
        public string[] AllowedRoots { get; set; } = [];

        /// <summary>
        /// 完全禁止访问的路径前缀（优先级高于 AllowedRoots）。
        /// 用于即使开放根目录也要挡住的敏感目录。
        /// </summary>
        public string[] DeniedPaths { get; set; } = ["/proc", "/sys", "/dev"];

        /// <summary>在线编辑器允许打开的最大文件（MB）。超出只允许下载，不允许编辑。</summary>
        public int MaxEditFileSizeMB { get; set; } = 5;

        /// <summary>单次上传文件大小上限（MB）。</summary>
        public int MaxUploadSizeMB { get; set; } = 512;

        /// <summary>目录列表单页上限，防止 /usr/lib 之类几十万文件的目录把浏览器卡死。</summary>
        public int MaxListEntries { get; set; } = 5000;

        /// <summary>二进制文件嗅探长度（字节），超过该长度的部分不参与 NUL 判定。</summary>
        public int BinarySniffBytes { get; set; } = 8192;

        /// <summary>是否允许 chmod。关闭后只读展示权限位。</summary>
        public bool AllowChmod { get; set; } = true;

        /// <summary>
        /// 是否把每次写操作（写文件 / 新建 / 重命名 / 删除 / chmod / 上传）落到
        /// logs/file-manager-audit.log。审计日志只追加，不随请求可删。
        /// </summary>
        public bool AuditLogEnabled { get; set; } = true;

        /// <summary>审计日志相对 ContentRootPath 的路径。</summary>
        public string AuditLogPath { get; set; } = "logs/file-manager-audit.log";

        /// <summary>列表隐藏点文件（.git / .env 等）。安全默认开，需要时显式关。</summary>
        public bool HideDotFiles { get; set; }

        /// <summary>
        /// 新建 / 重命名 / 上传的文件是否继承父目录的属主与权限。
        /// 打开后新文件默认 644、目录 755，避免因 umask 产生不可读文件。
        /// </summary>
        public bool NormalizePermissions { get; set; } = true;
    }
}

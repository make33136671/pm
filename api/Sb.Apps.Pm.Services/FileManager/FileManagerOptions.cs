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
    }
}

using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.WebTerminals
{
    [Table(Name = "sb_pm_webterm_config")]
    public class CliConfig : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        // ---- 进程身份与启动 ----

        [Column(Name = "RunAsUser", StringLength = 50)]
        public string? RunAsUser { get; set; }

        [Column(Name = "DefaultShell", StringLength = 200)]
        public string DefaultShell { get; set; } = "/bin/bash";

        [Column(Name = "DefaultCwd", StringLength = 500)]
        public string? DefaultCwd { get; set; }

        // 自定义环境变量 JSON: [{"Key":"x","Value":"y","Encrypted":false},...]
        [Column(Name = "DefaultEnvJson", StringLength = -1)]
        public string? DefaultEnvJson { get; set; }

        // ---- Git 凭据 ----

        // SSH 私钥(已加密存储,启动会话时解密注入临时文件 + GIT_SSH_COMMAND)
        [Column(Name = "SshPrivateKey", StringLength = -1)]
        public string? SshPrivateKey { get; set; }

        [Column(Name = "SshPrivateKeyPassphrase", StringLength = 500)]
        public string? SshPrivateKeyPassphrase { get; set; }

        // ---- 缓冲区 / 清理策略 ----

        [Column(Name = "ScrollbackBytes")]
        public int ScrollbackBytes { get; set; } = 4 * 1024 * 1024;

        [Column(Name = "AutoCleanDays")]
        public int AutoCleanDays { get; set; } = 30;

        [Column(Name = "TotalCapMb")]
        public int TotalCapMb { get; set; } = 5120;

        [Column(Name = "IdleSessionDays")]
        public int IdleSessionDays { get; set; } = 30;

        // ---- 网页终端外观(xterm.js 视觉) ----

        [Column(Name = "FontFamily", StringLength = 200)]
        public string FontFamily { get; set; } = "Menlo, Consolas, \"Liberation Mono\", monospace";

        [Column(Name = "FontSize")]
        public int FontSize { get; set; } = 14;

        [Column(Name = "LineHeight")]
        public decimal LineHeight { get; set; } = 1.2m;

        [Column(Name = "LetterSpacing")]
        public decimal LetterSpacing { get; set; } = 0m;

        // 预设主题名: dark / light / solarized-dark / solarized-light / monokai / custom
        [Column(Name = "Theme", StringLength = 30)]
        public string Theme { get; set; } = "dark";

        // theme=custom 时用,否则忽略;十六进制 #rrggbb
        [Column(Name = "CustomBgColor", StringLength = 20)]
        public string? CustomBgColor { get; set; }

        [Column(Name = "CustomFgColor", StringLength = 20)]
        public string? CustomFgColor { get; set; }

        [Column(Name = "CustomCursorColor", StringLength = 20)]
        public string? CustomCursorColor { get; set; }

        // block / underline / bar
        [Column(Name = "CursorStyle", StringLength = 20)]
        public string CursorStyle { get; set; } = "block";

        [Column(Name = "CursorBlink")]
        public bool CursorBlink { get; set; } = true;

        // xterm 客户端 scrollback 行数(跟服务端 ScrollbackBytes 不同 — 那个是 PTY 缓冲)
        [Column(Name = "XtermScrollback")]
        public int XtermScrollback { get; set; } = 5000;

        // ---- 标准字段 ----

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}

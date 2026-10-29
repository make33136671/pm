using FreeSql;
using Microsoft.Extensions.Logging;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.WebTerminals;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Services;
using SB.Framework.Services.Startup;

namespace Sb.Apps.Pm.Services.WebTerminals;

/// <summary>
/// 网页 CLI 全局配置(单行)。
/// 也是 webterm 模块的 IModuleInitializer 入口,负责注册 entity 类型 + 注册菜单。
/// </summary>
[AutoDependencyInjection]
public class CliConfigService : BaseCrudService<CliConfig>, IModuleInitializer
{
    #region IModuleInitializer

    public string ModuleName => "网页终端";
    public int InitOrder => 250;
    public Type[] GetEntityTypes() => new[] { typeof(CliConfig), typeof(CliSession) };
    public List<MenuDefinition> GetMenus() => new()
    {
        new MenuDefinition
        {
            Name = "WebTerminal",
            Path = "/webterm",
            Component = "/webterm/index",
            Title = "网页终端",
            Icon = "lucide:terminal-square",
            OrderNo = 95,
        },
        new MenuDefinition
        {
            ParentPath = "/system",
            Name = "WebTerminalConfig",
            Path = "/webterm-config",
            Component = "/webterm/config",
            Title = "终端配置",
            Icon = "lucide:settings",
            OrderNo = 96,
        },
    };

    #endregion

    public CliConfigService(IFreeSql fsql, ILogger<CliConfigService> logger) : base(fsql, logger) { }

    /// <summary>取唯一配置行(没有就插一条默认值)。</summary>
    public CliConfig GetOrCreate()
    {
        var cfg = Select.OrderBy(c => c.Id).First();
        if (cfg != null) return cfg;

        cfg = new CliConfig();
        cfg.CreatedTime = DateTime.Now;
        return Insert(cfg);
    }

    public Result Get()
    {
        var cfg = GetOrCreate();
        // 输出时把敏感字段抹掉(只返回是否存在),前端编辑保存后端再 PreserveSensitive
        return Result.Success(MaskSensitive(cfg));
    }

    public Result Edit(CliConfig input)
    {
        if (input == null) return Result.Fail("参数不能为空");
        var existing = GetOrCreate();

        // 把空白(代表"未修改")的敏感字段,从 existing 还原回去;非空的加密
        existing.RunAsUser = input.RunAsUser;
        existing.DefaultShell = string.IsNullOrWhiteSpace(input.DefaultShell) ? "/bin/bash" : input.DefaultShell;
        existing.DefaultCwd = input.DefaultCwd;
        existing.DefaultEnvJson = input.DefaultEnvJson;
        existing.SshPrivateKey = PreserveOrEncrypt(input.SshPrivateKey, existing.SshPrivateKey);
        existing.SshPrivateKeyPassphrase = PreserveOrEncrypt(input.SshPrivateKeyPassphrase, existing.SshPrivateKeyPassphrase);

        if (input.ScrollbackBytes > 0) existing.ScrollbackBytes = input.ScrollbackBytes;
        if (input.AutoCleanDays > 0) existing.AutoCleanDays = input.AutoCleanDays;
        if (input.TotalCapMb > 0) existing.TotalCapMb = input.TotalCapMb;
        if (input.IdleSessionDays > 0) existing.IdleSessionDays = input.IdleSessionDays;

        // 网页终端外观
        if (!string.IsNullOrWhiteSpace(input.FontFamily)) existing.FontFamily = input.FontFamily;
        if (input.FontSize > 0) existing.FontSize = input.FontSize;
        if (input.LineHeight > 0) existing.LineHeight = input.LineHeight;
        if (input.LetterSpacing >= 0) existing.LetterSpacing = input.LetterSpacing;
        if (!string.IsNullOrWhiteSpace(input.Theme)) existing.Theme = input.Theme;
        existing.CustomBgColor = input.CustomBgColor;
        existing.CustomFgColor = input.CustomFgColor;
        existing.CustomCursorColor = input.CustomCursorColor;
        if (!string.IsNullOrWhiteSpace(input.CursorStyle)) existing.CursorStyle = input.CursorStyle;
        existing.CursorBlink = input.CursorBlink;
        if (input.XtermScrollback > 0) existing.XtermScrollback = input.XtermScrollback;

        existing.UpdatedTime = DateTime.Now;
        Orm.Update<CliConfig>().SetSource(existing).ExecuteAffrows();
        return Result.Success("配置已保存");
    }

    /// <summary>
    /// 取已解密的配置(供 PtyManager 启动 PTY 时用,不暴露给前端)。
    /// </summary>
    public CliConfig GetDecryptedForRuntime()
    {
        var cfg = GetOrCreate();
        // 注意: 这里不解密 SshPrivateKey, PtyManager 自己解。
        // 仅用作"非脱敏副本"。
        return cfg;
    }

    private static CliConfig MaskSensitive(CliConfig cfg)
    {
        return new CliConfig
        {
            Id = cfg.Id,
            RunAsUser = cfg.RunAsUser,
            DefaultShell = cfg.DefaultShell,
            DefaultCwd = cfg.DefaultCwd,
            DefaultEnvJson = cfg.DefaultEnvJson,
            SshPrivateKey = string.IsNullOrEmpty(cfg.SshPrivateKey) ? null : "***",
            SshPrivateKeyPassphrase = string.IsNullOrEmpty(cfg.SshPrivateKeyPassphrase) ? null : "***",
            ScrollbackBytes = cfg.ScrollbackBytes,
            AutoCleanDays = cfg.AutoCleanDays,
            TotalCapMb = cfg.TotalCapMb,
            IdleSessionDays = cfg.IdleSessionDays,
            FontFamily = cfg.FontFamily,
            FontSize = cfg.FontSize,
            LineHeight = cfg.LineHeight,
            LetterSpacing = cfg.LetterSpacing,
            Theme = cfg.Theme,
            CustomBgColor = cfg.CustomBgColor,
            CustomFgColor = cfg.CustomFgColor,
            CustomCursorColor = cfg.CustomCursorColor,
            CursorStyle = cfg.CursorStyle,
            CursorBlink = cfg.CursorBlink,
            XtermScrollback = cfg.XtermScrollback,
            CreatedTime = cfg.CreatedTime,
            UpdatedTime = cfg.UpdatedTime,
        };
    }

    /// <summary>
    /// 前端把敏感字段以 *** 占位时,后端保留旧值;否则加密存新值。
    /// </summary>
    private static string? PreserveOrEncrypt(string? input, string? existing)
    {
        if (input == null) return existing;
        if (input == "***") return existing;
        if (string.IsNullOrEmpty(input)) return null; // 显式清空
        return EncryptionUtil.AesEncrypt(input);
    }
}

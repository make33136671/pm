using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using SB.Framework.Models;
using SB.Framework.Services.Startup;
using Serilog;

namespace Sb.Apps.Pm.Services.Startup;

/// <summary>
/// 一次性迁移:把旧的 /webterm/config 子菜单(挂在 /webterm 父级下)清掉。
/// 因为 EnsureMenu 按 path 匹配 update,新代码改 path 为 /webterm-config 后旧条目
/// 不会被自动删,留着会让"网页终端"被当成有 children 的折叠组,主菜单点不进去。
///
/// 这个 hook 是幂等的:旧菜单删完后下次启动 Where 查询返回 0 条,无副作用。
/// 等所有线上环境都升级过一次后(2-3 个月),可以删除本文件。
/// </summary>
public class WebTerminalMenuMigration : IStartupSyncHook
{
    public int Order => 200;

    public Task RunAsync(IServiceProvider sp, CancellationToken ct)
    {
        var fsql = sp.GetRequiredService<IFreeSql>();

        var oldIds = fsql.Select<Menu>()
            .Where(m => m.Path == "/webterm/config")
            .ToList(m => m.Id);

        if (oldIds.Count == 0) return Task.CompletedTask;

        var roleMenuRows = fsql.Delete<AdminRoleMenu>()
            .Where(rm => oldIds.Contains(rm.MenuId))
            .ExecuteAffrows();
        var menuRows = fsql.Delete<Menu>()
            .Where(m => oldIds.Contains(m.Id))
            .ExecuteAffrows();

        Log.Information(
            "WebTerminalMenuMigration: 清理旧菜单 /webterm/config {N} 条,关联 AdminRoleMenu {RM} 条",
            menuRows, roleMenuRows);

        return Task.CompletedTask;
    }
}

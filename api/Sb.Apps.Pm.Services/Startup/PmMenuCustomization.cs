using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using SB.Framework.Models;
using SB.Framework.Services.Startup;
using Serilog;

namespace Sb.Apps.Pm.Services.Startup;

/// <summary>
/// PM 项目级菜单定制（幂等）。
///
/// 框架/feature 包通过 IModuleInitializer.GetMenus() 声明的菜单会在每次启动由
/// DatabaseSeeder.SyncModuleMenus 重新 upsert（按 Path 匹配），我们无法直接改这些包，
/// 所以在菜单同步之后（IStartupSyncHook 阶段）做项目级覆盖：
///   1. "访问令牌"/"令牌审计" 从顶级移动到 "系统管理"(/system) 组下
///      —— 包里定义为顶级，seeder 每次会把 ParentId 拉回 0，这里再纠正。
///   2. 删除 "邮件管理"(/email 及其子菜单)
///      —— 包里仍定义该菜单，seeder 每次会重建，这里每次启动删掉。
///   3. 删除残留的 "配置文件"(/system/app-settings) 旧菜单
///      —— 历史遗留，新版已改挂到 /system/devtools/app-settings，旧条目不会被自动删。
///
/// 全部操作幂等：目标达成后下次启动查询返回 0 条 / 已就位，无副作用。
/// </summary>
public class PmMenuCustomization : IStartupSyncHook
{
    // 必须晚于 WebTerminalMenuMigration(200)，且在菜单 seed 之后（StartupSyncRunner 本就在 seed 之后）。
    public int Order => 210;

    public Task RunAsync(IServiceProvider sp, CancellationToken ct)
    {
        var fsql = sp.GetRequiredService<IFreeSql>();

        // 1. 访问令牌 / 令牌审计 → 挂到 系统管理(/system) 组下
        var systemId = fsql.Select<Menu>()
            .Where(m => m.Path == "/system")
            .First(m => m.Id);

        if (systemId > 0)
        {
            var moved = fsql.Update<Menu>()
                .Set(m => m.ParentId, systemId)
                .Where(m => (m.Path == "/system/access-token" || m.Path == "/system/access-token-audit")
                            && m.ParentId != systemId)
                .ExecuteAffrows();
            if (moved > 0)
                Log.Information("PmMenuCustomization: 访问令牌/令牌审计 已移动到 系统管理 组下 {N} 条", moved);
        }
        else
        {
            Log.Warning("PmMenuCustomization: 未找到 /system 菜单，跳过访问令牌移动");
        }

        // 2 + 3. 删除 邮件管理(/email*) 与 残留配置文件(/system/app-settings)
        var deadIds = fsql.Select<Menu>()
            .Where(m => m.Path == "/email" || m.Path.StartsWith("/email/")
                        || m.Path == "/system/app-settings")
            .ToList(m => m.Id);

        if (deadIds.Count > 0)
        {
            var rm = fsql.Delete<AdminRoleMenu>()
                .Where(x => deadIds.Contains(x.MenuId))
                .ExecuteAffrows();
            var n = fsql.Delete<Menu>()
                .Where(m => deadIds.Contains(m.Id))
                .ExecuteAffrows();
            Log.Information(
                "PmMenuCustomization: 删除下线菜单(邮件管理/残留配置文件) {N} 条，关联 AdminRoleMenu {RM} 条",
                n, rm);
        }

        return Task.CompletedTask;
    }
}

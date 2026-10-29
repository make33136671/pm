using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Sb.Apps.Pm.Services.MindMaps;
using Serilog;
using SB.Framework.Services.Startup;

namespace Sb.Apps.Pm.Services.Startup;

/// <summary>
/// 启动后扫描所有项目，为有需求的项目刷新主导图进度数据。
/// 由基座 StartupSyncRunner 在 Module Seed 完成后自动调用。
/// </summary>
public class MindMapStartupSync : IStartupSyncHook
{
    public int Order => 100;

    public Task RunAsync(IServiceProvider sp, CancellationToken ct)
    {
        var fsql = sp.GetRequiredService<IFreeSql>();
        var mindMapService = sp.GetRequiredService<MindMapService>();

        var projects = fsql.Select<Sb.Apps.Pm.Models.Projects.Project>().ToList();
        int synced = 0;
        foreach (var p in projects)
        {
            var reqCount = fsql.Select<Sb.Apps.Pm.Models.Requirements.Requirement>()
                .Where(r => r.ProjectId == p.Id).Count();
            if (reqCount > 0)
            {
                mindMapService.SyncMain(p.Id);
                synced++;
            }
        }

        if (synced > 0)
            Log.Information("MindMapStartupSync: 已同步 {Count} 个项目主导图进度", synced);

        return Task.CompletedTask;
    }
}

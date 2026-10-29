using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sb.Apps.Pm.Services.WebTerminals;

/// <summary>
/// 定时清理 webterm 日志:
/// 1. Status=Running 且 LastActiveAt + IdleSessionDays < now → kill PTY (Reaped)
/// 2. Status != Running 且 ExitedAt + AutoCleanDays < now → 删日志 + 删会话记录
/// 3. 日志总大小 > TotalCapMb*1MB → 按 LastActiveAt 升序删非 Running 的
/// 跑频率每小时一次,启动后 60s 首次执行(避开启动高峰)。
/// </summary>
public class CliCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CliCleanupHostedService> _logger;

    public CliCleanupHostedService(IServiceScopeFactory scopeFactory, ILogger<CliCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); } catch { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { RunOnce(); }
            catch (Exception ex) { _logger.LogWarning(ex, "CLI 日志清理任务异常"); }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch { return; }
        }
    }

    private void RunOnce()
    {
        using var scope = _scopeFactory.CreateScope();
        var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        var configService = scope.ServiceProvider.GetRequiredService<CliConfigService>();
        var pty = scope.ServiceProvider.GetRequiredService<PtyManager>();
        var cfg = configService.GetOrCreate();

        // 1. 空闲会话回收
        var idleCutoff = DateTime.Now.AddDays(-cfg.IdleSessionDays);
        var idleSessions = fsql.Select<CliSession>()
            .Where(c => c.Status == CliSessionStatus.Running && c.LastActiveAt != null && c.LastActiveAt < idleCutoff)
            .ToList();
        foreach (var s in idleSessions)
        {
            _logger.LogInformation("回收空闲会话 {Id} (LastActive={At})", s.Id, s.LastActiveAt);
            pty.Kill(s.Id, "idle_reaped");
        }

        // 2. 已结束会话日志按 AutoCleanDays 删除
        var logCutoff = DateTime.Now.AddDays(-cfg.AutoCleanDays);
        var endedSessions = fsql.Select<CliSession>()
            .Where(c => c.Status != CliSessionStatus.Running && c.ExitedAt != null && c.ExitedAt < logCutoff)
            .ToList();
        foreach (var s in endedSessions)
        {
            DeleteSessionLog(pty, s);
            fsql.Delete<CliSession>().Where(c => c.Id == s.Id).ExecuteAffrows();
            _logger.LogInformation("过期清理会话 {Id} (Exited={At})", s.Id, s.ExitedAt);
        }

        // 3. 总容量上限
        long capBytes = (long)cfg.TotalCapMb * 1024 * 1024;
        long total = 0;
        if (Directory.Exists(pty.LogRoot))
        {
            foreach (var f in Directory.EnumerateFiles(pty.LogRoot, "*.log", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        if (total > capBytes)
        {
            var oldest = fsql.Select<CliSession>()
                .Where(c => c.Status != CliSessionStatus.Running)
                .OrderBy(c => c.LastActiveAt ?? c.CreatedTime)
                .Limit(200)
                .ToList();
            foreach (var s in oldest)
            {
                DeleteSessionLog(pty, s);
                fsql.Delete<CliSession>().Where(c => c.Id == s.Id).ExecuteAffrows();
                total -= s.LogFileSize;
                if (total <= capBytes) break;
            }
            if (total > capBytes)
                _logger.LogWarning("已删完所有可清理会话日志,总容量仍超上限 ({Total} > {Cap})", total, capBytes);
        }
    }

    private static void DeleteSessionLog(PtyManager pty, CliSession s)
    {
        var abs = pty.GetLogFileAbsPath(s.LogFilePath);
        if (abs != null && File.Exists(abs))
            try { File.Delete(abs); } catch { }
    }
}

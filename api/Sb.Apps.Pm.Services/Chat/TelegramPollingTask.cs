using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.BackgroundTask;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// Telegram Polling 后台任务
    /// 通过 BaseBackgroundTask 框架管理：后台任务面板可查看状态、重启、查日志
    /// 每次心跳检查所有 Active Bot 是否在线，掉线自动重连
    /// </summary>
    public class TelegramPollingTask : BaseBackgroundTask
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TelegramPollingTask> _taskLogger;
        private bool _initialized;

        /// <summary>心跳间隔：30 秒检查一次连接状态</summary>
        protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

        public TelegramPollingTask(IServiceProvider serviceProvider,
            ILogger<TelegramPollingTask> logger, IFreeSql fsql)
            : base(logger, fsql)
        {
            _serviceProvider = serviceProvider;
            _taskLogger = logger;
        }

        protected override async Task OnStartAsync(CancellationToken cancellationToken)
        {
            _taskLogger.LogInformation("TelegramPollingTask 启动，开始初始化所有 Bot 连接...");
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancellationToken)
        {
            var connMgr = _serviceProvider.GetRequiredService<ChatConnectorManager>();
            var fsql = _serviceProvider.GetRequiredService<IFreeSql>();

            var appEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            AppEnv? envFilter = null;
            if (!string.IsNullOrEmpty(appEnv) && Enum.TryParse<AppEnv>(appEnv, true, out var parsed))
                envFilter = parsed;

            var accounts = fsql.Select<ChatAccount>()
                .Where(a => a.Status == ChatAccountStatus.Active && !a.IsDeleted
                    && a.AccountType == ChatAccountType.Bot)
                .WhereIf(envFilter.HasValue,
                    a => a.Env == envFilter.Value || a.Env == AppEnv.All)
                .ToList();

            var connectedCount = 0;
            var reconnectedCount = 0;

            foreach (var account in accounts)
            {
                var bot = connMgr.GetBot(account.Id);
                if (bot != null && bot.IsConnected)
                {
                    connectedCount++;
                    continue;
                }

                // 未连接或已断开，重连
                try
                {
                    _taskLogger.LogInformation("Bot {Name}(Id={Id}) 未连接，正在重连...",
                        account.AccountName, account.Id);
                    await connMgr.DisconnectAccount(account.Id);
                    await connMgr.ConnectAccount(account);
                    reconnectedCount++;
                    connectedCount++;
                }
                catch (Exception ex)
                {
                    _taskLogger.LogWarning(ex, "Bot {Name}(Id={Id}) 重连失败",
                        account.AccountName, account.Id);
                }
            }

            if (!_initialized || reconnectedCount > 0)
            {
                _taskLogger.LogInformation(
                    "Bot 状态: {Total} 个账号, {Connected} 个在线, {Reconnected} 个重连",
                    accounts.Count, connectedCount, reconnectedCount);
                _initialized = true;
            }
        }
    }
}

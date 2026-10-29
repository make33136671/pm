using Microsoft.Extensions.DependencyInjection;
using Sb.Apps.Pm.Services.Ai;
using Sb.Apps.Pm.Services.Configuration;
using Sb.Apps.Pm.Services.Startup;
using Sb.Apps.Pm.Services.WebTerminals;
using SB.Framework.Features;
using SB.Framework.Services.Configuration;
using SB.Framework.Services.Startup;

namespace Sb.Apps.Pm.Services;

/// <summary>
/// PM 项目自身作为一个 feature（项目级 feature）。范式与 SB.Feature.* 插件统一：
/// 框架经 deps.json 发现项目程序集后，连同它的 IFeatureModule 清单一起装配，
/// 取代旧的"Program.cs 手动 AddPmServices()"。
///
/// 项目 feature 与 SB.Feature.* 插件的唯一区别：程序集名不带 SB.Feature. 前缀，
/// 靠 appsettings 的 ProjectAssemblyName 告诉框架它在哪个程序集（FeatureScanner 据此纳入扫描）。
/// 业务的 [AutoDependencyInjection] Service 和 IModuleInitializer 仍自动扫描；
/// 这里只放"自动 DI 覆盖不到"的手动接口绑定 / HostedService / 第三方 AddXxx。
/// </summary>
public sealed class PmCoreFeature : IFeatureModule
{
    public string FeatureName => "PmCore";
    public string DisplayName => "项目管理";

    public void ConfigureServices(IServiceCollection services, FeatureContext ctx)
    {
        // —— 与原 AddPmServices 逐字一致 ——
        services.AddSingleton<IFreeSqlConfigurator, PmFreeSqlConfigurator>();
        services.AddScoped<IStartupSyncHook, MindMapStartupSync>();
        services.AddScoped<IStartupSyncHook, WebTerminalMenuMigration>();
        services.AddScoped<IStartupSyncHook, PmMenuCustomization>();
        // PtyManager 必须 Singleton(持有 PTY 子进程字典,跨请求共享)
        services.AddSingleton<PtyManager>();
        services.AddHostedService<CliCleanupHostedService>();

        // AI Provider Adapters — 注册为 IAiProviderAdapter 供 AiProviderFactory 收集
        services.AddScoped<IAiProviderAdapter, HermesAiProviderAdapter>();
        services.AddScoped<IAiProviderAdapter, StubAiProviderAdapter>();

        // AI Provider — 活跃 Provider 桥接层（读取 DB 配置 + 选 adapter）
        services.AddScoped<IAiProvider, ActiveAiProvider>();

        // AI 服务层 — 框架 AutoDependencyInjection 只注册类自身，不注册接口
        services.AddScoped<IAiService, DefaultAiService>();

        // 文件管理器 — 绑 appsettings "FileManager" 节 + 注册 PathGuard 单例
        // （FileManagerService 靠 [AutoDependencyInjection] 自动 Scoped，不在此重复注册）
        services.AddPmFileManager(ctx.Configuration);

        // Agent 鉴权/令牌已下沉到框架 SB.Feature.AccessToken(功能包自注册 IApiKeyAuthenticator +
        // 全局 ScopeFilter + IApiKeyContext);PM 不再自带 agent token 栈。
    }
}

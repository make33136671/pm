using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sb.Apps.Pm.Services.FileManager;
using SB.Framework.Features;

namespace Sb.Apps.Pm.Services;

/// <summary>
/// 文件管理器模块装配。
///
/// 与 PmCoreFeature 同源（PM 自身是一个 feature 程序集），这里只放
/// 「[AutoDependencyInjection] 覆盖不到」的部分：
///   · FileManagerOptions 手动绑定 appsettings 的 "FileManager" 节
///     （框架的 OptionsType 机制是给 SB.Feature.* 插件用的，PM 内部模块走手动绑定更直观）
///   · PathGuard 注册为 Singleton（无状态，持有只读配置，每次请求无需重建）
///
/// FileManagerService 本身标了 [AutoDependencyInjection]，由框架自动注册 Scoped，
/// **不要**在这里再 AddScoped，否则双注册。
/// </summary>
public static class FileManagerModule
{
    public static IServiceCollection AddPmFileManager(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = new FileManagerOptions();
        configuration.GetSection(FileManagerOptions.SectionName).Bind(options);
        services.AddSingleton(options);
        services.AddSingleton<PathGuard>();
        return services;
    }
}

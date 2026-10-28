using SB.Framework.Features;

namespace SB.Feature.FileManager
{
    /// <summary>
    /// 服务器文件管理器插件清单（无状态薄壳）。
    /// Service 靠 [AutoDependencyInjection] 自动注册，清单【不】手动 AddScoped（避免双注册）。
    /// 审计日志走普通文件追加，不需要中间件 → 不实现 Configure / ConfigureServices。
    /// </summary>
    public sealed class FileManagerFeature : IFeatureModule
    {
        public string FeatureName => "FileManager";

        public string DisplayName => "服务器文件管理器";

        /// <summary>自动绑定 appsettings 的 Features:FileManager 节。</summary>
        public Type? OptionsType => typeof(FileManagerOptions);
    }
}

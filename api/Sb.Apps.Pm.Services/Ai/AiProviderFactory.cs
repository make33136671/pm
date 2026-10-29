using SB.Framework.Core.Attribute;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 提供商工厂 —— 收集所有 IAiProviderAdapter 实现，按 ProviderType 分发
    /// </summary>
    [AutoDependencyInjection]
    public class AiProviderFactory
    {
        private readonly Dictionary<string, IAiProviderAdapter> _adapters;

        public AiProviderFactory(IEnumerable<IAiProviderAdapter> adapters)
        {
            _adapters = adapters.ToDictionary(a => a.ProviderType, a => a, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>按 ProviderType 获取适配器，未找到时返回 null</summary>
        public IAiProviderAdapter? GetAdapter(string providerType)
        {
            return _adapters.TryGetValue(providerType, out var adapter) ? adapter : null;
        }

        /// <summary>获取所有已注册的适配器信息</summary>
        public List<AdapterInfo> GetRegisteredAdapters()
        {
            return _adapters.Values.Select(a => new AdapterInfo
            {
                ProviderType = a.ProviderType,
                DisplayName = a.DisplayName,
                SupportStream = a.SupportStream,
            }).ToList();
        }
    }
}

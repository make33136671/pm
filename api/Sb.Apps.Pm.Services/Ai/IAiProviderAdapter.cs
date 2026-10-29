using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 提供商适配器接口 —— 任务系统专用
    /// 每种 AI 实现此接口即可接入需求生成 / 任务执行链路
    /// </summary>
    public interface IAiProviderAdapter
    {
        /// <summary>提供商标识，与数据库 AiProvider.ProviderType 匹配</summary>
        string ProviderType { get; }

        /// <summary>显示名称</summary>
        string DisplayName { get; }

        /// <summary>是否支持流式输出</summary>
        bool SupportStream { get; }

        /// <summary>同步生成（一次性返回，所有 AI 都必须实现）</summary>
        Task<string> GenerateAsync(AiProvider config, string prompt, CancellationToken ct = default);

        /// <summary>流式生成（逐块返回，SupportStream=true 时必须实现）</summary>
        IAsyncEnumerable<string> StreamGenerateAsync(AiProvider config, string prompt, CancellationToken ct = default);
    }

    /// <summary>已注册适配器的元信息（供前端下拉使用）</summary>
    public class AdapterInfo
    {
        public string ProviderType { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool SupportStream { get; set; }
    }
}

using System.Runtime.CompilerServices;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// Stub 占位适配器 —— 开发阶段跑通流程用，返回模拟数据
    /// </summary>
    [AutoDependencyInjection]
    public class StubAiProviderAdapter : IAiProviderAdapter
    {
        public string ProviderType => "Stub";
        public string DisplayName => "Stub (测试用)";
        public bool SupportStream => true;

        public async Task<string> GenerateAsync(AiProvider config, string prompt, CancellationToken ct = default)
        {
            await Task.Delay(2000, ct);
            return $"[Stub AI 响应]\n\n收到指令：{prompt[..Math.Min(200, prompt.Length)]}...\n\n" +
                   "这是一个占位响应，请对接实际的 AI 提供商。\n" +
                   $"配置信息：Provider={config.ProviderType}, Model={config.ModelName ?? "未设置"}";
        }

        public async IAsyncEnumerable<string> StreamGenerateAsync(AiProvider config, string prompt, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var chunks = new[]
            {
                "[Stub AI 流式响应]\n\n",
                $"收到指令：{prompt[..Math.Min(100, prompt.Length)]}...\n\n",
                "正在分析中...\n",
                "这是一个占位的流式响应。\n",
                "请对接实际的 AI 提供商以获得真实结果。\n\n",
                $"配置：Provider={config.ProviderType}, Model={config.ModelName ?? "未设置"}\n",
                "\n[生成完成]"
            };

            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(200, ct);
                yield return chunk;
            }
        }
    }
}

namespace Sb.Apps.Pm.Services.WebTerminals;

/// <summary>
/// PTY 输出转发目标(WebSocket 端实现)。
/// </summary>
public interface IPtyClient
{
    Task SendOutputAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <param name="reason">关闭原因(写入 close frame 的 description)。</param>
    /// <param name="closeCode">
    /// 关闭码。默认 1000(NormalClosure = 会话真正结束,前端不重连)。
    /// 因「被新连接顶替」而关闭时必须传 <see cref="CloseCodes.Replaced"/>，
    /// 否则前端会误判为正常结束而放弃重连。
    /// </param>
    Task CloseAsync(string? reason = null, int closeCode = CloseCodes.Normal);
}

/// <summary>WebSocket 关闭码约定（前端 Terminal.vue 必须与此保持一致）。</summary>
public static class CloseCodes
{
    /// <summary>正常结束（会话已退出/主动停止）。前端<b>不</b>自动重连。</summary>
    public const int Normal = 1000;

    /// <summary>被同账号的另一连接顶替。前端应重连一次取回控制权，或提示已切换设备。</summary>
    public const int Replaced = 4001;
}

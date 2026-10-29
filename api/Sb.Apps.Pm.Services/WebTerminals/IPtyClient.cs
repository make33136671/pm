namespace Sb.Apps.Pm.Services.WebTerminals;

/// <summary>
/// PTY 输出转发目标(WebSocket 端实现)。
/// </summary>
public interface IPtyClient
{
    Task SendOutputAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    Task CloseAsync(string? reason = null);
}

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sb.Apps.Pm.Models.WebTerminals;
using Sb.Apps.Pm.Services.WebTerminals;
using SB.Framework.Core.Expand;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Sb.Apps.Pm.WebApi.WebTerminals;

/// <summary>
/// WebSocket endpoint 作为早期中间件注册,**完全绕开** ASP.NET endpoint routing。
/// Program.cs 在 UseSbFramework() 之前调用 UseWebTerminalEndpoint。
///
/// 路径: /webterm/ws/{id} ?token=JWT
///
/// 历史:之前用 endpoint routing(app.Map)时,curl 401 endpoint 命中,但浏览器
/// 同路径请求 404。怀疑是 MVC default route {controller}/{action}/{id?} 影响了
/// 4 段 path 的 specificity 选择,具体原因没查清。改成早期 app.Use 中间件直接按
/// path prefix 短路,routing 决策完全无关。
///
/// 协议:
///   client → server: Binary 帧=PTY 输入; Text 帧 JSON {type:"resize",cols,rows} / {type:"ping",t}
///   server → client: Binary 帧=PTY 输出; Text 帧 JSON {type:"exit",code} / {type:"pong",t}
///   ping/pong: 前端每秒发 ping(t=客户端时间戳),后端原样回 pong,前端据此算 RTT 做连接指示灯
/// </summary>
public static class WebTerminalEndpoint
{
    private const string PathPrefix = "/webterm/ws/";

    public static IApplicationBuilder UseWebTerminalEndpoint(this IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value;
            if (path != null && path.StartsWith(PathPrefix, StringComparison.Ordinal))
            {
                var tail = path.Substring(PathPrefix.Length).TrimEnd('/');
                if (int.TryParse(tail, out int id))
                {
                    await HandleAsync(ctx, id);
                    return;
                }
            }
            await next();
        });
        return app;
    }

    private static async Task HandleAsync(HttpContext ctx, int id)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILogger<object>>();

        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("WebSocket only");
            return;
        }

        // 从 query 取 token,注入 Authorization header,走 framework 现成的 JwtAdmin scheme
        var token = ctx.Request.Query["token"].ToString();
        if (!string.IsNullOrEmpty(token) && !ctx.Request.Headers.ContainsKey("Authorization"))
            ctx.Request.Headers.Authorization = "Bearer " + token;

        var auth = await ctx.AuthenticateAsync("JwtAdmin");
        if (!auth.Succeeded || auth.Principal == null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var username = auth.Principal.Username();
        if (string.IsNullOrEmpty(username))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // 校验会话归属(私有,Q5 a)
        var sessionService = ctx.RequestServices.GetRequiredService<CliSessionService>();
        var detailResult = sessionService.Detail(id);
        if (detailResult is not { Code: 0 } || detailResult.Data is not CliSession s)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (s.OwnerUsername != username)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var pty = ctx.RequestServices.GetRequiredService<PtyManager>();
        if (!pty.IsAlive(id))
        {
            ctx.Response.StatusCode = StatusCodes.Status410Gone;
            await ctx.Response.WriteAsync("会话已结束");
            return;
        }

        var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var client = new WebSocketPtyClient(ws);
        pty.Attach(id, client);

        try
        {
            // 发送 snapshot(replay 缓冲区)
            var snap = pty.GetSnapshot(id);
            if (snap != null && snap.Length > 0)
            {
                await ws.SendAsync(snap, WebSocketMessageType.Binary, true, CancellationToken.None);
            }

            var buf = new byte[8192];
            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try { result = await ws.ReceiveAsync(buf, ctx.RequestAborted); }
                catch (WebSocketException) { break; }
                catch (OperationCanceledException) { break; }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }
                    catch { }
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await pty.SendInputAsync(id, buf.AsMemory(0, result.Count));
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(buf, 0, result.Count);
                        var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("type", out var t))
                        {
                            var typeName = t.GetString();
                            if (typeName == "resize"
                                && root.TryGetProperty("cols", out var c) && root.TryGetProperty("rows", out var r))
                            {
                                pty.Resize(id, c.GetInt32(), r.GetInt32());
                            }
                            else if (typeName == "clientlog"
                                && root.TryGetProperty("msg", out var m))
                            {
                                // 前端 debug overlay 镜像到后端 logger,移动端调试不用 copy 浮层
                                logger.LogInformation("[webterm/clientlog s={SessionId} u={Username}] {Msg}",
                                    id, username, m.GetString());
                            }
                            else if (typeName == "ping")
                            {
                                // 心跳:把客户端时间戳 t 原样回传,前端算 RTT 驱动连接指示灯。
                                // 走 client.SendTextAsync 复用 _sendLock,避免与 PTY 二进制输出帧交错。
                                var tRaw = root.TryGetProperty("t", out var tv)
                                    && tv.ValueKind == JsonValueKind.Number
                                    ? tv.GetRawText() : "0";
                                await client.SendTextAsync($"{{\"type\":\"pong\",\"t\":{tRaw}}}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "解析 webterm 控制消息失败");
                    }
                }
            }
        }
        finally
        {
            pty.Detach(id, client);
        }
    }

    private sealed class WebSocketPtyClient : IPtyClient
    {
        private readonly WebSocket _ws;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public WebSocketPtyClient(WebSocket ws) { _ws = ws; }

        public async Task SendOutputAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            if (_ws.State != WebSocketState.Open) return;
            await _sendLock.WaitAsync(ct);
            try { await _ws.SendAsync(data, WebSocketMessageType.Binary, true, ct); }
            finally { _sendLock.Release(); }
        }

        /// <summary>发送 Text 帧(控制消息,如 pong),与二进制输出共用 _sendLock 串行化。</summary>
        public async Task SendTextAsync(string text)
        {
            if (_ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync();
            try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
            catch { /* 连接中断忽略 */ }
            finally { _sendLock.Release(); }
        }

        public async Task CloseAsync(string? reason = null)
        {
            if (_ws.State != WebSocketState.Open) return;
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason ?? "closed", CancellationToken.None);
            }
            catch { }
        }
    }
}

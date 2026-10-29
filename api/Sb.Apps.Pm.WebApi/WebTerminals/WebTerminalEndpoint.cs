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

    /// <summary>
    /// 断连日志上报端点（独立 HTTP，不依赖 WebSocket）。
    ///
    /// 为什么必须有它：断连那一刻 ws 已经不可用，`dlog` 里的
    /// `if (ws.readyState === OPEN) ws.send(...)` 必然失败 ——
    /// **最需要的那条日志永远到不了后端**（见文档 §4.1）。
    /// 前端改为：断连事件用 `navigator.sendBeacon` POST 到这里；
    /// 缓冲在 localStorage 的历史日志在下次连接成功时用 fetch 补传。
    /// </summary>
    private const string ClientLogPath = "/webterm/clientlog";

    public static IApplicationBuilder UseWebTerminalEndpoint(this IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value;

            if (path != null && path.StartsWith(ClientLogPath, StringComparison.Ordinal))
            {
                await HandleClientLogAsync(ctx);
                return;
            }

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

    /// <summary>
    /// 接收前端断连/重连日志。
    ///
    /// 鉴权从 query token 或 Authorization 头取，失败也**不拒绝**（降级为匿名记录）：
    /// 断连时 token 可能已失效，此时最需要日志，不能因为鉴权失败把诊断信息丢掉。
    /// </summary>
    /// <summary>
    /// 独立的诊断日志文件写入器（绕开 Serilog）。
    ///
    /// 为什么需要它：线上 Serilog 用的是 Async sink + restrictedToMaximumLevel 过滤，
    /// 排查时发现日志时常不落盘（异步缓冲），断连这种「一次性事件」极易丢失。
    /// 这里直接 append 到 logs/webterm-diag.log，同步写、立即刷盘，不会漏。
    ///
    /// 只用于 webterm 断连诊断，量级很低（每连接几行），不影响性能。
    /// </summary>
    private static string WriteDiagLog(string line)
    {
        try
        {
            // AppContext.BaseDirectory 在某些启动方式下可能不等于 ContentRoot，
            // 这里优先用环境变量 ASPNETCORE_CONTENTROOT，再退回 BaseDirectory
            var root = Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT")
                       ?? AppContext.BaseDirectory;
            var dir = Path.Combine(root, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"webterm-diag-{SB.Framework.Core.Util.AppTime.UtcNow:yyyyMMdd}.log");
            var text = $"{SB.Framework.Core.Util.AppTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {line}{Environment.NewLine}";
            // FileShare.ReadWrite：允许同时 tail 查看
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs);
            sw.Write(text);
            sw.Flush();
            fs.Flush(true);
            return path;
        }
        catch (Exception ex)
        {
            // 诊断日志失败绝不能影响业务，但要能看见失败原因
            return "ERR:" + ex.Message;
        }
    }
    private static async Task HandleClientLogAsync(HttpContext ctx)
    {
        // ⚠️ 必须用专用 category：ILogger<object> 的 category 是 System.Object，
        // 会被 appsettings 里 System=Warning 的 Override 压掉 Information 级日志（实测踩坑）
        // ⚠️ 用命名 category（不能用 ILogger<object>：其 category 是 System.Object，
        // 会被 appsettings 里 System=Warning 的 Override 压掉 Information 级日志）
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Sb.Apps.Pm.WebApi.WebTerminals");

        if (!HttpMethods.IsPost(ctx.Request.Method))
        {
            ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        string token = ctx.Request.Query["token"].ToString();
        if (string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(ctx.Request.Headers.Authorization))
            token = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();

        string username = "anonymous";
        if (!string.IsNullOrEmpty(token))
        {
            ctx.Request.Headers.Authorization = "Bearer " + token;
            var auth = await ctx.AuthenticateAsync("JwtAdmin");
            if (auth.Succeeded && auth.Principal != null)
            {
                var u = auth.Principal.Username();
                if (!string.IsNullOrEmpty(u)) username = u;
            }
        }

        string body;
        using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(ctx.RequestAborted);
        }

        var remoteIp = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                       ?? ctx.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";

        // body 形如 {"sessionId":123,"lines":["...","..."]}，单行过长时截断防日志爆量
        if (body.Length > 16000) body = body[..16000] + "...(truncated)";

        logger.LogWarning(
            "[webterm/clientlog-http u={Username} remote={RemoteIp}] {Body}",
            username, remoteIp, body);
        var diagResult = WriteDiagLog($"[CLIENT-LOG] user={username} remote={remoteIp} body={body}");

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        // 回显写入结果，便于线上直接确认诊断日志是否落盘
        await ctx.Response.WriteAsync($"ok diag={diagResult}");
    }
    private static async Task HandleAsync(HttpContext ctx, int id)
    {
        // ⚠️ 必须用专用 category：ILogger<object> 的 category 是 System.Object，
        // 会被 appsettings 里 System=Warning 的 Override 压掉 Information 级日志（实测踩坑）
        // ⚠️ 用命名 category（不能用 ILogger<object>：其 category 是 System.Object，
        // 会被 appsettings 里 System=Warning 的 Override 压掉 Information 级日志）
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Sb.Apps.Pm.WebApi.WebTerminals");

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

        // ── 断连诊断用：连接生命周期全链路日志（见 docs/webterm/网页终端断连问题与调试日志.md §5.1）──
        // 关键：必须记录「存活时长」和「断开原因分类」，才能区分 120s 规律断开与随机断开。
        var remoteIp = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                       ?? ctx.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
        var connStartedAt = SB.Framework.Core.Util.AppTime.UtcNow;
        var connId = Guid.NewGuid().ToString("N")[..8];
        string closeReason = "unknown";
        long pingCount = 0;

        var ua = ctx.Request.Headers.UserAgent.ToString();
        logger.LogInformation(
            "[webterm] 连接建立 conn={ConnId} s={SessionId} u={Username} remote={RemoteIp} ua={UserAgent}",
            connId, id, username, remoteIp, ua);
        WriteDiagLog($"[CONNECT] conn={connId} s={id} user={username} remote={remoteIp} ua={ua}");

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
                catch (WebSocketException wex)
                {
                    // 断连诊断：区分「客户端主动断」和「链路异常断」的关键
                    closeReason = $"websocket-exception:{ws.CloseStatus?.ToString() ?? "n/a"}:{wex.Message}";
                    var aliveSec = (long)(SB.Framework.Core.Util.AppTime.UtcNow - connStartedAt).TotalSeconds;
                    logger.LogWarning(wex,
                        "[webterm] 异常断开 conn={ConnId} s={SessionId} 存活={Seconds}s wsState={State} wsCloseStatus={Status}",
                        connId, id, aliveSec, ws.State, ws.CloseStatus?.ToString() ?? "n/a");
                    WriteDiagLog($"[ABNORMAL] conn={connId} s={id} alive={aliveSec}s state={ws.State} closeStatus={ws.CloseStatus} err={wex.Message}");
                    break;
                }
                catch (OperationCanceledException)
                {
                    closeReason = "request-aborted";
                    var aliveSec2 = (long)(SB.Framework.Core.Util.AppTime.UtcNow - connStartedAt).TotalSeconds;
                    logger.LogInformation(
                        "[webterm] 请求被取消 conn={ConnId} s={SessionId} 存活={Seconds}s（通常是浏览器关闭/刷新页面）",
                        connId, id, aliveSec2);
                    WriteDiagLog($"[CANCELED] conn={connId} s={id} alive={aliveSec2}s");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    closeReason = $"client-close:{result.CloseStatus?.ToString() ?? "n/a"}:{result.CloseStatusDescription}";
                    logger.LogInformation(
                        "[webterm] 收到客户端 Close conn={ConnId} s={SessionId} code={Code} desc={Desc} 存活={Seconds}s",
                        connId, id, result.CloseStatus?.ToString() ?? "n/a",
                        result.CloseStatusDescription ?? "",
                        (long)(SB.Framework.Core.Util.AppTime.UtcNow - connStartedAt).TotalSeconds);
                    WriteDiagLog($"[CLIENT-CLOSE] conn={connId} s={id} code={result.CloseStatus} desc={result.CloseStatusDescription}");
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
                                pingCount++;
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
            var lifespan = (long)(SB.Framework.Core.Util.AppTime.UtcNow - connStartedAt).TotalSeconds;
            // 断连诊断的「汇总行」：一行看清 存活多久 / 怎么断的 / 心跳次数
            logger.LogInformation(
                "[webterm] 连接结束 conn={ConnId} s={SessionId} u={Username} 存活={Seconds}s 原因={Reason} ping={Pings} wsState={State}",
                connId, id, username, lifespan, closeReason, pingCount, ws.State);
            WriteDiagLog($"[END] conn={connId} s={id} user={username} alive={lifespan}s reason={closeReason} pings={pingCount} state={ws.State}");
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

        /// <summary>
        /// 自定义关闭码：4001 = 被同账号新连接顶替（见 <see cref="CloseCodes"/>）。
        /// 必须与 1000(NormalClosure) 区分开 —— 前端把 1000 当作「会话真正结束」而放弃重连。
        /// </summary>
        public async Task CloseAsync(string? reason = null, int closeCode = CloseCodes.Normal)
        {
            if (_ws.State != WebSocketState.Open) return;
            try
            {
                await _ws.CloseAsync((WebSocketCloseStatus)closeCode, reason ?? "closed", CancellationToken.None);
            }
            catch { }
        }
    }
}

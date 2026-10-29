using FreeSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.WebTerminals;
using SB.Framework.Core.Util;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.WebTerminals;

/// <summary>
/// PTY 进程池 + 会话生命周期管理。Singleton。
/// pm 进程内一份,持有所有 alive PTY 子进程的 NativePty 实例 + 输出 buffer + 日志文件 + attached 客户端。
/// </summary>
public sealed class PtyManager : IDisposable
{
    private readonly ConcurrentDictionary<int, RuntimeSession> _sessions = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PtyManager> _logger;
    private readonly string _logRoot;
    private readonly int _scrollbackBytes;
    private readonly string _sshKeyDir;

    public PtyManager(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<PtyManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _logRoot = config["WebTerminal:LogRoot"]
            ?? Path.Combine(env.ContentRootPath, "logs", "cli");
        _scrollbackBytes = config.GetValue<int?>("WebTerminal:ScrollbackBytes") ?? 4 * 1024 * 1024;
        Directory.CreateDirectory(_logRoot);

        // SSH key 临时目录: linux 优先 /dev/shm(tmpfs 不落盘),mac 用 /tmp
        _sshKeyDir = Directory.Exists("/dev/shm") ? "/dev/shm" : Path.GetTempPath();

        ReapDanglingOnStartup();
    }

    /// <summary>
    /// pm 启动时把数据库里 Status=Running 的 session 全部标记为 Terminated/pm_restart
    /// (因为 pm 重启 PTY 子进程已被 SIGHUP 杀掉,记录里残留 Running 状态会误导 UI)。
    /// </summary>
    private void ReapDanglingOnStartup()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            int n = fsql.Update<CliSession>()
                .Set(c => c.Status, CliSessionStatus.Terminated)
                .Set(c => c.TerminationReason, "pm_restart")
                .Set(c => c.ExitedAt, DateTime.Now)
                .Where(c => c.Status == CliSessionStatus.Running)
                .ExecuteAffrows();
            if (n > 0) _logger.LogInformation("pm 重启清理上次未结束的 webterm 会话 {N} 个", n);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReapDanglingOnStartup 失败");
        }
    }

    /// <summary>
    /// 启动 PTY 进程并把 session 加入活跃池。返回实际 pid。
    /// </summary>
    public int Start(int sessionId, CliConfig cfg, int cols, int rows, string? cwdOverride = null)
    {
        if (_sessions.ContainsKey(sessionId))
            throw new InvalidOperationException($"会话 {sessionId} 已经在运行");

        // 准备 SSH key 临时文件(写 600 权限)
        string? sshKeyPath = null;
        if (!string.IsNullOrEmpty(cfg.SshPrivateKey))
        {
            try
            {
                var key = EncryptionUtil.AesDecrypt(cfg.SshPrivateKey);
                sshKeyPath = Path.Combine(_sshKeyDir, $"pm-cli-key-{sessionId}");
                File.WriteAllText(sshKeyPath, key.EndsWith("\n") ? key : key + "\n");
                Chmod600(sshKeyPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解密/写入 SSH key 失败,跳过 git 凭据注入");
                sshKeyPath = null;
            }
        }

        // env: 继承 pm 环境 + 注入终端默认 + 用户配置 + SSH key
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in Environment.GetEnvironmentVariables() as System.Collections.IDictionary
            ?? new Dictionary<object, object>())
        {
            if (kv is System.Collections.DictionaryEntry de && de.Key is string k && de.Value is string v)
                env[k] = v;
        }
        env["TERM"] = "xterm-256color";
        env["COLUMNS"] = cols.ToString();
        env["LINES"] = rows.ToString();
        if (!env.ContainsKey("LANG")) env["LANG"] = "zh_CN.UTF-8";

        // 用户配置的 env
        if (!string.IsNullOrEmpty(cfg.DefaultEnvJson))
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<EnvItem>>(cfg.DefaultEnvJson) ?? new();
                foreach (var item in items)
                {
                    if (string.IsNullOrEmpty(item.Key)) continue;
                    var val = item.Encrypted && !string.IsNullOrEmpty(item.Value)
                        ? EncryptionUtil.AesDecrypt(item.Value)
                        : item.Value ?? "";
                    env[item.Key] = val;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 DefaultEnvJson 失败,跳过");
            }
        }

        if (sshKeyPath != null)
            env["GIT_SSH_COMMAND"] = $"ssh -i {sshKeyPath} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null";

        // 决定 app 和 args:RunAsUser 配了就走 sudo 切身份
        string app;
        List<string> args;
        string shell = string.IsNullOrWhiteSpace(cfg.DefaultShell) ? "/bin/bash" : cfg.DefaultShell;
        if (!string.IsNullOrWhiteSpace(cfg.RunAsUser))
        {
            app = "/usr/bin/sudo";
            args = new() { "-H", "-E", "-u", cfg.RunAsUser, "--", shell, "-i" };
        }
        else
        {
            app = shell;
            args = new() { "-i" };
        }

        string cwd = cwdOverride ?? cfg.DefaultCwd
            ?? (string.IsNullOrWhiteSpace(cfg.RunAsUser)
                ? Environment.GetEnvironmentVariable("HOME") ?? "/"
                : $"/home/{cfg.RunAsUser}");
        if (!Directory.Exists(cwd)) cwd = "/";

        var pty = NativePty.Spawn(new NativePtyOptions
        {
            App = app,
            Args = args,
            Env = env,
            Cwd = cwd,
            Cols = cols,
            Rows = rows,
        });

        // 准备日志文件
        var logRel = Path.Combine(DateTime.Now.ToString("yyyyMM"), $"session-{sessionId}.log");
        var logAbs = Path.Combine(_logRoot, logRel);
        Directory.CreateDirectory(Path.GetDirectoryName(logAbs)!);
        var logFs = new FileStream(logAbs, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 8192, useAsync: false);

        var rs = new RuntimeSession
        {
            SessionId = sessionId,
            Pty = pty,
            Buffer = new CircularByteBuffer(_scrollbackBytes),
            LogFile = logFs,
            LogPath = logRel,
            SshKeyTmpPath = sshKeyPath,
            LastActiveAt = DateTime.Now,
        };
        _sessions[sessionId] = rs;

        pty.Exited += code => OnPtyExited(rs, code);
        StartReadLoop(rs);

        // 持久化 pid + Cwd + ShellPath
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            fsql.Update<CliSession>()
                .Set(c => c.Pid, pty.Pid)
                .Set(c => c.ShellPath, shell)
                .Set(c => c.Cwd, cwd)
                .Set(c => c.LogFilePath, logRel)
                .Set(c => c.Status, CliSessionStatus.Running)
                .Set(c => c.LastActiveAt, DateTime.Now)
                .Where(c => c.Id == sessionId)
                .ExecuteAffrows();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "持久化 session {Id} 启动信息失败", sessionId);
        }

        return pty.Pid;
    }

    public bool Attach(int sessionId, IPtyClient client)
    {
        if (!_sessions.TryGetValue(sessionId, out var rs)) return false;
        lock (rs.Lock)
        {
            // 已有客户端: 主动踢掉(单 WS 约束,Q5 a)
            var prev = rs.Client;
            rs.Client = client;
            rs.LastActiveAt = DateTime.Now;
            if (prev != null)
                _ = prev.CloseAsync("被同账号新连接顶替");
        }
        return true;
    }

    public void Detach(int sessionId, IPtyClient client)
    {
        if (!_sessions.TryGetValue(sessionId, out var rs)) return;
        lock (rs.Lock)
        {
            if (ReferenceEquals(rs.Client, client))
                rs.Client = null;
            rs.LastActiveAt = DateTime.Now;
        }
    }

    public Task SendInputAsync(int sessionId, ReadOnlyMemory<byte> data)
    {
        if (!_sessions.TryGetValue(sessionId, out var rs)) return Task.CompletedTask;
        rs.LastActiveAt = DateTime.Now;
        try
        {
            // 用 native write 直接 syscall,绕开 FileStream — 后者 WriteAsync 在 PTY master fd 上会卡住不返回
            rs.Pty.WriteInput(data.Span);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SendInputAsync session {Id} write failed", sessionId);
        }
        return Task.CompletedTask;
    }

    public bool Resize(int sessionId, int cols, int rows)
    {
        if (!_sessions.TryGetValue(sessionId, out var rs)) return false;
        rs.Pty.Resize(cols, rows);
        return true;
    }

    public byte[]? GetSnapshot(int sessionId)
        => _sessions.TryGetValue(sessionId, out var rs) ? rs.Buffer.Snapshot() : null;

    public bool IsAlive(int sessionId)
        => _sessions.TryGetValue(sessionId, out var rs) && rs.Pty.IsRunning;

    public IEnumerable<int> GetAllRunningSessionIds() => _sessions.Keys.ToArray();

    /// <summary>
    /// 强制 kill PTY 进程并归档会话。reason 写入 TerminationReason 字段。
    /// </summary>
    public void Kill(int sessionId, string reason)
    {
        if (!_sessions.TryGetValue(sessionId, out var rs)) return;

        // 先记录原因(供 OnPtyExited 取用)
        rs.TerminationReason = reason;

        try { rs.Pty.Kill(15 /* SIGTERM */); } catch { }
        // 给点时间优雅退出,然后 SIGKILL
        Task.Run(async () =>
        {
            await Task.Delay(2000);
            if (rs.Pty.IsRunning)
            {
                try { rs.Pty.Kill(9 /* SIGKILL */); } catch { }
            }
        });
    }

    private void StartReadLoop(RuntimeSession rs)
    {
        _ = Task.Run(async () =>
        {
            var buf = new byte[8192];
            try
            {
                while (true)
                {
                    int n;
                    try { n = await rs.Pty.Stream.ReadAsync(buf.AsMemory(0, buf.Length), rs.ReadCts.Token); }
                    catch { break; }
                    if (n <= 0) break;

                    rs.Buffer.Append(buf.AsSpan(0, n));

                    try
                    {
                        await rs.LogFile!.WriteAsync(buf.AsMemory(0, n));
                        await rs.LogFile.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "写入会话 {Id} 日志失败", rs.SessionId);
                    }

                    // 转发给 attached 客户端
                    IPtyClient? client;
                    lock (rs.Lock) client = rs.Client;
                    if (client != null)
                    {
                        try
                        {
                            await client.SendOutputAsync(buf.AsMemory(0, n), CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "向客户端转发输出失败,断开它");
                            lock (rs.Lock)
                            {
                                if (ReferenceEquals(rs.Client, client)) rs.Client = null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ReadLoop 异常 session {Id}", rs.SessionId);
            }
        });
    }

    private void OnPtyExited(RuntimeSession rs, int exitCode)
    {
        try { rs.ReadCts.Cancel(); } catch { }
        try { rs.LogFile?.Dispose(); } catch { }
        if (rs.SshKeyTmpPath != null && File.Exists(rs.SshKeyTmpPath))
        {
            try { File.Delete(rs.SshKeyTmpPath); } catch { }
        }

        IPtyClient? client;
        lock (rs.Lock) { client = rs.Client; rs.Client = null; }
        if (client != null) _ = client.CloseAsync("会话已退出");

        var reason = rs.TerminationReason ?? (exitCode == 0 ? "user_exit" : "process_died");
        var status = reason switch
        {
            "user_terminated" => CliSessionStatus.Terminated,
            "idle_reaped" => CliSessionStatus.Reaped,
            _ => CliSessionStatus.Exited,
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            long size = 0;
            try
            {
                var fullPath = Path.Combine(_logRoot, rs.LogPath ?? "");
                if (File.Exists(fullPath)) size = new FileInfo(fullPath).Length;
            }
            catch { }
            fsql.Update<CliSession>()
                .Set(c => c.Status, status)
                .Set(c => c.ExitCode, exitCode)
                .Set(c => c.ExitedAt, DateTime.Now)
                .Set(c => c.TerminationReason, reason)
                .Set(c => c.LogFileSize, size)
                .Where(c => c.Id == rs.SessionId)
                .ExecuteAffrows();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "持久化 session {Id} 退出状态失败", rs.SessionId);
        }

        _sessions.TryRemove(rs.SessionId, out _);
        try { rs.Pty.Dispose(); } catch { }
    }

    private static void Chmod600(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void Dispose()
    {
        foreach (var rs in _sessions.Values)
        {
            try { rs.Pty.Dispose(); } catch { }
            try { rs.LogFile?.Dispose(); } catch { }
            if (rs.SshKeyTmpPath != null && File.Exists(rs.SshKeyTmpPath))
                try { File.Delete(rs.SshKeyTmpPath); } catch { }
        }
        _sessions.Clear();
    }

    /// <summary>会话日志文件绝对路径(若存在)。</summary>
    public string? GetLogFileAbsPath(string? relPath)
        => string.IsNullOrEmpty(relPath) ? null : Path.Combine(_logRoot, relPath);

    public string LogRoot => _logRoot;

    private sealed class RuntimeSession
    {
        public int SessionId;
        public NativePty Pty = null!;
        public CircularByteBuffer Buffer = null!;
        public FileStream? LogFile;
        public string? LogPath;
        public string? SshKeyTmpPath;
        public IPtyClient? Client;
        public DateTime LastActiveAt;
        public CancellationTokenSource ReadCts = new();
        public string? TerminationReason;
        public readonly object Lock = new();
    }

    public sealed class EnvItem
    {
        public string Key { get; set; } = "";
        public string? Value { get; set; }
        public bool Encrypted { get; set; } = false;
    }
}

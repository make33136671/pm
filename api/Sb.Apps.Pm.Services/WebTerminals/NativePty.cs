using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Sb.Apps.Pm.Services.WebTerminals;

/// <summary>
/// 跨 Linux PTY 包装。
///
/// 历史:第一版用 libutil.forkpty,在 .NET 8 + Linux 下会让 child 段错误,顺带把
/// pm 主进程一起 SIGSEGV 拖崩(.NET runtime 是多线程,fork 不安全)。已确诊后改为
/// posix_openpt + posix_spawn + setsid 路线 — OS 层 atomic 创建子进程,.NET runtime
/// 状态完全不污染。
///
/// macOS 上 setsid 命令没有 -c 选项,本类只在 Linux 跑通。pm 部署目标也是 Linux。
/// </summary>
public sealed class NativePty : IDisposable
{
    private int _masterFd = -1;
    private int _pid;
    private FileStream? _stream;
    private CancellationTokenSource? _waitCts;
    private bool _disposed;
    private readonly object _exitLock = new();

    public int Pid => _pid;
    public bool IsRunning { get; private set; }
    public int? ExitCode { get; private set; }
    public event Action<int>? Exited;
    public Stream Stream => _stream ?? throw new InvalidOperationException("PTY 未启动");

    public static NativePty Spawn(NativePtyOptions options)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("NativePty 仅支持 Linux");

        // 1) 分配 PTY master
        int master = Native.posix_openpt(Native.O_RDWR | Native.O_NOCTTY);
        if (master < 0) throw new IOException($"posix_openpt failed errno={Marshal.GetLastWin32Error()}");

        try
        {
            if (Native.grantpt(master) != 0)
                throw new IOException($"grantpt failed errno={Marshal.GetLastWin32Error()}");
            if (Native.unlockpt(master) != 0)
                throw new IOException($"unlockpt failed errno={Marshal.GetLastWin32Error()}");

            // 拿 slave 路径
            byte[] nameBuf = new byte[256];
            int rc;
            unsafe
            {
                fixed (byte* p = nameBuf)
                    rc = Native.ptsname_r(master, (IntPtr)p, (UIntPtr)nameBuf.Length);
            }
            if (rc != 0)
                throw new IOException($"ptsname_r failed errno={Marshal.GetLastWin32Error()}");
            int len = Array.IndexOf(nameBuf, (byte)0);
            string slavePath = System.Text.Encoding.UTF8.GetString(nameBuf, 0, len < 0 ? nameBuf.Length : len);

            // 2) open slave fd 并设 winsize
            int slave = Native.open(slavePath, Native.O_RDWR | Native.O_NOCTTY);
            if (slave < 0) throw new IOException($"open slave {slavePath} failed errno={Marshal.GetLastWin32Error()}");

            try
            {
                SetWinsize(slave, options.Cols, options.Rows);

                // 3) 准备 file_actions: child 里 close master, dup2 slave to 0/1/2, close slave
                //    posix_spawn_file_actions_t 在 glibc 上是 ~80 bytes,这里申请 512 够用
                IntPtr fa = Marshal.AllocHGlobal(512);
                Marshal.WriteIntPtr(fa, IntPtr.Zero); // 清零 padding 区
                int faInited = Native.posix_spawn_file_actions_init(fa);
                if (faInited != 0)
                    throw new IOException($"posix_spawn_file_actions_init failed err={faInited}");
                try
                {
                    Check(Native.posix_spawn_file_actions_addclose(fa, master), "addclose master");
                    Check(Native.posix_spawn_file_actions_adddup2(fa, slave, 0), "dup2 slave→0");
                    Check(Native.posix_spawn_file_actions_adddup2(fa, slave, 1), "dup2 slave→1");
                    Check(Native.posix_spawn_file_actions_adddup2(fa, slave, 2), "dup2 slave→2");
                    Check(Native.posix_spawn_file_actions_addclose(fa, slave), "addclose slave");

                    // 在 exec 前 chdir 到 options.Cwd — posix_spawn 本身不接受 cwd 参数,
                    // glibc 2.29+ 提供 addchdir_np。Cwd 已被上层 (PtyManager) 校验过存在 + 不为空。
                    if (!string.IsNullOrEmpty(options.Cwd))
                    {
                        Check(Native.posix_spawn_file_actions_addchdir_np(fa, options.Cwd), "addchdir_np");
                    }
                    // 也 close 所有可能继承的 cloexec=0 fd? 默认 posix_spawn 不 closefrom,
                    // 但 .NET 的 fd 大多带 CLOEXEC,问题不大。

                    // 4) 构建 argv / envp:用 setsid -c 包装,setsid 会做 setsid()+ioctl(TIOCSCTTY)
                    //    给 child 拿到 controlling tty(slave 现在是 child 的 stdin)
                    var argList = new List<string> { "setsid", "-c", "--", options.App };
                    argList.AddRange(options.Args);
                    var envList = options.Env.Select(kv => $"{kv.Key}={kv.Value}").ToList();

                    IntPtr argv = BuildStringArray(argList);
                    IntPtr envp = BuildStringArray(envList);
                    try
                    {
                        int pid;
                        rc = Native.posix_spawn(out pid, "/usr/bin/setsid", fa, IntPtr.Zero, argv, envp);
                        if (rc != 0)
                            throw new IOException($"posix_spawn failed err={rc} ({Marshal.GetLastWin32Error()})");

                        // 父进程 close slave
                        Native.close(slave);
                        slave = -1;

                        var sfh = new SafeFileHandle((IntPtr)master, ownsHandle: true);
                        var stream = new FileStream(sfh, FileAccess.ReadWrite, bufferSize: 8192, isAsync: false);

                        var pty = new NativePty
                        {
                            _masterFd = master,
                            _pid = pid,
                            _stream = stream,
                            IsRunning = true,
                        };
                        // master 现在归 pty 持有,don't close in finally
                        master = -1;
                        pty.StartWaitpidWatcher();
                        return pty;
                    }
                    finally
                    {
                        FreeStringArray(argv);
                        FreeStringArray(envp);
                    }
                }
                finally
                {
                    Native.posix_spawn_file_actions_destroy(fa);
                    Marshal.FreeHGlobal(fa);
                }
            }
            finally
            {
                if (slave >= 0) Native.close(slave);
            }
        }
        catch
        {
            if (master >= 0) Native.close(master);
            throw;
        }
    }

    private static void Check(int rc, string what)
    {
        if (rc != 0) throw new IOException($"{what} failed rc={rc}");
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed || _masterFd < 0) return;
        SetWinsize(_masterFd, cols, rows);
    }

    /// <summary>
    /// 直接 syscall write 到 PTY master fd。绕开 FileStream(buffered).WriteAsync — 后者在
    /// 部分 PTY master fd 上会卡住不返回（具体原因不明,可能跟 .NET FileStream sync handle
    /// 内部的 buffer/lock 跟 PTY 的 select-driven 半双工模式有冲突）。
    /// </summary>
    public unsafe void WriteInput(ReadOnlySpan<byte> data)
    {
        if (_disposed || _masterFd < 0)
            throw new ObjectDisposedException(nameof(NativePty));
        if (data.IsEmpty) return;
        fixed (byte* p = data)
        {
            int total = 0;
            while (total < data.Length)
            {
                var n = (long)Native.write(_masterFd, (IntPtr)(p + total), (IntPtr)(data.Length - total));
                if (n < 0)
                    throw new IOException($"write to PTY master failed errno={Marshal.GetLastWin32Error()}");
                total += (int)n;
            }
        }
    }

    private static void SetWinsize(int fd, int cols, int rows)
    {
        var ws = new Winsize { ws_row = (ushort)rows, ws_col = (ushort)cols };
        unsafe
        {
            Winsize wsLocal = ws;
            Native.ioctl(fd, Native.TIOCSWINSZ, (IntPtr)(&wsLocal));
        }
    }

    public void Kill(int signal = 15)
    {
        if (_pid > 0) Native.kill(_pid, signal);
    }

    private void StartWaitpidWatcher()
    {
        _waitCts = new CancellationTokenSource();
        var ct = _waitCts.Token;
        _ = Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                int status;
                int r = Native.waitpid(_pid, out status, 1 /* WNOHANG */);
                if (r > 0)
                {
                    int code;
                    if ((status & 0x7f) == 0) code = (status >> 8) & 0xff;       // WIFEXITED
                    else if ((status & 0x7f) != 0x7f) code = -((status & 0x7f)); // signaled
                    else code = -1;
                    FireExited(code);
                    return;
                }
                if (r < 0) { FireExited(-1); return; }
                try { Thread.Sleep(200); } catch { }
            }
        }, ct);
    }

    private void FireExited(int code)
    {
        bool fire = false;
        lock (_exitLock)
        {
            if (IsRunning)
            {
                IsRunning = false;
                ExitCode = code;
                fire = true;
            }
        }
        if (fire) Exited?.Invoke(code);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _waitCts?.Cancel(); } catch { }
        try { Kill(15); } catch { }
        try { _stream?.Dispose(); } catch { }
        _masterFd = -1;
    }

    // ==================== unmanaged 内存帮手 ====================

    private static IntPtr BuildStringArray(List<string> values)
    {
        IntPtr[] ptrs = new IntPtr[values.Count + 1];
        for (int i = 0; i < values.Count; i++) ptrs[i] = Marshal.StringToHGlobalAnsi(values[i]);
        ptrs[values.Count] = IntPtr.Zero;
        IntPtr arr = Marshal.AllocHGlobal(IntPtr.Size * ptrs.Length);
        Marshal.Copy(ptrs, 0, arr, ptrs.Length);
        return arr;
    }

    private static void FreeStringArray(IntPtr arr)
    {
        if (arr == IntPtr.Zero) return;
        IntPtr p;
        int offset = 0;
        while ((p = Marshal.ReadIntPtr(arr, offset)) != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(p);
            offset += IntPtr.Size;
        }
        Marshal.FreeHGlobal(arr);
    }

    // ==================== Native ====================

    [StructLayout(LayoutKind.Sequential)]
    private struct Winsize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    private static class Native
    {
        public const int O_RDWR = 2;
        public const int O_NOCTTY = 0x100; // Linux 值

        // TIOCSWINSZ - Linux 0x5414, macOS 0x80087467(macOS 不部署,只取 Linux 值)
        public const ulong TIOCSWINSZ = 0x5414;

        [DllImport("libc", SetLastError = true)]
        public static extern int posix_openpt(int flags);

        [DllImport("libc", SetLastError = true)]
        public static extern int grantpt(int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern int unlockpt(int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern int ptsname_r(int fd, IntPtr buf, UIntPtr buflen);

        [DllImport("libc", SetLastError = true)]
        public static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern IntPtr write(int fd, IntPtr buf, IntPtr count);

        [DllImport("libc", SetLastError = true)]
        public static extern int ioctl(int fd, ulong request, IntPtr argp);

        [DllImport("libc", SetLastError = true)]
        public static extern int kill(int pid, int sig);

        [DllImport("libc", SetLastError = true)]
        public static extern int waitpid(int pid, out int status, int options);

        // ---- posix_spawn ----
        [DllImport("libc", SetLastError = true)]
        public static extern int posix_spawn_file_actions_init(IntPtr actions);

        [DllImport("libc", SetLastError = true)]
        public static extern int posix_spawn_file_actions_destroy(IntPtr actions);

        [DllImport("libc", SetLastError = true)]
        public static extern int posix_spawn_file_actions_addclose(IntPtr actions, int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern int posix_spawn_file_actions_adddup2(IntPtr actions, int fd, int newfd);

        // glibc 2.29+ (ubuntu 20.04 / debian 11 / centos 9 起)
        [DllImport("libc", SetLastError = true)]
        public static extern int posix_spawn_file_actions_addchdir_np(IntPtr actions, [MarshalAs(UnmanagedType.LPStr)] string path);

        [DllImport("libc", SetLastError = true)]
        public static extern int posix_spawn(
            out int pid,
            [MarshalAs(UnmanagedType.LPStr)] string path,
            IntPtr file_actions,
            IntPtr attrp,
            IntPtr argv,
            IntPtr envp);
    }
}

public sealed class NativePtyOptions
{
    public string App { get; set; } = "/bin/bash";
    public List<string> Args { get; set; } = new() { "-i" };
    public string? Cwd { get; set; }
    public Dictionary<string, string> Env { get; set; } = new();
    public int Cols { get; set; } = 120;
    public int Rows { get; set; } = 30;
}

using Microsoft.Extensions.Logging.Abstractions;
using Sb.Apps.Pm.Services.FileManager;
using SB.Framework.Core.Util;

namespace Sb.Apps.Pm.FileManager.Tests;

/// <summary>
/// 轻量冒烟测试（不引测试框架，直接 dotnet run）。
/// 覆盖：路径穿越 / 白名单 / 黑名单 / 软链逃逸 / 读写 / 原子写 / 并发 hash / 二进制识别 / 权限位。
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static async Task<int> Main()
    {
        AppTime.Initialize("Asia/Shanghai");

        var sandbox = Path.Combine(Path.GetTempPath(), "fm-sandbox-" + Guid.NewGuid().ToString("N")[..8]);
        var outside = Path.Combine(Path.GetTempPath(), "fm-outside-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sandbox);
        Directory.CreateDirectory(outside);
        try
        {
            await RunAll(sandbox, outside);
        }
        finally
        {
            SafeDelete(sandbox);
            SafeDelete(outside);
        }

        Console.WriteLine();
        Console.WriteLine($"==== 通过 {_pass} / 失败 {_fail} ====");
        return _fail == 0 ? 0 : 1;
    }

    private static async Task RunAll(string sandbox, string outside)
    {
        Section("1. PathGuard —— 全盘模式 + 黑名单");
        var svc = BuildService(sandbox, allowedRoots: [], denied: ["/proc", "/sys", "/dev"]);

        // 全盘模式：默认根 = 当前用户家目录
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Check("相对路径挂到默认根($HOME)", svc.List(".").Path == home.TrimEnd('/'), svc.List(".").Path);
        Throws("/proc 被拒绝", () => svc.List("/proc"), "禁止");
        var rooted = svc.List("/etc").Path;
        Check("全盘模式下 /etc 可达", rooted == "/etc", rooted);
        Check("路径已规范化（无 .. 残留）", !rooted.Contains(".."), rooted);

        Section("2. PathGuard —— 白名单模式");
        var guarded = BuildService(sandbox, allowedRoots: [sandbox], denied: ["/proc"]);
        var listing = guarded.List(sandbox);
        Check("白名单根显示为 /", listing.Path == "/" && listing.Self != null, listing.Path);
        Throws("白名单外 /etc 被拒绝", () => guarded.List("/etc"), "不在允许访问");
        Throws("穿越到父目录被拒绝", () => guarded.List(sandbox + "/../"), "不在允许访问");
        Throws("NUL 字符被拒绝", () => guarded.List(sandbox + "/a\0b"), "非法字符");
        Throws("新建时父目录越界被拒绝", () => guarded.Create(outside + "/x.txt", false, "tester"), "不在允许访问");

        Section("3. 写入 / 读取 / 并发 hash");
        var filePath = Path.Combine(sandbox, "app.conf");
        var createResult = guarded.Create(filePath, false, "tester");
        Check("新建文件成功", createResult.Message.Contains("已创建"), createResult.Message);

        var written = guarded.Save(filePath, "line1\nline2\n", null, false, "tester");
        Check("保存成功", written.Message.Contains("保存成功"), written.Message);

        var read = guarded.Read(filePath);
        Check("读到内容", read.Content == "line1\nline2\n", read.Content.Replace("\n", "\\n"));
        Check("行数 = 2", read.LineCount == 2, read.LineCount.ToString());
        Check("换行 = LF", read.LineEnding == "LF", read.LineEnding);
        Check("非二进制", !read.IsBinary, read.IsBinary.ToString());
        Check("可编辑", read.Editable, read.Editable.ToString());
        Check("hash 非空", read.Hash.Length == 64, read.Hash.Length.ToString());
        Check("权限 0644", read.Mode == "0644", read.Mode);

        // 拿旧 hash 再保存 —— 应通过
        guarded.Save(filePath, "line1\nline2\nline3\n", read.Hash, false, "tester");
        Check("hash 一致时保存成功", guarded.Read(filePath).LineCount == 3, "ok");

        // 模拟他人改动磁盘文件后，用旧 hash 覆盖 —— 应被拒绝
        var staleHash = guarded.Read(filePath).Hash;
        File.AppendAllText(filePath, "someone-else\n");
        Throws("他人改过磁盘后用旧 hash 保存被拒", () => guarded.Save(filePath, "clobber\n", staleHash, false, "tester"), "已被其他进程修改");

        Section("4. 备份与原子写");
        var beforeBackup = Directory.GetFiles(sandbox, "*.bak").Length;
        var fresh = guarded.Read(filePath);
        guarded.Save(filePath, "backed-up\n", fresh.Hash, createBackup: true, "tester");
        Check(".bak 已生成", Directory.GetFiles(sandbox, "*.bak").Length == beforeBackup + 1, "ok");
        Check("无残留 .tmp", Directory.GetFiles(sandbox, "*.sbwrite.tmp").Length == 0, "ok");

        Section("5. 二进制识别");
        var binPath = Path.Combine(sandbox, "blob.bin");
        File.WriteAllBytes(binPath, [0x00, 0x01, 0x02, 0x00, 0xFF]);
        var bin = guarded.Read(binPath);
        Check("含 NUL 判为二进制", bin.IsBinary, bin.IsBinary.ToString());

        var utf8Path = Path.Combine(sandbox, "中文.txt");
        File.WriteAllText(utf8Path, "中文内容\n第二行\n", new System.Text.UTF8Encoding(false));
        var cn = guarded.Read(utf8Path);
        Check("中文 UTF-8 正常解码", cn.Content.Contains("中文内容"), cn.Content);
        Check("CRLF 检测（先写 CRLF 文件）", true, "skip");

        var crlfPath = Path.Combine(sandbox, "crlf.ini");
        File.WriteAllText(crlfPath, "a=1\r\nb=2\r\n");
        Check("CRLF 被识别", guarded.Read(crlfPath).LineEnding == "CRLF", guarded.Read(crlfPath).LineEnding);

        Section("6. 目录操作");
        var subDir = Path.Combine(sandbox, "sub");
        guarded.Create(subDir, true, "tester");
        Check("新建目录权限 0755", Directory.Exists(subDir) && (int)File.GetUnixFileMode(subDir) == Convert.ToInt32("755", 8), "ok");
        guarded.Create(Path.Combine(subDir, "a.txt"), false, "tester");
        Throws("非空目录不递归删除被拒", () => guarded.Delete(subDir, false, "tester"), "非空");
        var del = guarded.Delete(subDir, true, "tester");
        Check("递归删除成功", !Directory.Exists(subDir), del.Message);

        var renamed = guarded.Rename(filePath, "renamed.conf", "tester");
        Check("重命名成功", File.Exists(Path.Combine(sandbox, "renamed.conf")), renamed.Message);
        Throws("重命名含分隔符被拒", () => guarded.Rename(Path.Combine(sandbox, "renamed.conf"), "a/b", "tester"), "分隔符");

        Section("7. chmod");
        var target = Path.Combine(sandbox, "renamed.conf");
        guarded.Chmod(target, "600", "tester");
        Check("chmod 600 生效", guarded.Read(target).Mode == "0600", guarded.Read(target).Mode);
        Throws("非法权限格式被拒", () => guarded.Chmod(target, "999", "tester"), "八进制");
        Throws("超范围权限（5位）被格式校验拒绝", () => guarded.Chmod(target, "07777", "tester"), "八进制");
        guarded.Chmod(target, "7777", "tester");
        Check("chmod 7777 生效", guarded.Read(target).Mode == "7777", guarded.Read(target).Mode);
        guarded.Chmod(target, "444", "tester");
        Throws("无写位(0444)文件保存被拒", () =>
        {
            var r = guarded.Read(target);
            return guarded.Save(target, "x", r.Hash, false, "tester");
        }, "只读");
        guarded.Chmod(target, "644", "tester");

        Section("8. 软链接逃逸");
        var linkPath = Path.Combine(sandbox, "escape");
        Directory.CreateSymbolicLink(linkPath, outside);
        Throws("软链指向白名单外被拒", () => guarded.List(linkPath), "允许范围之外");

        var secret = Path.Combine(outside, "secret.txt");
        File.WriteAllText(secret, "top-secret");
        var fileLink = Path.Combine(sandbox, "passwd-link");
        File.CreateSymbolicLink(fileLink, secret);
        Throws("文件软链指向外部被拒", () => guarded.Read(fileLink), "允许范围之外");

        Section("9. 上传");
        var uploadDir = Path.Combine(sandbox, "uploads");
        guarded.Create(uploadDir, true, "tester");
        var payload = System.Text.Encoding.UTF8.GetBytes("uploaded-content");
        var formFile = new Microsoft.AspNetCore.Http.FormFile(
            new MemoryStream(payload), 0, payload.Length, "FormFile", "up.txt");
        var up = await guarded.UploadAsync(uploadDir, formFile, false, "tester", CancellationToken.None);
        Check("上传成功", File.Exists(Path.Combine(uploadDir, "up.txt")), up.Message);
        Check("上传内容一致", File.ReadAllText(Path.Combine(uploadDir, "up.txt")) == "uploaded-content", "ok");
        Check("上传后无 .tmp 残留", Directory.GetFiles(uploadDir, "*.sbupload.tmp").Length == 0, "ok");

        Section("10. 磁盘用量 / 根列表");
        var disk = guarded.DiskUsage(sandbox);
        Check("磁盘总量 > 0", disk.TotalBytes > 0, $"{disk.TotalBytes / 1024 / 1024 / 1024} GB, 已用 {disk.UsedPercent}%");
        Check("白名单根列表", guarded.Roots().Count == 1, guarded.Roots()[0].Path);

        Section("11. 审计日志");
        var auditPath = Path.Combine(AppContext.BaseDirectory, "logs", "file-manager-audit.log");
        Check("审计日志已写入", File.Exists(auditPath), auditPath);
        if (File.Exists(auditPath))
        {
            var lines = File.ReadAllLines(auditPath);
            Check("审计日志含 write 记录", lines.Any(l => l.Contains("\twrite\t")), $"{lines.Length} 行");
            Check("审计日志含 chmod 记录", lines.Any(l => l.Contains("\tchmod\t")), "ok");
            Check("审计日志含操作人", lines.Any(l => l.Contains("\ttester\t")), "ok");
        }

        Section("12. 大文件拒绝编辑");
        var bigSvc = BuildService(sandbox, [sandbox], [], maxEditMB: 0);
        var bigRead = bigSvc.Read(utf8Path);
        Check("超限标记 TooLarge", bigRead.TooLarge, bigRead.TooLarge.ToString());
        Throws("二进制文件读取仍可返回标记（不抛）", () => bigSvc.Read(Path.Combine(sandbox, "blob.bin")).IsBinary ? "ok" : throw new Exception("应判为二进制"));

        Section("13. Content-Disposition 中文名");
        var cd = FileManagerService.BuildContentDisposition("中文 文件名.conf");
        Check("含 ASCII 兜底 filename", cd.Contains("filename=\""), cd);
        Check("含 RFC5987 filename*", cd.Contains("filename*=UTF-8''"), cd);
        Check("中文已 percent-encode", cd.Contains("%E4%B8%AD%E6%96%87"), cd);
    }

    // ==================== 工具 ====================

    private static FileManagerService BuildService(string defaultRoot, string[] allowedRoots,
        string[] denied, int maxEditMB = 5)
    {
        var options = new FileManagerOptions
        {
            AllowedRoots = allowedRoots,
            DeniedPaths = denied,
            MaxEditFileSizeMB = maxEditMB,
            MaxUploadSizeMB = 8,
            HideDotFiles = false,
            AuditLogEnabled = true,
            AuditLogPath = "logs/file-manager-audit.log",
            NormalizePermissions = true,
        };
        var guard = new PathGuard(options, NullLogger<PathGuard>.Instance);
        var env = new FakeHostEnvironment();
        return new FileManagerService(guard, options, env, NullLogger<FileManagerService>.Instance);
    }

    private sealed class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "UnitTest";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title} ---");
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"  ✓ {name}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"  ✗ {name}  (实际: {detail})");
        }
    }

    private static void Throws<T>(string name, Func<T> action, string expectedFragment = "")
    {
        try
        {
            action();
            _fail++;
            Console.WriteLine($"  ✗ {name}  (未抛异常)");
        }
        catch (Exception ex) when (string.IsNullOrEmpty(expectedFragment) || ex.Message.Contains(expectedFragment))
        {
            _pass++;
            Console.WriteLine($"  ✓ {name}  → {ex.Message}");
        }
        catch (Exception ex)
        {
            _fail++;
            Console.WriteLine($"  ✗ {name}  (异常信息不符: {ex.Message})");
        }
    }

    private static void SafeDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }
}

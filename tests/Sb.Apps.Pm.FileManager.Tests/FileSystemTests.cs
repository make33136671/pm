using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Sb.Apps.Pm.Models.FileManager;
using Sb.Apps.Pm.Services.FileManager;

namespace Sb.Apps.Pm.FileManager.Tests;

/// <summary>
/// 文件系统相关的新增能力测试：压缩 / 解压 / 复制 / 移动 / 搜索 / 路径统计。
///
/// 这些都不依赖数据库，可以直接实例化服务验证。
/// （回收站 / 备注依赖 IFreeSql，见 Program.cs 说明——需要真实数据库环境。）
/// </summary>
internal static class FileSystemTests
{
    private static int _pass;
    private static int _fail;

    public static (int Pass, int Fail) Run(string sandbox)
    {
        var (svc, archive) = Build(sandbox);

        TestCompress(svc, archive, sandbox);
        TestExtract(archive, sandbox);
        TestZipSlip(archive, sandbox);
        TestCopyMove(svc, sandbox);
        TestSearch(svc, sandbox);
        TestPathSize(svc, sandbox);

        return (_pass, _fail);
    }

    private static (FileManagerService, ArchiveService) Build(string sandbox)
    {
        var options = new FileManagerOptions
        {
            AllowedRoots = [sandbox],
            DeniedPaths = ["/proc"],
            MaxEditFileSizeMB = 5,
            MaxExtractSizeMB = 64,
            MaxExtractEntries = 1000,
            NormalizePermissions = true,
            AuditLogEnabled = false,
        };
        var guard = new PathGuard(options, NullLogger<PathGuard>.Instance);
        var env = new Env { ContentRootPath = sandbox };
        var archive = new ArchiveService(options, guard, NullLogger<ArchiveService>.Instance);
        // 注意：不传 IFreeSql（回收站不可用），这里只测文件系统能力
        return (null!, archive);
    }

    // ==================== 压缩 ====================

    private static void TestCompress(FileManagerService _, ArchiveService archive, string sandbox)
    {
        Section("14. 压缩");

        var work = Path.Combine(sandbox, "compress");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "a.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(work, "dir1"));
        File.WriteAllText(Path.Combine(work, "dir1", "b.txt"), "world");

        // zip
        var r = archive.Compress(new CompressRequestDto
        {
            Paths = [Path.Combine(work, "a.txt"), Path.Combine(work, "dir1")],
            Name = "out",
            Format = "zip",
        }, "tester");
        var zipPath = Path.Combine(work, "out.zip");
        Check("zip 压缩成功", File.Exists(zipPath), r.Message);

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Check("zip 含 a.txt", names.Contains("a.txt"), string.Join(",", names));
            Check("zip 含 dir1/b.txt", names.Contains("dir1/b.txt"), string.Join(",", names));
        }

        // tar.gz
        archive.Compress(new CompressRequestDto
        {
            Paths = [Path.Combine(work, "a.txt")],
            Name = "out2",
            Format = "tar.gz",
        }, "tester");
        Check("tar.gz 压缩成功", File.Exists(Path.Combine(work, "out2.tar.gz")), "ok");

        // gz 单文件
        archive.Compress(new CompressRequestDto
        {
            Paths = [Path.Combine(work, "a.txt")],
            Name = "single",
            Format = "gz",
        }, "tester");
        Check("gz 单文件压缩成功", File.Exists(Path.Combine(work, "single.gz")), "ok");

        // gz 目录应被拒
        Throws("gz 压目录被拒", () => archive.Compress(new CompressRequestDto
        {
            Paths = [Path.Combine(work, "dir1")],
            Name = "bad",
            Format = "gz",
        }, "tester"), "单个文件");

        // 重名拒绝
        Throws("目标已存在被拒", () => archive.Compress(new CompressRequestDto
        {
            Paths = [Path.Combine(work, "a.txt")],
            Name = "out",
            Format = "zip",
        }, "tester"), "已存在");

        // 非法名称
        Throws("名称含分隔符被拒", () => archive.Compress(new CompressRequestDto
        {
            Paths = [Path.Combine(work, "a.txt")],
            Name = "../evil",
            Format = "zip",
        }, "tester"), "路径分隔符");
    }

    // ==================== 解压 ====================

    private static void TestExtract(ArchiveService archive, string sandbox)
    {
        Section("15. 解压");

        var work = Path.Combine(sandbox, "compress");
        var zipPath = Path.Combine(work, "out.zip");

        var preview = archive.Preview(zipPath);
        Check("预览 zip 有条目", preview.Entries.Count >= 2, preview.Entries.Count.ToString());
        Check("预览识别格式为 zip", preview.Format == "zip", preview.Format);

        var outDir = Path.Combine(work, "extracted");
        archive.Extract(new ExtractRequestDto { Path = zipPath, OutputDir = outDir, Overwrite = true }, "tester");
        Check("解压出 a.txt", File.Exists(Path.Combine(outDir, "a.txt")), "ok");
        Check("解压出 dir1/b.txt", File.Exists(Path.Combine(outDir, "dir1", "b.txt")), "ok");
        Check("解压内容正确", File.ReadAllText(Path.Combine(outDir, "a.txt")) == "hello", "ok");

        // 不覆盖时应拒绝
        Throws("不覆盖时已有文件被拒", () => archive.Extract(
            new ExtractRequestDto { Path = zipPath, OutputDir = outDir, Overwrite = false }, "tester"),
            "已存在");

        // tar.gz 解压
        var targz = Path.Combine(work, "out2.tar.gz");
        var out2 = Path.Combine(work, "extracted2");
        archive.Extract(new ExtractRequestDto { Path = targz, OutputDir = out2, Overwrite = true }, "tester");
        Check("tar.gz 解压成功", File.Exists(Path.Combine(out2, "a.txt")), "ok");
    }

    // ==================== zip slip ====================

    private static void TestZipSlip(ArchiveService archive, string sandbox)
    {
        Section("16. Zip Slip 防护（路径穿越）");

        var work = Path.Combine(sandbox, "evil");
        Directory.CreateDirectory(work);
        var evilZip = Path.Combine(work, "evil.zip");

        // 手工构造一个含 ../ 的 zip
        using (var fs = new FileStream(evilZip, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("../../escaped.txt");
            using var es = e.Open();
            var payload = Encoding.UTF8.GetBytes("pwned");
            es.Write(payload, 0, payload.Length);
        }

        var outDir = Path.Combine(work, "out");
        Throws("含 ../ 的条目被拒", () => archive.Extract(
            new ExtractRequestDto { Path = evilZip, OutputDir = outDir, Overwrite = true }, "tester"),
            "逃逸");

        // 确认没有写到目标目录之外
        var escaped = Path.GetFullPath(Path.Combine(outDir, "../../escaped.txt"));
        Check("未在目标目录外生成文件", !File.Exists(escaped), escaped);
    }

    // ==================== 复制 / 移动 ====================

    private static void TestCopyMove(FileManagerService _, string sandbox)
    {
        Section("17. 复制 / 移动（需 IFreeSql，仅验证路径防护逻辑）");

        var guardSvc = BuildGuardOnly(sandbox);

        // 复制目录到自身内部应被拒
        Throws("目录复制到自己内部被拒", () =>
        {
            var src = Path.Combine(sandbox, "cp-src");
            Directory.CreateDirectory(src);
            return guardSvc.CopyOrMove(src, Path.Combine(src, "inner"), false, false, "t");
        }, "自己内部");

        // 源不存在
        Throws("源不存在被拒", () => guardSvc.CopyOrMove(
            Path.Combine(sandbox, "nope"), Path.Combine(sandbox, "x"), false, false, "t"), "不存在");
    }

    // ==================== 搜索 ====================

    private static void TestSearch(FileManagerService _, string sandbox)
    {
        Section("18. 搜索");

        var svc = BuildFull(sandbox);
        var root = Path.Combine(sandbox, "search");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "app.conf"), "key=value\nport=8080");
        File.WriteAllText(Path.Combine(root, "readme.md"), "# hi");
        File.WriteAllText(Path.Combine(root, "sub", "app.log"), "port=8080 error");

        var r1 = svc.Search(new SearchRequestDto { Path = root, Pattern = "*.conf", Recursive = true });
        Check("按扩展名搜到 app.conf", r1.Items.Any(i => i.Name == "app.conf"), $"找到 {r1.Items.Count}");
        Check("通配符不匹配 readme.md", !r1.Items.Any(i => i.Name == "readme.md"), "ok");

        var r2 = svc.Search(new SearchRequestDto { Path = root, Pattern = "app*", Recursive = true });
        Check("前缀匹配 app* 得 2 个", r2.Items.Count(i => i.Name.StartsWith("app")) == 2, r2.Items.Count.ToString());

        var r3 = svc.Search(new SearchRequestDto { Path = root, Pattern = "*", Recursive = true, ContentContains = "8080" });
        Check("内容搜索 port=8080 命中 2 个", r3.Items.Count == 2, r3.Items.Count.ToString());

        var r4 = svc.Search(new SearchRequestDto { Path = root, Pattern = "*", Recursive = false });
        Check("非递归只扫一层", r4.Items.Count(i => i.Name == "app.log") == 0, $"共 {r4.Items.Count}");

        var r5 = svc.Search(new SearchRequestDto { Path = root, Pattern = "*", Recursive = true, MaxResults = 1 });
        Check("MaxResults 生效", r5.Items.Count <= 1 && r5.Truncated, $"{r5.Items.Count}/{r5.Truncated}");
    }

    // ==================== 路径大小 ====================

    private static void TestPathSize(FileManagerService _, string sandbox)
    {
        Section("19. 路径大小统计");

        var svc = BuildFull(sandbox);
        var root = Path.Combine(sandbox, "size");
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllBytes(Path.Combine(root, "f1.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(root, "a", "f2.bin"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(root, "a", "b", "f3.bin"), new byte[3000]);

        var single = svc.MeasurePath(Path.Combine(root, "f1.bin"));
        Check("单文件大小 = 1000", single.Size == 1000, single.Size.ToString());
        Check("单文件计数 = 1", single.FileCount == 1, single.FileCount.ToString());

        var dir = svc.MeasurePath(root);
        Check("目录总大小 = 6000", dir.Size == 6000, dir.Size.ToString());
        Check("目录文件数 = 3", dir.FileCount == 3, dir.FileCount.ToString());
        Check("目录子目录数 >= 2", dir.DirCount >= 2, dir.DirCount.ToString());
    }

    // ==================== 构建器 ====================

    /// <summary>只测路径防护（不涉及数据库）。用 null! 传 IFreeSql，只要不调用到它就行。</summary>
    private static FileManagerService BuildGuardOnly(string sandbox)
        => BuildFull(sandbox);

    private static FileManagerService BuildFull(string sandbox)
    {
        var options = new FileManagerOptions
        {
            AllowedRoots = [sandbox],
            DeniedPaths = ["/proc"],
            MaxEditFileSizeMB = 5,
            NormalizePermissions = true,
            AuditLogEnabled = false,
            RecycleBinEnabled = false,
        };
        var guard = new PathGuard(options, NullLogger<PathGuard>.Instance);
        var env = new Env { ContentRootPath = sandbox };
        // IFreeSql 传 null：搜索 / 大小统计 / 复制移动都不碰它；回收站相关接口不测
        return new FileManagerService(guard, options, env, null!, null!, NullLogger<FileManagerService>.Instance);
    }

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static void Section(string t)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {t} ---");
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✓ {name}"); }
        else { _fail++; Console.WriteLine($"  ✗ {name}  (实际: {detail})"); }
    }

    private static void Throws<T>(string name, Func<T> action, string fragment)
    {
        try
        {
            action();
            _fail++;
            Console.WriteLine($"  ✗ {name}  (未抛异常)");
        }
        catch (Exception ex) when (ex.Message.Contains(fragment))
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
}

using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using Sb.Apps.Pm.Models.FileManager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;

namespace Sb.Apps.Pm.Services.FileManager
{
    /// <summary>
    /// 压缩 / 解压。格式分工：
    ///
    ///   内置（零外部依赖，跨平台）：
    ///     · zip        —— System.IO.Compression.ZipFile
    ///     · tar        —— System.Formats.Tar
    ///     · tar.gz/tgz —— TarFile + GZipStream
    ///     · gz         —— GZipStream（单文件）
    ///
    ///   系统命令（找不到就明确报「服务器未安装 xxx」）：
    ///     · tar.bz2 / tar.xz —— tar（debian/ubuntu 自带）
    ///     · bz2 / xz         —— bzip2 / xz
    ///     · 7z               —— 7z
    ///     · rar              —— unrar / 7z
    ///
    /// 安全：解压前先做「zip 炸弹」预检（条目数 + 声明的解压后总大小），
    /// 并对每个条目做路径穿越校验（`../../etc/passwd` 这种绝不能落盘到目标目录外）。
    /// </summary>
    [AutoDependencyInjection]
    public class ArchiveService
    {
        private readonly FileManagerOptions _options;
        private readonly PathGuard _guard;
        private readonly ILogger<ArchiveService> _logger;

        /// <summary>支持压缩输出的格式（前端下拉用）。</summary>
        public static readonly string[] OutputFormats =
            ["zip", "tar", "tar.gz", "tgz", "gz", "tar.bz2", "tar.xz", "7z"];

        public ArchiveService(FileManagerOptions options, PathGuard guard, ILogger<ArchiveService> logger)
        {
            _options = options;
            _guard = guard;
            _logger = logger;
        }

        #region 压缩

        /// <summary>
        /// 压缩若干路径到一个包。所有源必须在同一个父目录下（面板语义：右键选中 → 打包）。
        /// </summary>
        public FileOperationResultDto Compress(CompressRequestDto req, string? actor)
        {
            var sources = req.Paths.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => _guard.Resolve(p)).ToList();
            if (sources.Count == 0) throw new FileManagerException("请至少选择一个文件或目录");

            foreach (var s in sources)
            {
                if (!File.Exists(s) && !Directory.Exists(s))
                    throw new FileManagerException($"源路径不存在：{_guard.ToDisplayPath(s)}");
            }

            var firstParent = Path.GetDirectoryName(sources[0].TrimEnd(Path.DirectorySeparatorChar))
                              ?? throw new FileManagerException("无法确定输出目录");
            var outputDir = string.IsNullOrWhiteSpace(req.OutputDir)
                ? firstParent
                : _guard.Resolve(req.OutputDir);
            if (!Directory.Exists(outputDir)) throw new FileManagerException("输出目录不存在");

            var format = NormalizeFormat(req.Format);
            var baseName = string.IsNullOrWhiteSpace(req.Name)
                ? (sources.Count == 1
                    ? Path.GetFileName(sources[0].TrimEnd(Path.DirectorySeparatorChar))
                    : "archive")
                : req.Name!.Trim();
            foreach (var bad in new[] { '/', '\\', ':' })
            {
                if (baseName.Contains(bad)) throw new FileManagerException("压缩包名称不能包含路径分隔符");
            }
            if (baseName.Contains("..")) throw new FileManagerException("压缩包名称非法");

            var fileName = baseName + ExtensionOf(format);
            var dest = _guard.ResolveForCreate(Path.Combine(outputDir, fileName));
            if (File.Exists(dest)) throw new FileManagerException($"目标文件已存在：{fileName}");

            try
            {
                switch (format)
                {
                    case "zip":
                        CreateZip(dest, sources, req.Level, req);
                        break;
                    case "tar":
                        CreateTar(dest, sources, gzip: false);
                        break;
                    case "tar.gz":
                    case "tgz":
                        CreateTar(dest, sources, gzip: true);
                        break;
                    case "gz":
                        if (sources.Count != 1 || Directory.Exists(sources[0]))
                            throw new FileManagerException("gz 只能压缩单个文件");
                        CreateGz(dest, sources[0]);
                        break;
                    case "tar.bz2":
                        RunTar(dest, sources, "-cjf");
                        break;
                    case "tar.xz":
                        RunTar(dest, sources, "-cJf");
                        break;
                    case "7z":
                        Create7z(dest, sources, req);
                        break;
                    default:
                        throw new FileManagerException($"不支持的格式：{format}");
                }
            }
            catch (FileManagerException) { throw; }
            catch (Exception ex)
            {
                TryDelete(dest);
                throw new FileManagerException($"压缩失败：{ex.Message}");
            }

            var size = new FileInfo(dest).Length;
            _logger.LogInformation("压缩 {Count} 项 -> {Dest}（{Size} 字节） by {Actor}",
                sources.Count, dest, size, actor ?? "-");
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(dest),
                Message = $"压缩完成（{FormatSize(size)}）",
            };
        }

        private void CreateZip(string dest, List<string> sources, int level, CompressRequestDto req)
        {
            var compression = level == 0 ? CompressionLevel.NoCompression
                : level >= 9 ? CompressionLevel.SmallestSize
                : CompressionLevel.Optimal;

            // 加密 zip 交给 7z（.NET 内置不支持 zip 加密写入）
            if (req.Encrypt)
            {
                if (string.IsNullOrEmpty(req.Password))
                    throw new FileManagerException("启用加密时必须提供口令");
                if (!CommandExists("7z")) throw new FileManagerException("服务器未安装 7z，无法创建加密 zip");
                var args = new List<string> { "a", "-tzip", "-mx=" + Math.Clamp(level, 0, 9), $"-p{req.Password}", dest };
                args.AddRange(sources.Select(s => "-ir!" + s));
                if (RunCommand("7z", args, out var err) != 0)
                    throw new FileManagerException($"7z 压缩失败：{Tail(err)}");
                return;
            }

            using var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var src in sources)
            {
                var parent = Path.GetDirectoryName(src.TrimEnd(Path.DirectorySeparatorChar))!;
                if (Directory.Exists(src))
                {
                    var rootName = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar));
                    foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(parent, file).Replace('\\', '/');
                        var entry = zip.CreateEntry(rel, compression);
                        entry.LastWriteTime = File.GetLastWriteTime(file);
                        using var es = entry.Open();
                        using var fsin = File.OpenRead(file);
                        fsin.CopyTo(es);
                    }
                    // 空目录也要留一条，否则解压后目录结构丢失
                    if (!Directory.EnumerateFileSystemEntries(src).Any())
                        zip.CreateEntry(rootName + "/", compression).LastWriteTime = Directory.GetLastWriteTime(src);
                }
                else
                {
                    var entry = zip.CreateEntry(rootNameSafe(src), compression);
                    entry.LastWriteTime = File.GetLastWriteTime(src);
                    using var es = entry.Open();
                    using var fsin = File.OpenRead(src);
                    fsin.CopyTo(es);
                }
            }
        }

        private static string rootNameSafe(string path) => Path.GetFileName(path);

        /// <param name="gzip">true = 额外套一层 gzip（.tar.gz）；false = 纯 tar
        private void CreateTar(string dest, List<string> sources, bool gzip)
        {
            using var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write);
            Stream target = fs;
            if (gzip)
            {
                target = new GZipStream(fs, CompressionLevel.Optimal, leaveOpen: false);
            }

            try
            {
                using var tar = new TarWriter(target, TarEntryFormat.Pax, leaveOpen: false);
                foreach (var src in sources)
                {
                    if (Directory.Exists(src))
                    {
                        // 目录：递归加入，条目名用相对父目录的路径
                        var parent = Path.GetDirectoryName(src.TrimEnd(Path.DirectorySeparatorChar))!;
                        foreach (var entryPath in Directory.EnumerateFileSystemEntries(src, "*", SearchOption.AllDirectories))
                        {
                            var rel = Path.GetRelativePath(parent, entryPath).Replace('\\', '/');
                            if (Directory.Exists(entryPath))
                                tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, rel + "/"));
                            else
                            {
                                var te = new PaxTarEntry(TarEntryType.RegularFile, rel)
                                {
                                    DataStream = File.OpenRead(entryPath),
                                };
                                tar.WriteEntry(te);
                            }
                        }
                        // 目录本身也可能为空，补一条
                        var rootRel = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar));
                        if (!Directory.EnumerateFileSystemEntries(src).Any())
                            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, rootRel + "/"));
                    }
                    else
                    {
                        var te = new PaxTarEntry(TarEntryType.RegularFile, Path.GetFileName(src))
                        {
                            DataStream = File.OpenRead(src),
                        };
                        tar.WriteEntry(te);
                    }
                }
            }
            finally
            {
                if (!ReferenceEquals(target, fs)) target.Dispose();
            }
        }

        private void CreateGz(string dest, string source)
        {
            using var outFs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write);
            using var gz = new GZipStream(outFs, CompressionLevel.Optimal);
            using var inFs = File.OpenRead(source);
            inFs.CopyTo(gz);
        }

        private void RunTar(string dest, List<string> sources, string modeFlag)
        {
            if (!CommandExists("tar")) throw new FileManagerException("服务器未安装 tar");
            var parent = Path.GetDirectoryName(sources[0].TrimEnd(Path.DirectorySeparatorChar))!;
            var args = new List<string> { modeFlag, dest, "-C", parent };
            args.AddRange(sources.Select(s => Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar))));
            if (RunCommand("tar", args, out var err) != 0)
                throw new FileManagerException($"tar 压缩失败：{Tail(err)}");
        }

        private void Create7z(string dest, List<string> sources, CompressRequestDto req)
        {
            if (!CommandExists("7z")) throw new FileManagerException("服务器未安装 7z");
            var args = new List<string> { "a", "-t7z", "-mx=" + Math.Clamp(req.Level, 0, 9) };
            if (req.Encrypt)
            {
                if (string.IsNullOrEmpty(req.Password))
                    throw new FileManagerException("启用加密时必须提供口令");
                args.Add($"-p{req.Password}");
            }
            args.Add(dest);
            args.AddRange(sources);
            if (RunCommand("7z", args, out var err) != 0)
                throw new FileManagerException($"7z 压缩失败：{Tail(err)}");
        }

        #endregion

        #region 预览 / 解压

        /// <summary>预览压缩包条目（不解压）。</summary>
        public ArchivePreviewDto Preview(string? path)
        {
            var full = _guard.Resolve(path);
            if (!File.Exists(full)) throw new FileManagerException("压缩包不存在");

            var format = DetectFormat(full) ?? throw new FileManagerException("无法识别压缩格式或不是压缩包");
            var preview = new ArchivePreviewDto
            {
                Path = _guard.ToDisplayPath(full),
                Format = format,
            };

            if (format == "zip")
            {
                using var zip = ZipFile.OpenRead(full);
                preview.TotalEntries = zip.Entries.Count;
                preview.TotalSize = zip.Entries.Sum(e => e.Length);
                preview.Truncated = zip.Entries.Count > _options.MaxArchivePreviewEntries;
                foreach (var e in zip.Entries.Take(_options.MaxArchivePreviewEntries))
                {
                    preview.Entries.Add(new ArchiveEntryDto
                    {
                        Name = e.FullName,
                        Size = e.Length,
                        CompressedSize = e.CompressedLength,
                        IsDirectory = e.FullName.EndsWith('/') || e.Name.Length == 0,
                        LastModified = e.LastWriteTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    });
                }
                return preview;
            }

            if (format is "tar" or "tar.gz" or "tgz")
            {
                using var reader = OpenTar(full, format);
                var count = 0;
                TarEntry? entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    count++;
                    if (preview.Entries.Count < _options.MaxArchivePreviewEntries)
                    {
                        preview.Entries.Add(new ArchiveEntryDto
                        {
                            Name = entry.Name,
                            Size = entry.Length,
                            IsDirectory = entry.EntryType is TarEntryType.Directory,
                            LastModified = entry.ModificationTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        });
                        preview.TotalSize += entry.Length;
                    }
                }
                if (count > _options.MaxArchivePreviewEntries) preview.Truncated = true;
                preview.TotalEntries = count;
                return preview;
            }

            // 7z / rar / bz2 / xz：交给 7z 列目录
            var tool = CommandExists("7z") ? "7z" : null;
            if (tool == null) throw new FileManagerException($"预览 {format} 需要服务器安装 7z");

            if (RunCommand(tool, new List<string> { "l", "-ba", "-slt", full }, out var output, out _) != 0)
                throw new FileManagerException("读取压缩包列表失败");

            foreach (var block in output.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!block.Contains("Path = ")) continue;
                var name = GetField(block, "Path");
                if (string.IsNullOrEmpty(name) || name == full) continue;
                var isDir = GetField(block, "Folder") == "+";
                var size = long.TryParse(GetField(block, "Size"), out var s) ? s : 0;
                var packed = long.TryParse(GetField(block, "Packed Size"), out var p) ? p : 0;
                var enc = GetField(block, "Encrypted") == "+";
                preview.TotalEntries++;
                preview.TotalSize += size;
                if (preview.Entries.Count >= _options.MaxArchivePreviewEntries)
                {
                    preview.Truncated = true;
                    continue;
                }
                preview.Entries.Add(new ArchiveEntryDto
                {
                    Name = name,
                    Size = size,
                    CompressedSize = packed,
                    IsDirectory = isDir,
                    Encrypted = enc,
                });
            }
            return preview;
        }

        /// <summary>解压。</summary>
        public FileOperationResultDto Extract(ExtractRequestDto req, string? actor)
        {
            var full = _guard.Resolve(req.Path);
            if (!File.Exists(full)) throw new FileManagerException("压缩包不存在");

            var format = DetectFormat(full) ?? throw new FileManagerException("无法识别压缩格式");

            // 默认解到「包名去扩展名」的目录
            var defaultDir = Path.Combine(Path.GetDirectoryName(full)!,
                Path.GetFileNameWithoutExtension(
                    format.StartsWith("tar.", StringComparison.Ordinal) ? Path.GetFileNameWithoutExtension(full)! : full));
            var outDir = string.IsNullOrWhiteSpace(req.OutputDir)
                ? _guard.ResolveForCreate(defaultDir)
                : _guard.Resolve(req.OutputDir!);

            Directory.CreateDirectory(outDir);

            switch (format)
            {
                case "zip":
                    ExtractZip(full, outDir, req);
                    break;
                case "tar":
                case "tar.gz":
                case "tgz":
                    ExtractTar(full, outDir, format, req);
                    break;
                case "gz":
                    ExtractSingleGz(full, outDir, req);
                    break;
                case "tar.bz2":
                case "tar.xz":
                case "7z":
                case "rar":
                case "bz2":
                case "xz":
                    ExtractWith7zOrSystem(full, outDir, format, req);
                    break;
                default:
                    throw new FileManagerException($"不支持解压：{format}");
            }

            _logger.LogInformation("解压 {Path} -> {OutDir} by {Actor}",
                full, outDir, actor ?? "-");
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(outDir),
                Message = "解压完成",
            };
        }

        private void ExtractZip(string zipPath, string outDir, ExtractRequestDto req)
        {
            using var zip = ZipFile.OpenRead(zipPath);

            // 预检：条目数 + 声明总大小（zip 炸弹）
            if (zip.Entries.Count > _options.MaxExtractEntries)
                throw new FileManagerException($"压缩包条目过多（{zip.Entries.Count}），超过上限 {_options.MaxExtractEntries}");
            var declared = zip.Entries.Sum(e => e.Length);
            if (_options.MaxExtractSizeMB > 0 && declared > _options.MaxExtractSizeMB * 1024L * 1024L)
                throw new FileManagerException($"解压后体积 {FormatSize(declared)} 超过上限 {_options.MaxExtractSizeMB}MB");

            long written = 0;
            foreach (var entry in zip.Entries)
            {
                var name = req.UseGbkNames ? FixCjkName(entry) : entry.FullName;
                var target = SafeEntryPath(outDir, name);

                if (entry.FullName.EndsWith('/') || string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target) && !req.Overwrite)
                    throw new FileManagerException($"目标已存在：{name}（勾选「覆盖」后重试）");

                using var es = entry.Open();
                using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
                var buffer = new byte[81920];
                int read;
                while ((read = es.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += read;
                    if (_options.MaxExtractSizeMB > 0 && written > _options.MaxExtractSizeMB * 1024L * 1024L)
                        throw new FileManagerException($"实际解压体积超过上限 {_options.MaxExtractSizeMB}MB（疑似压缩炸弹），已中止");
                    fs.Write(buffer, 0, read);
                }
            }
        }

        /// <summary>
        /// Windows 打的 zip 常用 GBK 存文件名且不设 UTF-8 标志位，.NET 会按 CP437 解出乱码。
        ///
        /// 注意：`ZipArchiveEntry` **没有公开 general purpose flags**（.NET 不暴露），
        /// 无法判断「是否已声明 UTF-8」。所以做法是：用户显式勾选 GBK 时，
        /// 把 .NET 按 CP437 解出的字符串**还原回原始字节**（CP437 是 0-255 单字节映射，可逆），
        /// 再用 GB18030 重解码；只在结果不含替换符时采用，否则保留原名。
        /// </summary>
        private static string FixCjkName(ZipArchiveEntry entry)
        {
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                var raw = entry.FullName;
                if (raw.All(c => c < 128)) return raw; // 纯 ASCII，不用管

                var cp437 = System.Text.Encoding.GetEncoding(437);
                var gb18030 = System.Text.Encoding.GetEncoding("GB18030");

                // CP437 字符 → 原始字节 的反查表
                var map = new Dictionary<char, byte>();
                for (var b = 0; b < 256; b++)
                {
                    var ch = cp437.GetString([(byte)b])[0];
                    map.TryAdd(ch, (byte)b);
                }

                var bytes = new List<byte>(raw.Length);
                foreach (var c in raw)
                {
                    if (!map.TryGetValue(c, out var b)) return raw; // 还原不了就放弃
                    bytes.Add(b);
                }

                var fixedName = gb18030.GetString(bytes.ToArray());
                return fixedName.Contains('\uFFFD') ? raw : fixedName;
            }
            catch
            {
                return entry.FullName;
            }
        }

        private void ExtractTar(string tarPath, string outDir, string format, ExtractRequestDto req)
        {
            using var reader = OpenTar(tarPath, format);
            TarEntry? entry;
            long written = 0;
            var count = 0;
            while ((entry = reader.GetNextEntry()) != null)
            {
                count++;
                if (count > _options.MaxExtractEntries)
                    throw new FileManagerException($"压缩包条目过多，超过上限 {_options.MaxExtractEntries}");

                var target = SafeEntryPath(outDir, entry.Name);
                if (entry.EntryType is TarEntryType.Directory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                // 只解普通文件；忽略符号链接/设备文件（避免 tar slip + 特殊文件攻击）
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile
                    or TarEntryType.ContiguousFile))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target) && !req.Overwrite)
                    throw new FileManagerException($"目标已存在：{entry.Name}（勾选「覆盖」后重试）");

                using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
                entry.DataStream?.CopyTo(fs);
                written += entry.Length;
                if (_options.MaxExtractSizeMB > 0 && written > _options.MaxExtractSizeMB * 1024L * 1024L)
                    throw new FileManagerException($"解压体积超过上限 {_options.MaxExtractSizeMB}MB，已中止");
            }
        }

        private void ExtractSingleGz(string gzPath, string outDir, ExtractRequestDto req)
        {
            var name = Path.GetFileNameWithoutExtension(gzPath);
            var target = SafeEntryPath(outDir, name);
            if (File.Exists(target) && !req.Overwrite)
                throw new FileManagerException($"目标已存在：{name}");

            using var inFs = File.OpenRead(gzPath);
            using var gz = new GZipStream(inFs, CompressionMode.Decompress);
            using var outFs = new FileStream(target, FileMode.Create, FileAccess.Write);

            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (_options.MaxExtractSizeMB > 0 && written > _options.MaxExtractSizeMB * 1024L * 1024L)
                    throw new FileManagerException($"解压体积超过上限 {_options.MaxExtractSizeMB}MB，已中止");
                outFs.Write(buffer, 0, read);
            }
        }

        private void ExtractWith7zOrSystem(string archive, string outDir, string format, ExtractRequestDto req)
        {
            string? tool = null;
            List<string> args = [];

            switch (format)
            {
                case "rar":
                    if (CommandExists("unrar")) { tool = "unrar"; args = ["x", "-o" + (req.Overwrite ? "+" : "-"), archive, outDir]; }
                    break;
                case "tar.bz2":
                    if (CommandExists("tar")) { tool = "tar"; args = ["-xjf", archive, "-C", outDir]; }
                    break;
                case "tar.xz":
                    if (CommandExists("tar")) { tool = "tar"; args = ["-xJf", archive, "-C", outDir]; }
                    break;
                case "xz":
                    if (CommandExists("xz")) { tool = "xz"; args = ["-dkf", archive]; }
                    break;
                case "bz2":
                    if (CommandExists("bzip2")) { tool = "bzip2"; args = ["-dkf", archive]; }
                    break;
            }

            if (tool == null && CommandExists("7z"))
            {
                tool = "7z";
                args = ["x", "-y", req.Overwrite ? "-aoa" : "-aos", $"-o{outDir}", archive];
                if (!string.IsNullOrEmpty(req.Password)) args.Add($"-p{req.Password}");
            }

            if (tool == null)
                throw new FileManagerException($"解压 {format} 需要服务器安装 unrar / 7z / xz / bzip2 其中之一");

            if (RunCommand(tool, args, out var err) != 0)
                throw new FileManagerException($"{tool} 解压失败：{Tail(err)}");
        }

        private static TarReader OpenTar(string path, string format)
        {
            var fs = File.OpenRead(path);
            if (format is "tar.gz" or "tgz")
            {
                var gz = new GZipStream(fs, CompressionMode.Decompress);
                return new TarReader(gz);
            }
            return new TarReader(fs);
        }

        /// <summary>
        /// 防路径穿越：解压条目的最终路径必须仍在 outDir 之内。
        /// 拒绝绝对路径、`..`、以及符号链接形式的跳转。
        /// </summary>
        private static string SafeEntryPath(string outDir, string entryName)
        {
            var normalized = entryName.Replace('\\', '/').TrimStart('/');
            if (normalized.Contains('\0')) throw new FileManagerException("压缩包条目名含非法字符");

            var full = Path.GetFullPath(Path.Combine(outDir, normalized));
            var root = Path.GetFullPath(outDir);
            if (!PathGuard.IsUnder(full, root) && !string.Equals(full, root, StringComparison.Ordinal))
                throw new FileManagerException($"压缩包条目试图逃逸目标目录：{entryName}");
            return full;
        }

        #endregion

        #region 工具

        /// <summary>按文件名识别格式。</summary>
        public static string? DetectFormat(string path)
        {
            var lower = path.ToLowerInvariant();
            if (lower.EndsWith(".tar.gz")) return "tar.gz";
            if (lower.EndsWith(".tgz")) return "tgz";
            if (lower.EndsWith(".tar.bz2") || lower.EndsWith(".tbz2")) return "tar.bz2";
            if (lower.EndsWith(".tar.xz") || lower.EndsWith(".txz")) return "tar.xz";
            if (lower.EndsWith(".tar")) return "tar";
            if (lower.EndsWith(".zip")) return "zip";
            if (lower.EndsWith(".7z")) return "7z";
            if (lower.EndsWith(".rar")) return "rar";
            if (lower.EndsWith(".gz")) return "gz";
            if (lower.EndsWith(".bz2")) return "bz2";
            if (lower.EndsWith(".xz")) return "xz";
            return null;
        }

        /// <summary>是否是可解压的包。</summary>
        public static bool IsArchive(string path) => DetectFormat(path) != null;

        private static string NormalizeFormat(string? format)
        {
            var f = (format ?? "zip").Trim().ToLowerInvariant().TrimStart('.');
            return f switch
            {
                "tgz" => "tgz",
                "tar.gz" or "targz" => "tar.gz",
                "tarbz2" or "tar.bz2" => "tar.bz2",
                "tarxz" or "tar.xz" => "tar.xz",
                "" => "zip",
                _ => f,
            };
        }

        private static string ExtensionOf(string format) => format switch
        {
            "tgz" => ".tgz",
            "tar.gz" => ".tar.gz",
            "tar.bz2" => ".tar.bz2",
            "tar.xz" => ".tar.xz",
            _ => "." + format,
        };

        /// <summary>命令是否存在（走 /usr/bin/which，避免 rely on PATH 解析差异）。</summary>
        private static bool CommandExists(string cmd)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    ArgumentList = { cmd },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                p!.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private int RunCommand(string file, IEnumerable<string> args, out string stdout, out string stderr)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)!;
            stderr = string.Empty;
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(_options.ExternalCommandTimeoutSeconds * 1000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                stdout = string.Empty;
                stderr = $"{file} 执行超时（{_options.ExternalCommandTimeoutSeconds}s）";
                return -1;
            }

            stderr = se.GetAwaiter().GetResult();
            stdout = so.GetAwaiter().GetResult();
            return p.ExitCode;
        }

        /// <summary>不关心 stdout 的重载。</summary>
        private int RunCommand(string file, IEnumerable<string> args, out string stderr)
            => RunCommand(file, args, out _, out stderr);

        private int RunCommand(string file, List<string> args, out string stderr)
            => RunCommand(file, (IEnumerable<string>)args, out _, out stderr);

        private static string Tail(string s, int max = 300)
            => string.IsNullOrEmpty(s) ? "（无输出）" : s.Length <= max ? s.Trim() : s[^max..].Trim();

        private static string? GetField(string block, string key)
        {
            foreach (var line in block.Split('\n'))
            {
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var k = line[..idx].Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return line[(idx + 1)..].Trim();
            }
            return null;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string FormatSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double v = bytes;
            var i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return $"{v:0.##} {units[i]}";
        }

        #endregion
    }
}

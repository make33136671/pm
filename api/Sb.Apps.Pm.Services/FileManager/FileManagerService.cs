using System.Globalization;
using FreeSql;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sb.Apps.Pm.Models.FileManager;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;

namespace Sb.Apps.Pm.Services.FileManager
{
    /// <summary>
    /// 服务器文件管理器核心服务：列目录 / 读 / 写 / 新建 / 重命名 / 删除 / 上传 / chmod / 磁盘用量。
    ///
    /// **安全边界**：所有路径必须经 <see cref="PathGuard"/> 解析（白名单 + 黑名单 + 链接逃逸校验）。
    /// **审计**：所有写操作追加一行到 logs/file-manager-audit.log（若开启）。
    /// **并发**：保存时携带读取时拿到的内容 Hash，磁盘已变化则拒绝覆盖，避免静默丢改动。
    /// </summary>
    [AutoDependencyInjection]
    public class FileManagerService : IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "文件管理";
        public int InitOrder => 255;   // 紧跟网页终端(250)之后
        public Type[] GetEntityTypes() => [typeof(FileRecycleItem), typeof(FileNote)];
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                ParentPath = "/system",
                Name = "FileManager",
                Path = "/file-manager",
                Component = "/file-manager/index",
                Title = "文件管理",
                Icon = "lucide:folder-open",
                OrderNo = 97,
            },
        };

        #endregion

        private readonly PathGuard _guard;
        private readonly FileManagerOptions _options;
        private readonly ILogger<FileManagerService> _logger;
        private readonly IHostEnvironment _env;
        private readonly RecycleBinService _recycle;
        private readonly IFreeSql _fsql;

        /// <summary>新建文件的默认权限 0644（C# 无八进制字面量，故写十进制并注明）。</summary>
        private const int DefaultFileMode = 420;      // 0644 = 420

        /// <summary>新建目录的默认权限 0755。</summary>
        private const int DefaultDirectoryMode = 493; // 0755 = 493

        /// <summary>07777 = 4095，POSIX 权限位上限。</summary>
        private const int MaxPosixMode = 4095;

        private static readonly string[] TextExtensions =
        [
            ".txt", ".md", ".markdown", ".log", ".ini", ".conf", ".cfg", ".config", ".properties",
            ".json", ".jsonc", ".json5", ".yaml", ".yml", ".toml", ".xml", ".csv", ".tsv",
            ".cs", ".csproj", ".sln", ".props", ".targets", ".razor", ".cshtml", ".fs", ".vb",
            ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".vue", ".svelte", ".html", ".htm",
            ".css", ".scss", ".sass", ".less", ".styl", ".map",
            ".py", ".pyi", ".rb", ".go", ".rs", ".java", ".kt", ".kts", ".scala", ".php", ".pl", ".lua", ".r",
            ".c", ".h", ".cpp", ".hpp", ".cc", ".hh", ".m", ".mm", ".swift", ".dart", ".zig",
            ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1", ".bat", ".cmd", ".service", ".timer",
            ".sql", ".graphql", ".gql", ".proto", ".tf", ".tfvars", ".hcl", ".gradle",
            ".txt", ".text", ".rst", ".adoc", ".tex", ".bib", ".gitignore", ".gitattributes",
            ".dockerfile", ".editorconfig", ".env", ".example", ".sample", ".lock", ".spec", ".patch", ".diff",
        ];

        /// <summary>没有扩展名但通常是文本的文件名（小写）。</summary>
        private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "dockerfile", "makefile", "cmakelists.txt", "rakefile", "gemfile", "procfile",
            "caddyfile", "nginx.conf", "requirements.txt", "pipfile", "license", "readme", "changelog",
            "hosts", "passwd", "shadow", "fstab", "crontab", "authorized_keys", "known_hosts",
        };

        public FileManagerService(
            PathGuard guard,
            FileManagerOptions options,
            IHostEnvironment env,
            RecycleBinService recycle,
            IFreeSql fsql,
            ILogger<FileManagerService> logger)
        {
            _guard = guard;
            _options = options;
            _env = env;
            _recycle = recycle;
            _fsql = fsql;
            _logger = logger;
        }

        #region 列目录

        /// <summary>列举目录内容。</summary>
        public DirectoryListingDto List(string? path, bool showHidden = false)
        {
            var full = _guard.Resolve(path);
            if (!Directory.Exists(full))
            {
                if (File.Exists(full))
                    throw new FileManagerException("目标是文件，不是目录");
                throw new FileManagerException($"目录不存在: {_guard.ToDisplayPath(full)}");
            }

            var di = new DirectoryInfo(full);
            var dto = new DirectoryListingDto
            {
                Path = _guard.ToDisplayPath(full),
                Self = ToEntry(di),
                Breadcrumbs = BuildBreadcrumbs(full),
            };
            dto.ParentPath = di.Parent is null ? string.Empty : _guard.ToDisplayPath(di.Parent.FullName);

            IEnumerable<FileSystemInfo> children;
            try
            {
                var opt = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    AttributesToSkip = 0, // 自己处理隐藏文件，走 Options.HideDotFiles
                };
                children = di.EnumerateFileSystemInfos("*", opt);
            }
            catch (UnauthorizedAccessException)
            {
                throw new FileManagerForbiddenException("没有权限读取该目录");
            }

            var hideDot = _options.HideDotFiles && !showHidden;
            var all = new List<FileSystemInfo>();
            foreach (var child in children)
            {
                if (hideDot && child.Name.StartsWith('.')) continue;
                all.Add(child);
            }

            dto.Total = all.Count;
            dto.Truncated = all.Count > _options.MaxListEntries;

            var entries = all.Take(_options.MaxListEntries).Select(ToEntry).ToList();
            dto.Directories = entries.Where(e => e.IsDirectory)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            dto.Files = entries.Where(e => !e.IsDirectory)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return dto;
        }

        /// <summary>目录树（只返回子目录，供左侧树展开）。</summary>
        public List<FileEntryDto> ListDirectories(string? path)
        {
            var full = _guard.Resolve(path);
            if (!Directory.Exists(full))
                throw new FileManagerException("目录不存在");

            var di = new DirectoryInfo(full);
            var result = new List<FileEntryDto>();
            try
            {
                var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
                foreach (var child in di.EnumerateDirectories("*", opt))
                {
                    if (_options.HideDotFiles && child.Name.StartsWith('.')) continue;
                    if (IsSymlinkLoop(child)) continue;
                    result.Add(ToEntry(child));
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw new FileManagerForbiddenException("没有权限读取该目录");
            }
            return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>可浏览的根目录列表。</summary>
        public List<BreadcrumbDto> Roots()
        {
            var roots = _guard.AllowedRoots;
            if (roots.Count == 0) return [new BreadcrumbDto { Name = "/", Path = "/" }];
            return roots.Select(r => new BreadcrumbDto
            {
                Name = r == "/" ? "/" : Path.GetFileName(r.TrimEnd('/')),
                Path = r,
            }).ToList();
        }

        private List<BreadcrumbDto> BuildBreadcrumbs(string full)
        {
            var crumbs = new List<BreadcrumbDto>();
            var root = _guard.AllowedRoots.FirstOrDefault(r => PathGuard.IsUnder(full, r));

            if (string.IsNullOrEmpty(root) || root == "/")
            {
                // 全盘：从 / 开始
                var parts = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var acc = "/";
                crumbs.Add(new BreadcrumbDto { Name = "/", Path = "/" });
                foreach (var p in parts)
                {
                    acc = acc == "/" ? "/" + p : acc + "/" + p;
                    crumbs.Add(new BreadcrumbDto { Name = p, Path = acc });
                }
                return crumbs;
            }

            crumbs.Add(new BreadcrumbDto
            {
                Name = Path.GetFileName(root.TrimEnd('/')) is { Length: > 0 } n ? n : root,
                Path = root,
            });
            var rel = full.Length > root.Length ? full[root.Length..] : string.Empty;
            var segs = rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var cur = root;
            foreach (var s in segs)
            {
                cur = Path.Combine(cur, s);
                crumbs.Add(new BreadcrumbDto { Name = s, Path = _guard.ToDisplayPath(cur) });
            }
            return crumbs;
        }

        #endregion

        #region 读文件

        /// <summary>读取文本内容（编辑用）。</summary>
        public FileContentDto Read(string? path)
        {
            var full = _guard.Resolve(path);
            if (Directory.Exists(full)) throw new FileManagerException("目标是目录，不能作为文本打开");
            if (!File.Exists(full)) throw new FileManagerException($"文件不存在: {_guard.ToDisplayPath(full)}");

            // 优先读 Length：FileInfo 的属性缓存以首次访问的属性为基准，
            // 先读 Length 才能保证 Size / LastWriteTime 都是实时值（见 Read 下行注释）
            var fi = new FileInfo(full);
            var currentSize = fi.Length;
            var dto = new FileContentDto
            {
                Path = _guard.ToDisplayPath(full),
                Name = fi.Name,
                Size = currentSize,
                LastModified = Utc(fi.LastWriteTimeUtc),
                Mode = GetMode(full),
            };

            if (currentSize > _options.MaxEditFileSizeMB * 1024L * 1024L)
            {
                dto.TooLarge = true;
                dto.LineEnding = "NONE";
                return dto;
            }

            byte[] bytes;
            try
            {
                // 共享读：不阻塞其他进程（日志文件正在被写也能打开）
                using var fs = new FileStream(full, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                throw new FileManagerForbiddenException("没有权限读取该文件");
            }

            dto.Size = bytes.LongLength;
            dto.Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            if (IsBinary(bytes, out _) && !IsKnownTextName(fi.Name))
            {
                dto.IsBinary = true;
                dto.LineEnding = "NONE";
                return dto;
            }

            var text = DecodeText(bytes);
            dto.Content = text;
            dto.LineEnding = DetectLineEnding(text);
            dto.LineCount = CountLines(text);
            dto.Editable = !IsReadOnly(full);
            return dto;
        }

        /// <summary>打开下载流。调用方负责 Dispose。</summary>
        public (Stream Stream, long Length, string FileName) OpenDownload(string? path)
        {
            var full = _guard.Resolve(path);
            if (Directory.Exists(full)) throw new FileManagerException("目标是目录，请打包后再下载");
            if (!File.Exists(full)) throw new FileManagerException("文件不存在");

            // FileInfo(path) 缓存只在 FirstAccess 首次访问时刷新；先读 Length 拿到实时大小
            var fi = new FileInfo(full);
            var length = fi.Length;
            var name = fi.Name;
            var fs = new FileStream(full, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: false);
            return (fs, length, name);
        }

        #endregion

        #region 写文件

        /// <summary>
        /// 保存文本内容。
        /// </summary>
        /// <param name="expectedHash">读取时拿到的 Hash；非空时若磁盘已变化则拒绝覆盖。</param>
        /// <param name="createBackup">true = 覆盖前把原文件备份为 .bak。</param>
        public FileOperationResultDto Save(string? path, string content, string? expectedHash,
            bool createBackup, string? actor)
        {
            var full = _guard.Resolve(path);
            if (Directory.Exists(full)) throw new FileManagerException("目标是目录，不能写入");

            var exists = File.Exists(full);
            if (exists)
            {
                // 只认 POSIX 写位：FileInfo.IsReadOnly 在 Linux 上即使 0644 也可能返回 true
                // （它映射的是 FileAttributes.ReadOnly，.NET 对新建文件不会自动清掉），不可靠。
                if (IsReadOnly(full)) throw new FileManagerException("文件是只读的（没有写权限位），请先调整权限");

                if (!string.IsNullOrEmpty(expectedHash))
                {
                    byte[] old;
                    using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        old = ms.ToArray();
                    }
                    var curHash = Convert.ToHexString(SHA256.HashData(old)).ToLowerInvariant();
                    if (!string.Equals(curHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                        throw new FileManagerException("文件在此期间已被其他进程修改，请重新加载后再保存");
                }

                if (createBackup)
                {
                    var backup = full + ".bak";
                    File.Copy(full, backup, overwrite: true);
                }
            }

            // 原子写：先写临时文件再替换，避免写一半进程被杀导致文件损坏
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                throw new FileManagerException("父目录不存在");

            var tmp = full + ".sbwrite.tmp";
            try
            {
                var oldMode = exists ? GetModeInt(full) : 0;
                File.WriteAllText(tmp, content, new UTF8Encoding(false));
                if (!exists && _options.NormalizePermissions)
                    TryChmod(tmp, DefaultFileMode);
                else if (exists && oldMode != 0)
                    TryChmod(tmp, oldMode);

                if (!exists && OperatingSystem.IsLinux() && TryGetOwner(full, out var uid, out var gid))
                    TryChown(tmp, uid, gid);

                File.Move(tmp, full, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp)) TryDelete(tmp);
            }

            Audit(actor, exists ? "write" : "create-file", full, $"{content.Length} 字符");
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(full),
                Message = exists ? "保存成功" : "创建成功",
            };
        }

        /// <summary>新建空文件或目录。</summary>
        public FileOperationResultDto Create(string? path, bool isDirectory, string? actor)
        {
            var full = _guard.ResolveForCreate(path);
            if (File.Exists(full) || Directory.Exists(full))
                throw new FileManagerException("同名文件或目录已存在");

            var dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                throw new FileManagerException("父目录不存在");

            if (isDirectory)
            {
                Directory.CreateDirectory(full);
                if (_options.NormalizePermissions) TryChmod(full, DefaultDirectoryMode);
            }
            else
            {
                using (File.Create(full)) { }
                if (_options.NormalizePermissions) TryChmod(full, DefaultFileMode);
            }
            if (OperatingSystem.IsLinux() && TryGetOwner(dir, out var uid, out var gid))
                TryChown(full, uid, gid);

            Audit(actor, isDirectory ? "mkdir" : "touch", full, null);
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(full),
                Message = isDirectory ? "目录已创建" : "文件已创建",
            };
        }

        /// <summary>重命名 / 移动（同白名单内）。</summary>
        public FileOperationResultDto Rename(string? path, string newName, string? actor)
        {
            var full = _guard.Resolve(path);
            if (!File.Exists(full) && !Directory.Exists(full))
                throw new FileManagerException("源文件或目录不存在");

            if (string.IsNullOrWhiteSpace(newName))
                throw new FileManagerException("新名称不能为空");
            var clean = newName.Trim();
            if (clean.Contains('/') || clean.Contains('\\') || clean is "." or "..")
                throw new FileManagerException("新名称不能包含路径分隔符");

            var parent = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(parent)) throw new FileManagerException("无法确定父目录");

            var target = Path.Combine(parent, clean);
            if (string.Equals(target, full, StringComparison.Ordinal))
                return new FileOperationResultDto { Path = _guard.ToDisplayPath(full), Message = "名称未变化" };
            if (File.Exists(target) || Directory.Exists(target))
                throw new FileManagerException("目标名称已存在");

            // 目标路径同样要过白名单（父目录已校验，这里再确认一次）
            _guard.ResolveForCreate(target);

            if (Directory.Exists(full)) Directory.Move(full, target);
            else File.Move(full, target);

            Audit(actor, "rename", full, Path.GetFileName(target));
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(target),
                Message = "重命名成功",
            };
        }

        /// <summary>
        /// 删除文件或目录（目录递归）。
        ///
        /// **回收站开启时 = 移入回收站（可恢复）；关闭时 = 立即永久删除。**
        /// 返回消息里会体现是哪一种，避免用户误以为还能找回。
        /// </summary>
        public FileOperationResultDto Delete(string? path, bool recursive, string? actor)
        {
            var full = _guard.Resolve(path);
            var isDir = Directory.Exists(full);
            var isFile = File.Exists(full);
            if (!isDir && !isFile) throw new FileManagerException("文件或目录不存在");

            // 禁止删除白名单根本身
            if (isDir && _guard.AllowedRoots.Any(r => string.Equals(r, full, StringComparison.Ordinal)))
                throw new FileManagerException("不能删除根目录");

            if (isDir)
            {
                var di = new DirectoryInfo(full);
                var hasChildren = di.EnumerateFileSystemInfos().Any();
                if (hasChildren && !recursive)
                    throw new FileManagerException("目录非空，请确认递归删除");
            }

            // 回收站不可用时（配置关闭 / 未装配）优雅降级为直接删除，不能空引用崩溃
            if (_recycle is { Enabled: true })
            {
                var size = isDir ? MeasureSize(full).Size : new FileInfo(full).Length;
                var row = _recycle.MoveToRecycleBin(full, actor ?? "-", isDir, size);
                Audit(actor, isDir ? "rmdir->recycle" : "rm->recycle", full, size.ToString());
                return new FileOperationResultDto
                {
                    Path = _guard.ToDisplayPath(full),
                    Message = $"已移入回收站（可恢复，{FormatSize(size)}）",
                };
            }

            if (isDir) SafeDeleteDirectory(full);
            else File.Delete(full);

            Audit(actor, isDir ? "rmdir" : "rm", full, "permanent");
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(full),
                Message = "已永久删除（回收站已关闭，不可恢复）",
            };
        }

        /// <summary>修改权限位（八进制字符串，如 "644" / "0755"）。</summary>
        public FileOperationResultDto Chmod(string? path, string mode, string? actor)
        {
            if (!_options.AllowChmod)
                throw new FileManagerException("当前配置已禁用 chmod");

            var full = _guard.Resolve(path);
            if (!File.Exists(full) && !Directory.Exists(full))
                throw new FileManagerException("文件或目录不存在");

            var trimmed = mode.Trim();
            if (trimmed.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[2..];
            // 八进制只允许 0-7；"999" 虽是数字，Convert.ToInt32(,8) 却会抛
            // "Could not find any recognizable digits"，所以先按位校验再转换。
            if (trimmed.Length is < 3 or > 4 || !trimmed.All(c => c is >= '0' and <= '7'))
                throw new FileManagerException("权限格式应为八进制，如 644 / 0755");

            var value = Convert.ToInt32(trimmed, 8);
            if (value < 0 || value > MaxPosixMode)
                throw new FileManagerException("权限超出范围");

            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                throw new FileManagerException("当前操作系统不支持 POSIX 权限修改");
            if (!TryChmod(full, value))
                throw new FileManagerException("chmod 调用失败（可能没有属主权限）");

            Audit(actor, "chmod", full, trimmed);
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(full),
                Message = $"权限已改为 {Convert.ToString(value, 8)}",
            };
        }

        /// <summary>上传文件到指定目录（同名覆盖）。</summary>
        public async Task<FileOperationResultDto> UploadAsync(string? dirPath, IFormFile file,
            bool overwrite, string? actor, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                throw new FileManagerException("上传文件为空");

            var maxBytes = _options.MaxUploadSizeMB * 1024L * 1024L;
            if (file.Length > maxBytes)
                throw new FileManagerException($"文件超过 {_options.MaxUploadSizeMB}MB 上限");

            var dirFull = _guard.Resolve(dirPath);
            if (!Directory.Exists(dirFull))
                throw new FileManagerException("目标目录不存在");

            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name))
                throw new FileManagerException("文件名无效");
            if (name.Contains('/') || name.Contains('\\') || name is "." or "..")
                throw new FileManagerException("文件名包含非法字符");

            var target = _guard.ResolveForCreate(Path.Combine(dirFull, name));
            if (File.Exists(target) && !overwrite)
                throw new FileManagerException("同名文件已存在");

            var tmp = target + ".sbupload.tmp";
            try
            {
                await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                 FileShare.None, 64 * 1024, useAsync: true))
                {
                    await file.CopyToAsync(fs, ct);
                }
                if (_options.NormalizePermissions) TryChmod(tmp, DefaultFileMode);
                if (OperatingSystem.IsLinux() && TryGetOwner(dirFull, out var uid, out var gid))
                    TryChown(tmp, uid, gid);
                File.Move(tmp, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp)) TryDelete(tmp);
            }

            Audit(actor, "upload", target, $"{file.Length} bytes");
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(target),
                Message = $"上传成功（{FormatSize(file.Length)}）",
            };
        }

        /// <summary>磁盘用量。</summary>
        public DiskUsageDto DiskUsage(string? path)
        {
            var full = _guard.Resolve(path);
            if (File.Exists(full)) full = Path.GetDirectoryName(full) ?? full;
            var drive = new DriveInfo(Path.GetPathRoot(full) ?? "/");
            var total = drive.TotalSize;
            var free = drive.AvailableFreeSpace;
            return new DiskUsageDto
            {
                Path = _guard.ToDisplayPath(full),
                TotalBytes = total,
                FreeBytes = free,
                UsedBytes = total - free,
                UsedPercent = total > 0 ? Math.Round((total - free) * 100.0 / total, 1) : 0,
            };
        }

        /// <summary>
        /// 生成 Content-Disposition（含中文 / 特殊字符文件名兼容）：
        /// 同时给 ASCII 兜底名与 RFC 5987 的 filename*=UTF-8''，各浏览器都能正确落名。
        /// </summary>
        public static string BuildContentDisposition(string fileName)
        {
            var ascii = new string(fileName.Select(c => c < 128 && c != '"' && c != '\\' ? c : '_').ToArray());
            var encoded = Uri.EscapeDataString(fileName);
            return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{encoded}";
        }

        #endregion

        #region 复制 / 移动

        /// <summary>
        /// 复制或移动文件/目录。
        /// </summary>
        /// <param name="move">true = 移动，false = 复制。</param>
        public FileOperationResultDto CopyOrMove(string? source, string? target, bool move, bool overwrite, string? actor)
        {
            var src = _guard.Resolve(source);
            if (!File.Exists(src) && !Directory.Exists(src))
                throw new FileManagerException("源文件或目录不存在");

            var isDir = Directory.Exists(src);
            var dst = _guard.ResolveForCreate(target);

            // 目标已存在：目录保持原样，文件按 overwrite 决定
            if (Directory.Exists(dst))
            {
                dst = Path.Combine(dst, Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar)));
                _guard.ResolveForCreate(dst);
            }

            if (string.Equals(src.TrimEnd('/'), dst.TrimEnd('/'), StringComparison.Ordinal))
                throw new FileManagerException("源和目标相同");

            // 防递归：目录不能复制/移动到自己内部
            if (isDir && PathGuard.IsUnder(dst, src))
                throw new FileManagerException("不能把目录复制或移动到自己内部");

            if ((File.Exists(dst) || Directory.Exists(dst)) && !overwrite)
                throw new FileManagerException("目标已存在，请勾选覆盖或改名");

            var parent = Path.GetDirectoryName(dst);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                throw new FileManagerException("目标目录不存在");

            try
            {
                if (move)
                {
                    // 先试原子 move（同盘），跨盘时退化
                    try
                    {
                        if (isDir) Directory.Move(src, dst);
                        else File.Move(src, dst, overwrite);
                    }
                    catch (IOException)
                    {
                        if (isDir) CopyDirectory(src, dst);
                        else File.Copy(src, dst, overwrite);
                        if (isDir) SafeDeleteDirectory(src);
                        else File.Delete(src);
                    }
                }
                else
                {
                    if (isDir) CopyDirectory(src, dst);
                    else File.Copy(src, dst, overwrite);
                }
            }
            catch (FileManagerException) { throw; }
            catch (Exception ex) { throw new FileManagerException($"{(move ? "移动" : "复制")}失败：{ex.Message}"); }

            Audit(actor, move ? "mv" : "cp", src, _guard.ToDisplayPath(dst));
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(dst),
                Message = move ? "移动成功" : "复制成功",
            };
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
            }
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dst, Path.GetRelativePath(src, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to, overwrite: true);
            }
        }

        #endregion

        #region 批量操作

        /// <summary>批量删除。单项失败不影响其它项，返回逐项结果。</summary>
        public BatchResultDto BatchDelete(List<string> paths, bool recursive, string? actor)
            => RunBatch(paths, p => Delete(p, recursive, actor)?.Message ?? "ok");

        /// <summary>批量复制 / 移动。</summary>
        public BatchResultDto BatchCopyMove(List<string> paths, string targetDir, bool move, bool overwrite, string? actor)
        {
            var dir = _guard.Resolve(targetDir);
            if (!Directory.Exists(dir)) throw new FileManagerException("目标目录不存在");
            return RunBatch(paths, p =>
            {
                var name = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar));
                return CopyOrMove(p, Path.Combine(dir, name), move, overwrite, actor).Message;
            });
        }

        /// <summary>批量 chmod。</summary>
        public BatchResultDto BatchChmod(List<string> paths, string mode, string? actor)
            => RunBatch(paths, p => Chmod(p, mode, actor).Message);

        private BatchResultDto RunBatch(List<string> paths, Func<string, string> action)
        {
            var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
            if (list.Count == 0) throw new FileManagerException("请至少选择一个条目");
            if (list.Count > _options.MaxBatchItems)
                throw new FileManagerException($"单次批量操作不能超过 {_options.MaxBatchItems} 项");

            var result = new BatchResultDto { Total = list.Count };
            foreach (var p in list)
            {
                try
                {
                    var msg = action(p);
                    result.Succeeded++;
                    result.Items.Add(new BatchItemResultDto { Path = p, Success = true, Message = msg });
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Items.Add(new BatchItemResultDto { Path = p, Success = false, Message = ex.Message });
                }
            }
            return result;
        }

        #endregion

        #region 搜索 / 统计

        /// <summary>
        /// 按文件名（或内容）搜索。有扫描上限，避免全盘搜索把进程拖死。
        /// </summary>
        public SearchResultDto Search(SearchRequestDto req)
        {
            var start = _guard.Resolve(req.Path);
            if (!Directory.Exists(start)) throw new FileManagerException("搜索起点不是目录");

            var maxResults = Math.Clamp(req.MaxResults <= 0 ? _options.MaxSearchResults : req.MaxResults, 1, 5000);
            var maxScan = Math.Clamp(req.MaxScan <= 0 ? _options.MaxSearchScan : req.MaxScan, 1, 1_000_000);

            var regex = GlobToRegex(req.Pattern);
            var hasContent = !string.IsNullOrWhiteSpace(req.ContentContains);
            var result = new SearchResultDto();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var opt = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = req.Recursive,
                AttributesToSkip = 0,
                MaxRecursionDepth = req.Recursive ? 32 : 0,
            };

            try
            {
                foreach (var entry in new DirectoryInfo(start).EnumerateFileSystemInfos("*", opt))
                {
                    if (++result.Scanned > maxScan) { result.Truncated = true; break; }

                    if (_options.HideDotFiles && entry.Name.StartsWith('.')) continue;
                    if (!regex.IsMatch(entry.Name)) continue;

                    if (hasContent)
                    {
                        if (entry is not FileInfo fi) continue;
                        if (fi.Length > 2L * 1024 * 1024) continue; // 内容搜索只读小文件
                        if (!IsEditableByExtension(Path.GetExtension(fi.Name).TrimStart('.').ToLowerInvariant(), fi.Name)) continue;
                        try
                        {
                            var text = File.ReadAllText(fi.FullName);
                            if (!text.Contains(req.ContentContains!, StringComparison.OrdinalIgnoreCase)) continue;
                        }
                        catch { continue; }
                    }

                    result.Items.Add(ToEntry(entry));
                    if (result.Items.Count >= maxResults) { result.Truncated = true; break; }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "搜索过程中断: {Path}", start);
                result.Truncated = true;
            }

            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }

        /// <summary>统计路径大小（目录递归）。用于「属性」面板。</summary>
        public PathSizeDto MeasurePath(string? path, int maxEntries = 2_000_000)
        {
            var full = _guard.Resolve(path);
            var dto = new PathSizeDto { Path = _guard.ToDisplayPath(full) };

            if (File.Exists(full))
            {
                dto.Size = new FileInfo(full).Length;
                dto.FileCount = 1;
                return dto;
            }
            if (!Directory.Exists(full)) throw new FileManagerException("路径不存在");

            var m = MeasureSize(full, maxEntries);
            dto.Size = m.Size;
            dto.FileCount = m.Files;
            dto.DirCount = m.Dirs;
            dto.Truncated = m.Truncated;
            return dto;
        }

        private (long Size, long Files, long Dirs, bool Truncated) MeasureSize(string dir, int maxEntries = 2_000_000)
        {
            long size = 0, files = 0, dirs = 0;
            try
            {
                foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*",
                             new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 }))
                {
                    if (files + dirs >= maxEntries) return (size, files, dirs, true);
                    if (e is FileInfo f) { size += f.Length; files++; }
                    else dirs++;
                }
            }
            catch { /* 权限不足的地方跳过 */ }
            return (size, files, dirs, false);
        }

        /// <summary>通配符转正则（* → .*，? → .，其余转义）。无通配符时按子串匹配。</summary>
        private static System.Text.RegularExpressions.Regex GlobToRegex(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = "*";
            if (!pattern.Contains('*') && !pattern.Contains('?')) pattern = "*" + pattern + "*";
            var sb = new System.Text.StringBuilder("^");
            foreach (var c in pattern)
            {
                sb.Append(c switch
                {
                    '*' => ".*",
                    '?' => ".",
                    _ => System.Text.RegularExpressions.Regex.Escape(c.ToString()),
                });
            }
            sb.Append('$');
            return new System.Text.RegularExpressions.Regex(sb.ToString(),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(2));
        }

        #endregion

        #region 属主 / 备注

        /// <summary>修改属主 / 属组。</summary>
        public FileOperationResultDto Chown(string? path, string owner, string? group, bool recursive, string? actor)
        {
            if (!_options.AllowChown) throw new FileManagerException("当前配置已禁用 chown");
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                throw new FileManagerException("当前操作系统不支持 chown");

            var full = _guard.Resolve(path);
            if (!File.Exists(full) && !Directory.Exists(full)) throw new FileManagerException("文件或目录不存在");

            var users = ListSystemUsers();
            var user = users.FirstOrDefault(u => u.Name == owner || u.Uid.ToString() == owner)
                       ?? throw new FileManagerException($"用户不存在：{owner}");
            var gid = user.Gid;
            if (!string.IsNullOrWhiteSpace(group))
            {
                var g = ListSystemUsers().FirstOrDefault(u => u.Name == group || u.Uid.ToString() == group);
                gid = g?.Gid ?? gid;
            }

            var count = 0;
            void Apply(string p)
            {
                if (chown(p, (uint)user.Uid, (uint)gid) == 0) count++;
                else _logger.LogDebug("chown 失败: {Path}", p);
            }

            Apply(full);
            if (recursive && Directory.Exists(full))
            {
                foreach (var e in new DirectoryInfo(full).EnumerateFileSystemInfos("*",
                             new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 }))
                {
                    Apply(e.FullName);
                }
            }

            Audit(actor, "chown", full, $"{user.Name}:{gid}{(recursive ? " -R" : "")}");
            return new FileOperationResultDto
            {
                Path = _guard.ToDisplayPath(full),
                Message = $"属主已改为 {user.Name}:{gid}（{count} 项）",
            };
        }

        /// <summary>备注功能依赖数据库，这里统一兜住。</summary>
        private void EnsureDb()
        {
            if (_fsql == null)
                throw new FileManagerException("备注功能需要数据库支持，当前环境未初始化");
        }

        /// <summary>列出系统用户（读 /etc/passwd）。</summary>
        public List<SystemUserDto> ListSystemUsers()
        {
            var list = new List<SystemUserDto>();
            if (!File.Exists("/etc/passwd")) return list;
            try
            {
                foreach (var line in File.ReadLines("/etc/passwd"))
                {
                    var parts = line.Split(':');
                    if (parts.Length < 4) continue;
                    if (!int.TryParse(parts[2], out var uid)) continue;
                    if (!int.TryParse(parts[3], out var gid)) continue;
                    list.Add(new SystemUserDto { Name = parts[0], Uid = uid, Gid = gid });
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "读取 /etc/passwd 失败"); }
            return list;
        }

        /// <summary>读取某路径的备注 / 收藏状态。<br/>备注依赖数据库，未装配 IFreeSql 时抛业务异常而不是空引用。</summary>
        public FileNoteDto GetNote(string? path)
        {
            EnsureDb();
            var full = _guard.Resolve(path);
            var row = _fsql.Select<FileNote>().Where(n => n.Path == full).First();
            return new FileNoteDto
            {
                Path = _guard.ToDisplayPath(full),
                Note = row?.Note ?? string.Empty,
                Favorite = row?.Favorite ?? false,
                UpdatedTime = row == null ? string.Empty
                    : DateTime.SpecifyKind(row.UpdatedTime, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
        }

        /// <summary>保存备注 / 收藏。两者都空则删除这一行。</summary>
        public FileNoteDto SaveNote(string? path, string? note, bool favorite, string? actor)
        {
            EnsureDb();
            var full = _guard.Resolve(path);
            var row = _fsql.Select<FileNote>().Where(n => n.Path == full).First();

            if (string.IsNullOrWhiteSpace(note) && !favorite)
            {
                if (row != null) _fsql.Delete<FileNote>(row.Id).ExecuteAffrows();
                return new FileNoteDto { Path = _guard.ToDisplayPath(full) };
            }

            if (row == null)
            {
                row = new FileNote { Path = full, Note = note, Favorite = favorite, CreatedBy = actor ?? "-", UpdatedTime = AppTime.UtcNow };
                _fsql.Insert(row).ExecuteAffrows();
            }
            else
            {
                _fsql.Update<FileNote>(row.Id)
                    .Set(n => n.Note, note)
                    .Set(n => n.Favorite, favorite)
                    .Set(n => n.UpdatedTime, AppTime.UtcNow)
                    .ExecuteAffrows();
            }

            return new FileNoteDto
            {
                Path = _guard.ToDisplayPath(full),
                Note = note ?? string.Empty,
                Favorite = favorite,
                UpdatedTime = AppTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
        }

        /// <summary>收藏列表（前端快捷入口用）。</summary>
        public List<FileNoteDto> ListFavorites()
        {
            EnsureDb();
            return _fsql.Select<FileNote>().Where(n => n.Favorite)
                .OrderByDescending(n => n.UpdatedTime)
                .ToList()
                .Select(n => new FileNoteDto
                {
                    Path = n.Path,
                    Note = n.Note ?? string.Empty,
                    Favorite = true,
                    UpdatedTime = DateTime.SpecifyKind(n.UpdatedTime, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                }).ToList();
        }

        #endregion

        #region 内部工具

        private FileEntryDto ToEntry(FileSystemInfo info)
        {
            var isDir = info is DirectoryInfo;
            var isLink = info.LinkTarget is not null;
            long size = 0;
            DateTime mtime = default, atime = default;
            string mode = "-", owner = "-", group = "-";
            try
            {
                size = isDir ? 0 : ((FileInfo)info).Length;
                mtime = info.LastWriteTimeUtc;
                atime = info.LastAccessTimeUtc;
                mode = GetMode(info.FullName);
                if (OperatingSystem.IsLinux() && TryGetOwner(info.FullName, out var uid, out var gid))
                {
                    owner = uid.ToString(CultureInfo.InvariantCulture);
                    group = gid.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "读取文件元数据失败: {Path}", info.FullName);
            }

            var ext = isDir ? string.Empty : Path.GetExtension(info.Name).TrimStart('.').ToLowerInvariant();
            return new FileEntryDto
            {
                Name = info.Name,
                Path = _guard.ToDisplayPath(info.FullName),
                IsDirectory = isDir,
                IsSymlink = isLink,
                Size = size,
                LastModified = mtime == default ? string.Empty : Utc(mtime),
                LastAccessed = atime == default ? string.Empty : Utc(atime),
                Mode = mode,
                Owner = owner,
                Group = group,
                Extension = ext,
                Editable = !isDir && IsEditableByExtension(ext, info.Name) && size <= _options.MaxEditFileSizeMB * 1024L * 1024L,
            };
        }

        private bool IsEditableByExtension(string ext, string name)
        {
            if (!string.IsNullOrEmpty(ext) && TextExtensions.Contains("." + ext)) return true;
            return TextFileNames.Contains(name);
        }

        private bool IsKnownTextName(string name)
            => IsEditableByExtension(Path.GetExtension(name).TrimStart('.').ToLowerInvariant(), name);

        private static bool IsSymlinkLoop(DirectoryInfo dir)
        {
            try { return dir.LinkTarget is not null && dir.ResolveLinkTarget(true) is not DirectoryInfo; }
            catch { return true; }
        }

        /// <summary>二进制嗅探：出现 NUL 字节即判定为二进制。</summary>
        private bool IsBinary(byte[] bytes, out int nulIndex)
        {
            var limit = Math.Min(bytes.Length, _options.BinarySniffBytes);
            for (var i = 0; i < limit; i++)
            {
                if (bytes[i] == 0) { nulIndex = i; return true; }
            }
            nulIndex = -1;
            return false;
        }

        /// <summary>UTF-8 优先解码，非法序列回退 GB18030（中文老文件常见）。</summary>
        private static string DecodeText(byte[] bytes)
        {
            var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            try
            {
                var text = utf8Strict.GetString(bytes);
                return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    return Encoding.GetEncoding("GB18030").GetString(bytes);
                }
                catch
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }

        private static string DetectLineEnding(string text)
        {
            if (text.Length == 0) return "NONE";
            var crlf = 0;
            var lf = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                if (i > 0 && text[i - 1] == '\r') crlf++;
                else lf++;
            }
            if (crlf > 0 && lf > 0) return "MIXED";
            if (crlf > 0) return "CRLF";
            return lf > 0 ? "LF" : "NONE";
        }

        private static int CountLines(string text)
        {
            if (text.Length == 0) return 0;
            var n = 0;
            foreach (var c in text) if (c == '\n') n++;
            return text.EndsWith('\n') ? n : n + 1;
        }

        private static bool IsReadOnly(string full)
        {
            // 注意：不要用 FileInfo.IsReadOnly —— Linux 上它对 0644 文件也可能为 true。
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return false;
            try
            {
                var mode = File.GetUnixFileMode(full);
                // 只读 = 没有任何写位
                const UnixFileMode writeBits =
                    UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
                return (mode & writeBits) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string GetMode(string full)
        {
            try
            {
                if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return "-";
                var value = (int)File.GetUnixFileMode(full);
                // 统一成 4 位八进制：0644 / 0755 / 1777 / 7777（与 ls -l 看到的 st_mode 对齐）
                return Convert.ToString(value, 8).PadLeft(4, '0');
            }
            catch
            {
                return "-";
            }
        }

        private static int GetModeInt(string full)
        {
            try
            {
                if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return 0;
                return (int)File.GetUnixFileMode(full);
            }
            catch
            {
                return 0;
            }
        }

        private bool TryChmod(string full, int mode)
        {
            try
            {
                if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return false;
                File.SetUnixFileMode(full, (UnixFileMode)mode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "chmod 失败: {Path}", full);
                return false;
            }
        }

        /// <summary>新建文件时沿用父目录属主（Linux 用 libc chown，其它平台返回 false）。</summary>
        private bool TryGetOwner(string path, out int uid, out int gid)
        {
            uid = gid = -1;
            if (!OperatingSystem.IsLinux()) return false;
            try
            {
                var buf = new byte[StatBufferSize];
                if (stat_native(path, buf) != 0) return false;
                uid = BitConverter.ToInt32(buf, StatUidOffset);
                gid = BitConverter.ToInt32(buf, StatGidOffset);
                return uid >= 0 && gid >= 0;
            }
            catch
            {
                return false;
            }
        }

        private void TryChown(string path, int uid, int gid)
        {
            if (!OperatingSystem.IsLinux() || uid < 0) return;
            try { chown(path, (uint)uid, (uint)gid); }
            catch (Exception ex) { _logger.LogDebug(ex, "chown 失败: {Path}", path); }
        }

        private static void SafeDeleteDirectory(string full)
        {
            // 先递归删掉只读文件，避免 Directory.Delete 因权限失败
            foreach (var f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* 忽略 */ }
            }
            Directory.Delete(full, recursive: true);
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { /* 清理失败不影响主流程 */ }
        }

        private static string FormatSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double v = bytes;
            var i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return $"{v:0.##} {units[i]}";
        }

        /// <summary>统一输出带 Z 后缀的 UTC ISO8601（前端按 appTimeZone 转本地显示）。</summary>
        private static string Utc(DateTime value) =>
            DateTime.SpecifyKind(value, DateTimeKind.Utc)
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        /// <summary>追加审计日志。失败只记 Warning，不影响业务成功。</summary>
        private void Audit(string? actor, string action, string fullPath, string? detail)
        {
            if (!_options.AuditLogEnabled) return;
            try
            {
                var logPath = Path.IsPathRooted(_options.AuditLogPath)
                    ? _options.AuditLogPath
                    : Path.Combine(_env.ContentRootPath, _options.AuditLogPath);
                var dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var line = string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-ddTHH:mm:ssZ}\t{1}\t{2}\t{3}\t{4}\n",
                    AppTime.UtcNow, actor ?? "-", action, fullPath,
                    (detail ?? "-").Replace('\n', ' ').Replace('\t', ' '));
                File.AppendAllText(logPath, line, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写文件管理审计日志失败（不影响业务）");
            }
        }

        #endregion

        #region libc 互操作

        /// <summary>
        /// Linux struct stat 的**前 40 字节**在各架构上一致：
        ///   st_dev(8) st_ino(8) st_nlink(8) st_mode(4) st_uid(4) st_gid(4) + 4 字节 padding。
        ///
        /// 注意：inode/u32 在不同 ABI 下宽度不同（x86_64 的 st_nlink 是 u64，arm64/riscv 是 u32），
        /// 所以**不能**按某个 arch 逐字段声明结构体——那样会读错偏移甚至越界。
        /// 这里用 144 字节的不透明缓冲区（glibc 各架构 sizeof(struct stat) ≤ 144），
        /// 只按固定偏移（user 24 / group 28）读 uid/gid，绝对安全。
        /// </summary>
        private const int StatBufferSize = 144;
        private const int StatUidOffset = 24;
        private const int StatGidOffset = 28;

        [DllImport("libc", SetLastError = true, EntryPoint = "stat")]
        private static extern int stat_native(string path, byte[] buffer);

        [DllImport("libc", SetLastError = true)]
        private static extern int chown(string path, uint owner, uint group);

        #endregion
    }
}

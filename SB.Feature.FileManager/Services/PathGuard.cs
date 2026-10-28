using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SB.Feature.FileManager.Services
{
    /// <summary>
    /// 路径安全解析器 —— 本插件唯一允许把「用户输入」变成「磁盘路径」的地方。
    ///
    /// 三层防护：
    ///   1. 规范化：Path.GetFullPath 消掉 ../，拒绝 NUL 字符；
    ///   2. 白名单：AllowedRoots 为空 = 全盘（与 web shell 对等）；非空 = 必须落在某个根内；
    ///   3. 黑名单：DeniedPaths 前缀一律拒绝（默认 /proc /sys /dev）。
    ///
    /// 另外对**已解析符号链接**做二次校验（ResolveLinkTarget），
    /// 防止 ln -s /etc/shadow /opt/app/innocent 这种「链接逃逸」绕过白名单。
    /// </summary>
    public sealed class PathGuard
    {
        private readonly FileManagerOptions _options;
        private readonly ILogger<PathGuard> _logger;

        public PathGuard(IOptions<FileManagerOptions> options, ILogger<PathGuard> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>配置的根目录列表（空 = 全盘）。</summary>
        public IReadOnlyList<string> AllowedRoots =>
            _options.AllowedRoots.Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(NormalizeRoot)
                .ToList();

        /// <summary>是否全盘开放。</summary>
        public bool IsWholeDisk => AllowedRoots.Count == 0;

        private static string NormalizeRoot(string root)
        {
            var full = Path.GetFullPath(root);
            // 根目录保留末尾分隔符（"/" 不能变成空串）
            return full.Length > 1 ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
        }

        /// <summary>
        /// 把用户输入解析为磁盘绝对路径，越界则抛 <see cref="FileManagerForbiddenException"/>。
        /// </summary>
        /// <param name="input">用户输入的路径（可为空 = 默认根）。</param>
        public string Resolve(string? input)
        {
            var raw = string.IsNullOrWhiteSpace(input) ? DefaultRoot : input!;

            // 1. 拒绝 NUL（.NET 也会拒，但先给出清晰错误）
            if (raw.Contains('\0'))
                throw new FileManagerForbiddenException("路径包含非法字符");

            // 2. 相对路径统一挂到默认根下，避免依赖进程 CWD（容器里可能是 /app）
            if (!Path.IsPathRooted(raw))
                raw = Path.Combine(DefaultRoot, raw);

            string full;
            try
            {
                full = Path.GetFullPath(raw);
            }
            catch (Exception ex)
            {
                throw new FileManagerForbiddenException($"非法路径: {ex.Message}");
            }

            // 3. 黑名单优先
            foreach (var denied in _options.DeniedPaths)
            {
                if (string.IsNullOrWhiteSpace(denied)) continue;
                if (IsUnder(full, NormalizeRoot(denied)))
                    throw new FileManagerForbiddenException($"该路径已被禁止访问: {denied}");
            }

            // 4. 白名单
            if (!IsWholeDisk)
            {
                var hit = AllowedRoots.Any(root => IsUnder(full, root));
                if (!hit)
                    throw new FileManagerForbiddenException("该路径不在允许访问的目录范围内");
            }

            // 5. 符号链接逃逸校验：只对已存在的路径做（新建文件的父目录单独校验）
            CheckSymlinkEscape(full);

            return full;
        }

        /// <summary>新建场景：解析目标路径，若不存在则校验其父目录的链接逃逸。</summary>
        public string ResolveForCreate(string? input)
        {
            var full = Resolve(input);
            if (File.Exists(full) || Directory.Exists(full))
                return full;

            var parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent))
                CheckSymlinkEscape(parent);
            return full;
        }

        private void CheckSymlinkEscape(string full)
        {
            if (IsWholeDisk && _options.DeniedPaths.Length == 0) return;

            string? current = full;
            // 逐级向上找到第一个存在的路径，解析其真实目标
            while (!string.IsNullOrEmpty(current) && !File.Exists(current) && !Directory.Exists(current))
            {
                current = Path.GetDirectoryName(current);
            }
            if (string.IsNullOrEmpty(current)) return;

            string real;
            try
            {
                var info = Directory.Exists(current)
                    ? (FileSystemInfo)new DirectoryInfo(current)
                    : new FileInfo(current);
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                real = target?.FullName ?? current;
            }
            catch (Exception ex)
            {
                // 无法解析时保守放行（路径本身已在白名单内），仅记录
                _logger.LogDebug(ex, "解析符号链接失败: {Path}", current);
                return;
            }

            foreach (var denied in _options.DeniedPaths)
            {
                if (!string.IsNullOrWhiteSpace(denied) && IsUnder(real, NormalizeRoot(denied)))
                    throw new FileManagerForbiddenException($"符号链接指向禁止目录: {denied}");
            }
            if (!IsWholeDisk && !AllowedRoots.Any(root => IsUnder(real, root)))
                throw new FileManagerForbiddenException("符号链接指向允许范围之外的目录");
        }

        /// <summary>默认根：第一个白名单根，否则当前运行用户的家目录，再否则 /。</summary>
        public string DefaultRoot
        {
            get
            {
                var roots = AllowedRoots;
                if (roots.Count > 0) return roots[0];
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return string.IsNullOrEmpty(home) ? "/" : home;
            }
        }

        /// <summary>b 是否等于 a 或位于 a 之下（按路径段边界判断，避免 /opt/ap 命中 /opt/app）。</summary>
        public static bool IsUnder(string b, string a)
        {
            if (string.IsNullOrEmpty(a)) return false;
            if (a == "/") return b.StartsWith('/');
            if (string.Equals(b, a, StringComparison.Ordinal)) return true;
            var prefix = a.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return b.StartsWith(prefix, StringComparison.Ordinal);
        }

        /// <summary>相对给定根的显示路径（根为 "/" 时返回完整路径）。</summary>
        public string ToDisplayPath(string full)
        {
            var roots = AllowedRoots;
            if (roots.Count == 0) return full;
            foreach (var root in roots)
            {
                if (root == "/") return full;
                if (IsUnder(full, root))
                {
                    var rel = full[root.Length..].TrimStart(Path.DirectorySeparatorChar);
                    return string.IsNullOrEmpty(rel) ? "/" : "/" + rel.Replace(Path.DirectorySeparatorChar, '/');
                }
            }
            return full;
        }
    }

    /// <summary>路径越界 / 被禁止。Controller 捕获后返回 Result.Fail。</summary>
    public sealed class FileManagerForbiddenException(string message) : Exception(message);

    /// <summary>业务校验失败（文件不存在 / 已存在 / 是二进制等）。</summary>
    public sealed class FileManagerException(string message) : Exception(message);
}

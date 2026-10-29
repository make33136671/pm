using System.Diagnostics;
using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// 工作目录管理服务 — 管理服务器上的代码工作副本
    /// 类似程序员本地的工程目录，AI 在此读写代码
    /// </summary>
    [AutoDependencyInjection]
    public class WorkspaceService
    {
        private readonly IFreeSql _fsql;
        private readonly SystemSettingService _settingService;
        private readonly ILogger<WorkspaceService> _logger;

        public WorkspaceService(IFreeSql fsql, SystemSettingService settingService, ILogger<WorkspaceService> logger)
        {
            _fsql = fsql;
            _settingService = settingService;
            _logger = logger;
        }

        /// <summary>
        /// 获取子项目的本地工作路径
        /// 始终从系统设置 workspace_root 重新拼接 — sp.LocalPath 只作缓存,不作决策依据,
        /// 避免历史脏数据(相对路径、过期值)绕过 workspace_root 校验
        /// </summary>
        public string GetLocalPath(int subProjectId)
        {
            var sp = _fsql.Select<SubProject>().Where(s => s.Id == subProjectId).First();
            if (sp == null) throw new ArgumentException($"子项目 {subProjectId} 不存在");

            var workspaceRoot = _settingService.GetSystemSettingByKey<string>("workspace_root");
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new InvalidOperationException("请在「系统设置 → 全局配置 → 项目操作根目录」配置 workspace_root");
            if (!Path.IsPathRooted(workspaceRoot))
                throw new InvalidOperationException($"workspace_root 必须是绝对路径,当前值: \"{workspaceRoot}\"。请改为 /data/pm-workspace 之类的绝对路径");

            var project = _fsql.Select<Project>().Where(p => p.Id == sp.ProjectId).First();
            var projectSeg = ResolveProjectSegment(project, sp.ProjectId);
            var subSeg = ResolveSubProjectSegment(sp);

            return Path.Combine(workspaceRoot, projectSeg, subSeg);
        }

        /// <summary>
        /// 获取主项目本地目录。
        /// 若项目设置了 LocalPath（绝对路径），直接返回；否则从 workspace_root 拼接。
        /// </summary>
        public string GetProjectLocalPath(int projectId)
        {
            var project = _fsql.Select<Project>().Where(p => p.Id == projectId).First();

            if (project != null && !string.IsNullOrWhiteSpace(project.LocalPath) && Path.IsPathRooted(project.LocalPath))
                return project.LocalPath;

            var workspaceRoot = _settingService.GetSystemSettingByKey<string>("workspace_root");
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new InvalidOperationException("请在「系统设置 → 全局配置 → 项目操作根目录」配置 workspace_root");
            if (!Path.IsPathRooted(workspaceRoot))
                throw new InvalidOperationException($"workspace_root 必须是绝对路径,当前值: \"{workspaceRoot}\"");

            var seg = ResolveProjectSegment(project, projectId);
            return Path.Combine(workspaceRoot, seg);
        }

        /// <summary>
        /// 主项目路径段: Code → sanitize(Name) → project_{Id}
        /// </summary>
        private static string ResolveProjectSegment(Project? project, int projectId)
        {
            if (project != null)
            {
                if (!string.IsNullOrWhiteSpace(project.Code)) return project.Code;
                var nameSlug = SanitizePathSegment(project.Name);
                if (!string.IsNullOrEmpty(nameSlug)) return nameSlug;
            }
            return $"project_{projectId}";
        }

        /// <summary>
        /// 子项目路径段: Code → GitlabProjectPath 最后一段 → sanitize(Name) → sub_{Id}
        /// </summary>
        private static string ResolveSubProjectSegment(SubProject sp)
        {
            if (!string.IsNullOrWhiteSpace(sp.Code)) return sp.Code;

            if (!string.IsNullOrWhiteSpace(sp.GitlabProjectPath))
            {
                var lastSeg = sp.GitlabProjectPath.TrimEnd('/').Split('/').Last();
                if (!string.IsNullOrWhiteSpace(lastSeg)) return lastSeg;
            }

            var nameSlug = SanitizePathSegment(sp.Name);
            if (!string.IsNullOrEmpty(nameSlug)) return nameSlug;

            return $"sub_{sp.Id}";
        }

        /// <summary>
        /// 把任意字符串清洗成合法路径段: 替换路径非法字符为 -,去除前后空白/点,合并连续 -
        /// 保留 Unicode 字符(中文等),Linux/macOS/Windows 文件系统都接受 UTF-8 路径
        /// </summary>
        private static readonly char[] _invalidPathChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };
        private static string SanitizePathSegment(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s.Trim())
            {
                if (c < 32) continue;
                sb.Append(Array.IndexOf(_invalidPathChars, c) >= 0 ? '-' : c);
            }
            var result = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-");
            return result.Trim('-', ' ', '.');
        }

        /// <summary>
        /// 确保工作目录存在（不存在则 clone，已存在则 pull 更新）
        /// </summary>
        public async Task<string> EnsureWorkspace(int subProjectId)
        {
            var sp = _fsql.Select<SubProject>().Where(s => s.Id == subProjectId).First();
            if (sp == null) throw new ArgumentException($"子项目 {subProjectId} 不存在");

            var localPath = GetLocalPath(subProjectId);

            // 平滑迁移: 数据库 LocalPath 跟当前算出的不一致(命名规则变更/Code 改名等),
            // 且旧路径下存在 .git,直接 rename 过去,避免重新 clone + 丢失未提交改动
            if (!string.IsNullOrWhiteSpace(sp.LocalPath) && sp.LocalPath != localPath
                && Directory.Exists(Path.Combine(sp.LocalPath, ".git"))
                && !Directory.Exists(localPath))
            {
                try
                {
                    var parentDir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        Directory.CreateDirectory(parentDir);
                    Directory.Move(sp.LocalPath, localPath);
                    _logger.LogInformation("工作目录已迁移: {Old} → {New}", sp.LocalPath, localPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "工作目录迁移失败,将在新路径重新 clone: {Old} → {New}", sp.LocalPath, localPath);
                }
            }

            if (Directory.Exists(Path.Combine(localPath, ".git")))
            {
                // 已存在，执行 pull 更新
                _logger.LogInformation("工作目录已存在，执行 git pull: {Path}", localPath);
                await RunGitAsync(localPath, "pull --ff-only");
            }
            else
            {
                // 不存在，执行 clone
                if (string.IsNullOrEmpty(sp.GitlabProjectUrl))
                    throw new InvalidOperationException($"子项目 {sp.Name} 未配置 GitLab 仓库地址");

                // 确保父目录存在
                var parentDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    Directory.CreateDirectory(parentDir);

                // 构建带认证的 clone URL
                var cloneUrl = BuildAuthUrl(sp.GitlabProjectUrl);
                _logger.LogInformation("克隆仓库: {Url} → {Path}", sp.GitlabProjectUrl, localPath);
                await RunGitAsync(parentDir!, $"clone {cloneUrl} {Path.GetFileName(localPath)}");
            }

            // 更新子项目的 LocalPath
            if (sp.LocalPath != localPath)
            {
                _fsql.Update<SubProject>().Where(s => s.Id == subProjectId)
                    .Set(s => s.LocalPath, localPath).ExecuteAffrows();
            }

            return localPath;
        }

        /// <summary>
        /// 创建新分支
        /// </summary>
        public async Task CreateBranch(string localPath, string branchName, string? fromRef = null)
        {
            if (!string.IsNullOrEmpty(fromRef))
                await RunGitAsync(localPath, $"checkout -b {branchName} {fromRef}");
            else
                await RunGitAsync(localPath, $"checkout -b {branchName}");
        }

        /// <summary>
        /// 切换分支
        /// </summary>
        public async Task CheckoutBranch(string localPath, string branchName)
        {
            await RunGitAsync(localPath, $"checkout {branchName}");
        }

        /// <summary>
        /// 提交并推送
        /// </summary>
        public async Task CommitAndPush(string localPath, string message, string? branch = null)
        {
            await RunGitAsync(localPath, "add -A");
            await RunGitAsync(localPath, $"commit -m \"{message.Replace("\"", "\\\"")}\"");

            if (!string.IsNullOrEmpty(branch))
                await RunGitAsync(localPath, $"push -u origin {branch}");
            else
                await RunGitAsync(localPath, "push");
        }

        /// <summary>
        /// 获取当前分支名
        /// </summary>
        public async Task<string> GetCurrentBranch(string localPath)
        {
            return (await RunGitAsync(localPath, "rev-parse --abbrev-ref HEAD")).Trim();
        }

        /// <summary>
        /// 获取最新提交信息
        /// </summary>
        public async Task<string> GetLatestCommit(string localPath)
        {
            return (await RunGitAsync(localPath, "log -1 --oneline")).Trim();
        }

        /// <summary>
        /// 获取本地仓库的 Git 状态摘要
        /// </summary>
        public async Task<object?> GetGitStatus(int subProjectId)
        {
            var sp = _fsql.Select<SubProject>().Where(s => s.Id == subProjectId).First();
            if (sp == null || string.IsNullOrEmpty(sp.LocalPath)) return null;
            if (!Directory.Exists(Path.Combine(sp.LocalPath, ".git"))) return null;

            var localPath = sp.LocalPath;
            var branch = (await RunGitAsync(localPath, "rev-parse --abbrev-ref HEAD")).Trim();
            var commit = (await RunGitAsync(localPath, "log -1 --format=%h|%s|%an|%ai")).Trim();
            var parts = commit.Split('|', 4);

            // 未提交变更统计 (git status --porcelain)
            int modified = 0, added = 0, deleted = 0, untracked = 0;
            try
            {
                var statusOutput = await RunGitAsync(localPath, "status --porcelain");
                foreach (var line in statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length < 2) continue;
                    var x = line[0]; var y = line[1];
                    if (x == '?' || y == '?') untracked++;
                    else if (x == 'A' || y == 'A') added++;
                    else if (x == 'D' || y == 'D') deleted++;
                    else if (x == 'M' || y == 'M' || x == 'R' || y == 'R') modified++;
                }
            }
            catch { }

            // 与远程的差异
            int ahead = 0, behind = 0;
            try
            {
                await RunGitAsync(localPath, "fetch origin --quiet", timeoutMs: 15000);
                var abStr = (await RunGitAsync(localPath, $"rev-list --left-right --count {branch}...origin/{branch}")).Trim();
                var ab = abStr.Split('\t');
                if (ab.Length == 2) { ahead = int.Parse(ab[0]); behind = int.Parse(ab[1]); }
            }
            catch { }

            return new
            {
                branch,
                commitHash = parts.Length > 0 ? parts[0] : "",
                commitMessage = parts.Length > 1 ? parts[1] : "",
                commitAuthor = parts.Length > 2 ? parts[2] : "",
                commitTime = parts.Length > 3 ? parts[3] : "",
                ahead, behind,
                modified, added, deleted, untracked,
                hasChanges = modified + added + deleted + untracked > 0,
                localPath,
            };
        }

        /// <summary>
        /// 将 http(s) 仓库地址注入 GitLab Token 认证
        /// http://host/path.git → http://oauth2:TOKEN@host/path.git
        /// </summary>
        private string BuildAuthUrl(string repoUrl)
        {
            var token = _settingService.GetSystemSettingByKey<string>("gitlab_token");
            if (string.IsNullOrEmpty(token)) return repoUrl;

            try
            {
                var uri = new Uri(repoUrl);
                if (uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo))
                    return $"{uri.Scheme}://oauth2:{token}@{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}{uri.PathAndQuery}";
            }
            catch { }

            return repoUrl;
        }

        #region Git 命令执行

        public async Task<string> RunGit(string workingDirectory, string arguments, int timeoutMs = 60000)
            => await RunGitAsync(workingDirectory, arguments, timeoutMs);

        private async Task<string> RunGitAsync(string workingDirectory, string arguments, int timeoutMs = 60000)
        {
            _logger.LogDebug("执行 git 命令: git {Args} (在 {Dir})", arguments, workingDirectory);

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var completed = await Task.Run(() => process.WaitForExit(timeoutMs));
            if (!completed)
            {
                process.Kill();
                throw new TimeoutException($"git {arguments} 执行超时（{timeoutMs}ms）");
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("git 命令失败: git {Args}, ExitCode={Code}, Error={Error}", arguments, process.ExitCode, error);
                throw new InvalidOperationException($"git {arguments} 失败: {error}");
            }

            return output;
        }

        #endregion
    }
}

using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Pipelines;

namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 上下文构建器 — 自动从数据库和文件系统收集 AI 所需的上下文信息
    /// </summary>
    [AutoDependencyInjection]
    public class AiContextBuilder
    {
        private readonly IFreeSql _fsql;
        private readonly SystemSettingService _settingService;
        private readonly AiMemoryService _memoryService;
        private readonly ILogger<AiContextBuilder> _logger;

        // 自动排除的目录和文件
        private static readonly HashSet<string> ExcludeDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", "node_modules", "bin", "obj", "dist", "build", "output",
            ".vs", ".idea", ".vscode", "packages", ".nuget", "wwwroot/lib",
            "__pycache__", ".next", ".nuxt", ".turbo", ".cache",
        };

        private static readonly HashSet<string> ExcludeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".so", ".dylib", ".pdb", ".obj", ".o",
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg",
            ".mp3", ".mp4", ".avi", ".mov", ".wav",
            ".zip", ".rar", ".7z", ".tar", ".gz",
            ".woff", ".woff2", ".ttf", ".eot",
            ".lock", ".map",
        };

        // 高优先级文件（常见的入口/配置文件）
        private static readonly HashSet<string> HighPriorityFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "README.md", "CLAUDE.md", ".editorconfig",
            "appsettings.json", "appsettings.Development.json",
            "package.json", "tsconfig.json", "vite.config.ts", "vite.config.mts",
            "Program.cs", "Startup.cs",
        };

        private readonly GitRepositoryService? _gitRepoService;

        public AiContextBuilder(IFreeSql fsql, SystemSettingService settingService, AiMemoryService memoryService,
            ILogger<AiContextBuilder> logger, GitRepositoryService? gitRepoService = null)
        {
            _fsql = fsql;
            _settingService = settingService;
            _memoryService = memoryService;
            _logger = logger;
            _gitRepoService = gitRepoService;
        }

        /// <summary>
        /// 从需求构建上下文
        /// </summary>
        public AiContext FromRequirement(int requirementId, string instruction)
        {
            var req = _fsql.Select<Requirement>().Where(r => r.Id == requirementId).First();
            if (req == null) throw new ArgumentException($"需求 {requirementId} 不存在");

            var project = _fsql.Select<Project>().Where(p => p.Id == req.ProjectId).First();

            var context = new AiContext
            {
                ProjectId = req.ProjectId,
                ProjectName = project?.Name,
                RequirementId = requirementId,
                RequirementContent = $"# {req.Title}\n\n类型: {req.Type}\n\n{req.Content}",
                Instruction = instruction,
            };

            return context;
        }

        /// <summary>
        /// 从子项目构建上下文
        /// </summary>
        public AiContext FromSubProject(int subProjectId, string instruction)
        {
            var sp = _fsql.Select<SubProject>().Where(s => s.Id == subProjectId).First();
            if (sp == null) throw new ArgumentException($"子项目 {subProjectId} 不存在");

            var project = _fsql.Select<Project>().Where(p => p.Id == sp.ProjectId).First();

            // 解析本地路径
            var localPath = sp.LocalPath;
            if (string.IsNullOrEmpty(localPath))
            {
                var workspaceRoot = _settingService.GetSystemSettingByKey<string>("workspace_root");
                if (!string.IsNullOrEmpty(workspaceRoot) && !string.IsNullOrEmpty(project?.Code) && !string.IsNullOrEmpty(sp.Code))
                {
                    localPath = Path.Combine(workspaceRoot, project.Code, sp.Code);
                }
            }

            var context = new AiContext
            {
                ProjectId = sp.ProjectId,
                SubProjectId = subProjectId,
                ProjectName = project?.Name,
                SubProjectName = sp.Name,
                WorkingDirectory = localPath,
                Instruction = instruction,
            };

            return context;
        }

        /// <summary>
        /// 丰富上下文：自动加载项目相关的 AI 记忆
        /// </summary>
        public void EnrichWithMemory(AiContext context)
        {
            if (!context.ProjectId.HasValue) return;

            try
            {
                // 加载项目级别和全局记忆
                var memories = _fsql.Select<AiMemory>()
                    .Where(m => m.ProjectId == context.ProjectId.Value || m.Scope == AiMemoryScope.Global || m.Scope == AiMemoryScope.Permanent)
                    .OrderByDescending(m => m.Importance)
                    .Take(20)
                    .ToList();

                context.Memories = memories.Select(m => new AiMemoryItem
                {
                    Title = m.Title,
                    Content = m.Content,
                    Category = m.Category ?? "",
                    Importance = m.Importance,
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载 AI 记忆失败");
            }
        }

        /// <summary>
        /// 丰富上下文：自动扫描工程目录，按 FocusPaths 筛选关键文件
        /// </summary>
        public void EnrichWithFiles(AiContext context, int maxFiles = 20, int maxTokensEstimate = 50000)
        {
            if (string.IsNullOrEmpty(context.WorkingDirectory) || !Directory.Exists(context.WorkingDirectory))
                return;

            var allFiles = new List<(string relativePath, long size, bool highPriority)>();

            ScanDirectory(context.WorkingDirectory, context.WorkingDirectory, allFiles, context.FocusPaths);

            // 排序：FocusPaths 中的文件优先，然后高优先级文件，然后按大小升序
            var sorted = allFiles
                .OrderByDescending(f => context.FocusPaths?.Any(fp => f.relativePath.StartsWith(fp, StringComparison.OrdinalIgnoreCase)) == true)
                .ThenByDescending(f => f.highPriority)
                .ThenBy(f => f.size)
                .ToList();

            int estimatedTokens = 0;
            int filesAdded = 0;

            foreach (var (relativePath, size, _) in sorted)
            {
                if (filesAdded >= maxFiles || estimatedTokens >= maxTokensEstimate) break;

                // 粗略估算: 1 token ≈ 4 字符
                var tokenEstimate = (int)(size / 4);
                if (estimatedTokens + tokenEstimate > maxTokensEstimate && filesAdded > 0) break;

                try
                {
                    var fullPath = Path.Combine(context.WorkingDirectory, relativePath);
                    var content = File.ReadAllText(fullPath);
                    var ext = Path.GetExtension(relativePath).TrimStart('.');

                    context.Files.Add(new AiFileContext
                    {
                        FilePath = relativePath,
                        Content = content,
                        Language = ext,
                        Size = size,
                    });

                    estimatedTokens += tokenEstimate;
                    filesAdded++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "读取文件失败: {Path}", relativePath);
                }
            }
        }

        /// <summary>
        /// 丰富上下文：注入编码规范
        /// </summary>
        public void EnrichWithConventions(AiContext context)
        {
            if (string.IsNullOrEmpty(context.WorkingDirectory)) return;

            // 尝试读取 CLAUDE.md
            var claudeMd = Path.Combine(context.WorkingDirectory, "CLAUDE.md");
            if (File.Exists(claudeMd))
            {
                try
                {
                    context.CodingConventions = File.ReadAllText(claudeMd);
                    return;
                }
                catch { /* ignore */ }
            }

            // 尝试读取 .editorconfig
            var editorconfig = Path.Combine(context.WorkingDirectory, ".editorconfig");
            if (File.Exists(editorconfig))
            {
                try
                {
                    context.CodingConventions = File.ReadAllText(editorconfig);
                }
                catch { /* ignore */ }
            }
        }

        /// <summary>
        /// 丰富上下文：注入项目关联的 Git 仓库信息（RepoId/RepoUrl/RecentCommits）。
        /// 供 AI 了解代码变化背景；GetCommits 失败时静默跳过不中断。
        /// </summary>
        public async Task EnrichWithGitInfo(AiContext context, int commitLimit = 10)
        {
            if (!context.ProjectId.HasValue || _gitRepoService == null) return;
            try
            {
                var repo = _gitRepoService.GetProjectRepo(context.ProjectId.Value);
                if (repo == null) return;

                context.GitRepoId = repo.Id;
                context.GitRepoUrl = repo.RepoUrl;

                var commitsResult = await _gitRepoService.GetCommits(repo.Id, limit: commitLimit);
                if (commitsResult.Code != 0) return;

                // commits 返回的是动态对象列表,用反射/JSON 转 GitCommitSummary
                var json = System.Text.Json.JsonSerializer.Serialize(commitsResult.Data);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(json);
                if (list == null) return;

                foreach (var item in list)
                {
                    context.RecentCommits.Add(new GitCommitSummary
                    {
                        Sha = TryGet(item, "sha", "id") ?? "",
                        Message = TryGet(item, "message", "title") ?? "",
                        Author = TryGet(item, "authorName", "author_name") ?? "",
                        CommittedAt = DateTime.TryParse(TryGet(item, "committedAt", "committed_date"), out var dt) ? dt : default,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "EnrichWithGitInfo 失败,跳过");
            }
        }

        private static string? TryGet(System.Text.Json.JsonElement el, params string[] keys)
        {
            foreach (var k in keys)
                if (el.TryGetProperty(k, out var v) && v.ValueKind != System.Text.Json.JsonValueKind.Null)
                    return v.ToString();
            return null;
        }

        #region 私有方法

        private void ScanDirectory(string rootDir, string currentDir, List<(string relativePath, long size, bool highPriority)> result, List<string>? focusPaths)
        {
            try
            {
                var dirName = Path.GetFileName(currentDir);
                if (ExcludeDirs.Contains(dirName)) return;

                foreach (var file in Directory.GetFiles(currentDir))
                {
                    var ext = Path.GetExtension(file);
                    if (ExcludeExtensions.Contains(ext)) continue;

                    var info = new FileInfo(file);
                    if (info.Length > 500 * 1024) continue; // 跳过大于 500KB 的文件

                    var relativePath = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
                    var fileName = Path.GetFileName(file);
                    var isHighPriority = HighPriorityFiles.Contains(fileName);

                    result.Add((relativePath, info.Length, isHighPriority));
                }

                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    ScanDirectory(rootDir, subDir, result, focusPaths);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "扫描目录失败: {Dir}", currentDir);
            }
        }

        #endregion
    }
}

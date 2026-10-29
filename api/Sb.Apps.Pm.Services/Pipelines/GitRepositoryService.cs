using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Util;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Pipelines
{
    [AutoDependencyInjection]
    public class GitRepositoryService : BaseCrudService<GitRepository>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "Git 集成";
        public int InitOrder => 120;
        public Type[] GetEntityTypes() => new[]
        {
            typeof(GitRepository), typeof(GitWebhookLog), typeof(GitPipeline),
        };
        public List<SystemSettingDefinition> GetSystemSettings() => new()
        {
            new SystemSettingDefinition { Key = "gitlab_url", Name = "GitLab 地址", CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1, DisplayType = SystemSettingDisplayType.Input, DefaultValue = "", CantDelete = true, Sort = 5 },
            new SystemSettingDefinition { Key = "gitlab_token", Name = "GitLab Token", CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1, DisplayType = SystemSettingDisplayType.InputPassword, DefaultValue = "", CantDelete = true, Sort = 6 },
        };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                Name = "Git", Path = "/git", Component = "BasicLayout",
                Redirect = "/git/repo", Title = "Git 集成",
                Icon = "lucide:git-branch", OrderNo = 4,
                Children = new()
                {
                    new() { Name = "GitRepo", Path = "/git/repo", Component = "/git/repo/index", Title = "仓库管理", Icon = "lucide:git-fork", OrderNo = 1 },
                    new() { Name = "GitPipeline", Path = "/git/pipeline", Component = "/git/pipeline/index", Title = "CI/CD 管道", Icon = "lucide:workflow", OrderNo = 2 },
                    new() { Name = "GitMergeRequest", Path = "/git/mr", Component = "/git/mr/index", Title = "Merge Request", Icon = "lucide:git-merge", OrderNo = 3 },
                }
            }
        };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

        public GitRepositoryService(IFreeSql fsql, ILogger<GitRepositoryService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>获取项目关联的首个 Git 仓库（供 AI 上下文注入）。</summary>
        public GitRepository? GetProjectRepo(int projectId)
            => Select.Where(r => r.ProjectId == projectId && !r.IsDeleted)
                     .OrderBy(r => r.Id).First();

        public Result GetList(int? projectId = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select.Include(c => c.Project)
                .Where(c => !c.IsDeleted);
            query = query.WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value);
            query = query.WhereIf(!string.IsNullOrEmpty(keyword), c => c.RepoUrl.Contains(keyword) || c.RepoFullName.Contains(keyword));
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(GitRepository repo)
        {
            if (Select.Where(c => c.RepoUrl == repo.RepoUrl && c.ProjectId == repo.ProjectId && !c.IsDeleted).Any())
                return Result.Fail("该项目下已存在相同仓库地址!");

            if (!string.IsNullOrEmpty(repo.AccessToken))
                repo.AccessToken = EncryptionUtil.AesEncrypt(repo.AccessToken);

            if (!string.IsNullOrEmpty(repo.WebhookSecret))
                repo.WebhookSecret = EncryptionUtil.AesEncrypt(repo.WebhookSecret);

            repo.CreatedTime = DateTime.Now;
            repo = this.Insert(repo);
            return Result.Success("仓库创建成功!", repo);
        }

        public Result Edit(GitRepository repo)
        {
            var existing = Select.Where(c => c.Id == repo.Id).First();
            if (existing == null) return Result.Fail("仓库不存在!");

            // Encrypt AccessToken only if it changed (not masked)
            if (!string.IsNullOrEmpty(repo.AccessToken) && repo.AccessToken != "******")
                repo.AccessToken = EncryptionUtil.AesEncrypt(repo.AccessToken);
            else
                repo.AccessToken = existing.AccessToken;

            if (!string.IsNullOrEmpty(repo.WebhookSecret) && repo.WebhookSecret != "******")
                repo.WebhookSecret = EncryptionUtil.AesEncrypt(repo.WebhookSecret);
            else
                repo.WebhookSecret = existing.WebhookSecret;

            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.ProjectId, c.Platform, c.RepoUrl, c.RepoFullName, c.RepoId,
                c.AccessToken, c.WebhookSecret, c.DefaultBranch
            }).SetSource(repo).ExecuteAffrows();

            return count > 0 ? Result.Success("仓库更新成功!") : Result.Fail("仓库更新失败!");
        }

        public Result GetDetail(int id)
        {
            var entity = Select.Include(c => c.Project).Where(c => c.Id == id).First();
            if (entity == null) return Result.Fail("仓库不存在!");

            // Mask sensitive fields
            if (!string.IsNullOrEmpty(entity.AccessToken))
                entity.AccessToken = "******";
            if (!string.IsNullOrEmpty(entity.WebhookSecret))
                entity.WebhookSecret = "******";

            return Result.Success(entity);
        }

        public Result Sync(int id)
        {
            var repo = Select.Where(c => c.Id == id).First();
            if (repo == null) return Result.Fail("仓库不存在!");

            // Placeholder: set syncing status
            _fsql.Update<GitRepository>()
                .Where(c => c.Id == id)
                .Set(c => c.SyncStatus, GitSyncStatus.Syncing)
                .Set(c => c.LastSyncTime, DateTime.Now)
                .ExecuteAffrows();

            // TODO: Implement real sync logic (fetch branches, recent commits, etc.)

            _fsql.Update<GitRepository>()
                .Where(c => c.Id == id)
                .Set(c => c.SyncStatus, GitSyncStatus.Idle)
                .ExecuteAffrows();

            return Result.Success("同步完成!");
        }

        public async Task<Result> GetBranches(int id)
        {
            var repo = Select.Where(c => c.Id == id).First();
            if (repo == null) return Result.Fail("仓库不存在!");

            try
            {
                var token = DecryptToken(repo.AccessToken);
                var branches = repo.Platform switch
                {
                    GitPlatform.GitLab => await FetchGitLabBranches(repo, token),
                    GitPlatform.GitHub => await FetchGitHubBranches(repo, token),
                    _ => throw new NotSupportedException($"不支持的平台: {repo.Platform}")
                };
                return Result.Success(branches);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分支列表失败, RepoId={Id}", id);
                return Result.Fail($"获取分支列表失败: {ex.Message}");
            }
        }

        public async Task<Result> GetCommits(int id, string? branch = null, int limit = 20)
        {
            var repo = Select.Where(c => c.Id == id).First();
            if (repo == null) return Result.Fail("仓库不存在!");

            try
            {
                var token = DecryptToken(repo.AccessToken);
                var commits = repo.Platform switch
                {
                    GitPlatform.GitLab => await FetchGitLabCommits(repo, token, branch, limit),
                    GitPlatform.GitHub => await FetchGitHubCommits(repo, token, branch, limit),
                    _ => throw new NotSupportedException($"不支持的平台: {repo.Platform}")
                };
                return Result.Success(commits);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取提交记录失败, RepoId={Id}", id);
                return Result.Fail($"获取提交记录失败: {ex.Message}");
            }
        }

        #region GitLab API

        private async Task<object> FetchGitLabBranches(GitRepository repo, string token)
        {
            var baseUrl = ExtractGitLabBaseUrl(repo.RepoUrl);
            var url = $"{baseUrl}/api/v4/projects/{repo.RepoId}/repository/branches?per_page=100";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("PRIVATE-TOKEN", token);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JArray.Parse(json).Select(b => new
            {
                name = b["name"]?.ToString(),
                isDefault = b["default"]?.ToObject<bool>() ?? false,
                commitId = b["commit"]?["id"]?.ToString(),
                commitMessage = b["commit"]?["message"]?.ToString(),
                committedDate = b["commit"]?["committed_date"]?.ToString()
            }).ToList();
        }

        private async Task<object> FetchGitLabCommits(GitRepository repo, string token, string? branch, int limit)
        {
            var baseUrl = ExtractGitLabBaseUrl(repo.RepoUrl);
            var url = $"{baseUrl}/api/v4/projects/{repo.RepoId}/repository/commits?per_page={limit}";
            if (!string.IsNullOrEmpty(branch))
                url += $"&ref_name={Uri.EscapeDataString(branch)}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("PRIVATE-TOKEN", token);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JArray.Parse(json).Select(c => new
            {
                id = c["id"]?.ToString(),
                shortId = c["short_id"]?.ToString(),
                title = c["title"]?.ToString(),
                message = c["message"]?.ToString(),
                authorName = c["author_name"]?.ToString(),
                authorEmail = c["author_email"]?.ToString(),
                createdAt = c["created_at"]?.ToString(),
                webUrl = c["web_url"]?.ToString()
            }).ToList();
        }

        /// <summary>
        /// Extract base URL from GitLab repo URL.
        /// e.g. "http://gitlab.saibo.club/group/project" => "http://gitlab.saibo.club"
        /// </summary>
        private string ExtractGitLabBaseUrl(string repoUrl)
        {
            var uri = new Uri(repoUrl.TrimEnd('/'));
            return $"{uri.Scheme}://{uri.Authority}";
        }

        #endregion

        #region GitHub API

        private async Task<object> FetchGitHubBranches(GitRepository repo, string token)
        {
            var url = $"https://api.github.com/repos/{repo.RepoFullName}/branches?per_page=100";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"token {token}");
            request.Headers.Add("User-Agent", "SbPm");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JArray.Parse(json).Select(b => new
            {
                name = b["name"]?.ToString(),
                isDefault = false,
                commitId = b["commit"]?["sha"]?.ToString(),
                commitMessage = (string?)null,
                committedDate = (string?)null
            }).ToList();
        }

        private async Task<object> FetchGitHubCommits(GitRepository repo, string token, string? branch, int limit)
        {
            var url = $"https://api.github.com/repos/{repo.RepoFullName}/commits?per_page={limit}";
            if (!string.IsNullOrEmpty(branch))
                url += $"&sha={Uri.EscapeDataString(branch)}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"token {token}");
            request.Headers.Add("User-Agent", "SbPm");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JArray.Parse(json).Select(c => new
            {
                id = c["sha"]?.ToString(),
                shortId = c["sha"]?.ToString()?[..7],
                title = c["commit"]?["message"]?.ToString()?.Split('\n').FirstOrDefault(),
                message = c["commit"]?["message"]?.ToString(),
                authorName = c["commit"]?["author"]?["name"]?.ToString(),
                authorEmail = c["commit"]?["author"]?["email"]?.ToString(),
                createdAt = c["commit"]?["author"]?["date"]?.ToString(),
                webUrl = c["html_url"]?.ToString()
            }).ToList();
        }

        #endregion

        #region Helpers

        private string DecryptToken(string? encryptedToken)
        {
            if (string.IsNullOrEmpty(encryptedToken))
                throw new InvalidOperationException("仓库未配置 AccessToken!");
            return EncryptionUtil.AesDecrypt(encryptedToken);
        }

        #endregion
    }
}

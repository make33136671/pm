using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SB.Framework.Core.Attribute;

namespace Sb.Apps.Pm.Services.Pipelines
{
    /// <summary>
    /// GitLab REST API v4 封装服务
    /// 使用系统设置中的 gitlab_url 和 gitlab_token 作为全局配置
    /// </summary>
    [AutoDependencyInjection]
    public class GitLabApiService
    {
        private readonly SystemSettingService _settingService;
        private readonly ILogger<GitLabApiService> _logger;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public GitLabApiService(SystemSettingService settingService, ILogger<GitLabApiService> logger)
        {
            _settingService = settingService;
            _logger = logger;
        }

        #region 基础请求

        private (string baseUrl, string token) GetConfig()
        {
            var baseUrl = _settingService.GetSystemSettingByKey<string>("gitlab_url");
            var token = _settingService.GetSystemSettingByKey<string>("gitlab_token");

            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("请先在系统设置中配置 GitLab 地址 (gitlab_url)");
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("请先在系统设置中配置 GitLab Token (gitlab_token)");

            return (baseUrl.TrimEnd('/'), token);
        }

        private async Task<JToken> RequestAsync(HttpMethod method, string path, object? body = null, string? overrideUrl = null, string? overrideToken = null)
        {
            var (baseUrl, token) = GetConfig();
            var url = $"{overrideUrl ?? baseUrl}/api/v4{path}";
            var tk = overrideToken ?? token;

            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("PRIVATE-TOKEN", tk);

            if (body != null)
            {
                var json = JsonConvert.SerializeObject(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitLab API {Method} {Path} => {Status}: {Body}", method, path, (int)response.StatusCode, content);
                throw new HttpRequestException($"GitLab API 请求失败 ({(int)response.StatusCode}): {content}");
            }

            if (string.IsNullOrWhiteSpace(content)) return new JObject();
            return content.TrimStart().StartsWith("[") ? JArray.Parse(content) : JObject.Parse(content);
        }

        private Task<JToken> GetAsync(string path) => RequestAsync(HttpMethod.Get, path);
        private Task<JToken> PostAsync(string path, object? body = null) => RequestAsync(HttpMethod.Post, path, body);
        private Task<JToken> PutAsync(string path, object? body = null) => RequestAsync(HttpMethod.Put, path, body);
        private Task<JToken> DeleteAsync(string path) => RequestAsync(HttpMethod.Delete, path);

        private static string Encode(string value) => Uri.EscapeDataString(value);

        #endregion

        #region 群组 Groups

        /// <summary>
        /// 获取当前用户可见的群组列表
        /// </summary>
        public async Task<object> GetGroups(string? search = null, int page = 1, int perPage = 20)
        {
            var path = $"/groups?page={page}&per_page={perPage}&order_by=name&sort=asc";
            if (!string.IsNullOrEmpty(search))
                path += $"&search={Encode(search)}";

            var data = await GetAsync(path);
            return ((JArray)data).Select(g => new
            {
                id = g["id"]?.ToObject<int>(),
                name = g["name"]?.ToString(),
                fullName = g["full_name"]?.ToString(),
                fullPath = g["full_path"]?.ToString(),
                description = g["description"]?.ToString(),
                visibility = g["visibility"]?.ToString(),
                webUrl = g["web_url"]?.ToString(),
                avatarUrl = g["avatar_url"]?.ToString(),
            }).ToList();
        }

        /// <summary>
        /// 获取群组详情
        /// </summary>
        public async Task<object> GetGroupDetail(int groupId)
        {
            var data = await GetAsync($"/groups/{groupId}");
            return new
            {
                id = data["id"]?.ToObject<int>(),
                name = data["name"]?.ToString(),
                fullName = data["full_name"]?.ToString(),
                fullPath = data["full_path"]?.ToString(),
                description = data["description"]?.ToString(),
                visibility = data["visibility"]?.ToString(),
                webUrl = data["web_url"]?.ToString(),
                projectCount = data["projects"]?.Count(),
            };
        }

        /// <summary>
        /// 创建群组
        /// </summary>
        public async Task<object> CreateGroup(string name, string path, string? description = null, string visibility = "private")
        {
            var data = await PostAsync("/groups", new
            {
                name,
                path,
                description = description ?? "",
                visibility,
            });
            return new
            {
                id = data["id"]?.ToObject<int>(),
                name = data["name"]?.ToString(),
                fullPath = data["full_path"]?.ToString(),
                webUrl = data["web_url"]?.ToString(),
                visibility = data["visibility"]?.ToString(),
            };
        }

        /// <summary>
        /// 获取群组下的项目列表
        /// </summary>
        public async Task<object> GetGroupProjects(int groupId, string? search = null, int page = 1, int perPage = 20)
        {
            var path = $"/groups/{groupId}/projects?page={page}&per_page={perPage}&order_by=last_activity_at&sort=desc&include_subgroups=true";
            if (!string.IsNullOrEmpty(search))
                path += $"&search={Encode(search)}";

            var data = await GetAsync(path);
            return MapProjects((JArray)data);
        }

        #endregion

        #region 项目 Projects

        /// <summary>
        /// 获取项目列表（当前用户可见）
        /// </summary>
        public async Task<object> GetProjects(string? search = null, int page = 1, int perPage = 20)
        {
            var path = $"/projects?page={page}&per_page={perPage}&order_by=last_activity_at&sort=desc&membership=true";
            if (!string.IsNullOrEmpty(search))
                path += $"&search={Encode(search)}";

            var data = await GetAsync(path);
            return MapProjects((JArray)data);
        }

        /// <summary>
        /// 获取项目详情
        /// </summary>
        public async Task<object> GetProjectDetail(int projectId)
        {
            var data = await GetAsync($"/projects/{projectId}");
            return MapProject(data);
        }

        private static object MapProject(JToken p) => new
        {
            id = p["id"]?.ToObject<int>(),
            name = p["name"]?.ToString(),
            nameWithNamespace = p["name_with_namespace"]?.ToString(),
            path = p["path"]?.ToString(),
            pathWithNamespace = p["path_with_namespace"]?.ToString(),
            description = p["description"]?.ToString(),
            defaultBranch = p["default_branch"]?.ToString(),
            visibility = p["visibility"]?.ToString(),
            webUrl = p["web_url"]?.ToString(),
            httpUrlToRepo = p["http_url_to_repo"]?.ToString(),
            sshUrlToRepo = p["ssh_url_to_repo"]?.ToString(),
            lastActivityAt = p["last_activity_at"]?.ToString(),
            createdAt = p["created_at"]?.ToString(),
            namespace_ = p["namespace"]?["full_path"]?.ToString(),
        };

        private static List<object> MapProjects(JArray arr) => arr.Select(MapProject).ToList();

        /// <summary>
        /// 在群组下创建项目（仓库）
        /// </summary>
        public async Task<object> CreateProject(int namespaceId, string name, string path, string? description = null, string visibility = "private", bool initializeWithReadme = true)
        {
            var data = await PostAsync("/projects", new
            {
                name,
                path,
                namespace_id = namespaceId,
                description = description ?? "",
                visibility,
                initialize_with_readme = initializeWithReadme,
            });
            return MapProject(data);
        }

        #endregion

        #region 分支 Branches

        /// <summary>
        /// 获取项目分支列表
        /// </summary>
        public async Task<object> GetBranches(int projectId, string? search = null)
        {
            var path = $"/projects/{projectId}/repository/branches?per_page=100";
            if (!string.IsNullOrEmpty(search))
                path += $"&search={Encode(search)}";

            var data = await GetAsync(path);
            return ((JArray)data).Select(b => new
            {
                name = b["name"]?.ToString(),
                isDefault = b["default"]?.ToObject<bool>() ?? false,
                isProtected = b["protected"]?.ToObject<bool>() ?? false,
                merged = b["merged"]?.ToObject<bool>() ?? false,
                commitId = b["commit"]?["id"]?.ToString(),
                commitShortId = b["commit"]?["short_id"]?.ToString(),
                commitMessage = b["commit"]?["message"]?.ToString(),
                committedDate = b["commit"]?["committed_date"]?.ToString(),
                authorName = b["commit"]?["author_name"]?.ToString(),
            }).ToList();
        }

        /// <summary>
        /// 创建分支
        /// </summary>
        public async Task<object> CreateBranch(int projectId, string branchName, string fromRef)
        {
            var data = await PostAsync($"/projects/{projectId}/repository/branches", new
            {
                branch = branchName,
                @ref = fromRef,
            });
            return new
            {
                name = data["name"]?.ToString(),
                commitId = data["commit"]?["id"]?.ToString(),
            };
        }

        /// <summary>
        /// 删除分支
        /// </summary>
        public async Task DeleteBranch(int projectId, string branchName)
        {
            await DeleteAsync($"/projects/{projectId}/repository/branches/{Encode(branchName)}");
        }

        #endregion

        #region 文件树 & 文件内容 Repository

        /// <summary>
        /// 获取仓库文件树
        /// </summary>
        public async Task<object> GetTree(int projectId, string? path = null, string? refName = null, bool recursive = false)
        {
            var apiPath = $"/projects/{projectId}/repository/tree?per_page=100";
            if (!string.IsNullOrEmpty(path))
                apiPath += $"&path={Encode(path)}";
            if (!string.IsNullOrEmpty(refName))
                apiPath += $"&ref={Encode(refName)}";
            if (recursive)
                apiPath += "&recursive=true";

            var data = await GetAsync(apiPath);
            return ((JArray)data).Select(f => new
            {
                id = f["id"]?.ToString(),
                name = f["name"]?.ToString(),
                type = f["type"]?.ToString(),
                path = f["path"]?.ToString(),
                mode = f["mode"]?.ToString(),
            }).ToList();
        }

        /// <summary>
        /// 获取文件内容
        /// </summary>
        public async Task<object> GetFileContent(int projectId, string filePath, string? refName = null)
        {
            var apiPath = $"/projects/{projectId}/repository/files/{Encode(filePath)}";
            if (!string.IsNullOrEmpty(refName))
                apiPath += $"?ref={Encode(refName)}";

            var data = await GetAsync(apiPath);
            var contentBase64 = data["content"]?.ToString() ?? "";
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(contentBase64));

            return new
            {
                fileName = data["file_name"]?.ToString(),
                filePath = data["file_path"]?.ToString(),
                size = data["size"]?.ToObject<int>(),
                encoding = data["encoding"]?.ToString(),
                refName = data["ref"]?.ToString(),
                lastCommitId = data["last_commit_id"]?.ToString(),
                content,
            };
        }

        /// <summary>
        /// 创建文件（自动提交）
        /// </summary>
        public async Task<object> CreateFile(int projectId, string filePath, string content, string branch, string commitMessage)
        {
            var data = await PostAsync($"/projects/{projectId}/repository/files/{Encode(filePath)}", new
            {
                branch,
                content,
                commit_message = commitMessage,
            });
            return new { filePath = data["file_path"]?.ToString(), branch = data["branch"]?.ToString() };
        }

        /// <summary>
        /// 更新文件（自动提交）
        /// </summary>
        public async Task<object> UpdateFile(int projectId, string filePath, string content, string branch, string commitMessage)
        {
            var data = await PutAsync($"/projects/{projectId}/repository/files/{Encode(filePath)}", new
            {
                branch,
                content,
                commit_message = commitMessage,
            });
            return new { filePath = data["file_path"]?.ToString(), branch = data["branch"]?.ToString() };
        }

        /// <summary>
        /// 删除文件（自动提交）
        /// </summary>
        public async Task DeleteFile(int projectId, string filePath, string branch, string commitMessage)
        {
            await DeleteAsync($"/projects/{projectId}/repository/files/{Encode(filePath)}?branch={Encode(branch)}&commit_message={Encode(commitMessage)}");
        }

        #endregion

        #region 提交 Commits

        /// <summary>
        /// 获取提交列表
        /// </summary>
        public async Task<object> GetCommits(int projectId, string? refName = null, string? path = null, int page = 1, int perPage = 20)
        {
            var apiPath = $"/projects/{projectId}/repository/commits?page={page}&per_page={perPage}";
            if (!string.IsNullOrEmpty(refName))
                apiPath += $"&ref_name={Encode(refName)}";
            if (!string.IsNullOrEmpty(path))
                apiPath += $"&path={Encode(path)}";

            var data = await GetAsync(apiPath);
            return ((JArray)data).Select(c => new
            {
                id = c["id"]?.ToString(),
                shortId = c["short_id"]?.ToString(),
                title = c["title"]?.ToString(),
                message = c["message"]?.ToString(),
                authorName = c["author_name"]?.ToString(),
                authorEmail = c["author_email"]?.ToString(),
                createdAt = c["created_at"]?.ToString(),
                webUrl = c["web_url"]?.ToString(),
            }).ToList();
        }

        /// <summary>
        /// 获取单个提交详情
        /// </summary>
        public async Task<object> GetCommitDetail(int projectId, string sha)
        {
            var data = await GetAsync($"/projects/{projectId}/repository/commits/{sha}");
            return new
            {
                id = data["id"]?.ToString(),
                shortId = data["short_id"]?.ToString(),
                title = data["title"]?.ToString(),
                message = data["message"]?.ToString(),
                authorName = data["author_name"]?.ToString(),
                authorEmail = data["author_email"]?.ToString(),
                createdAt = data["created_at"]?.ToString(),
                webUrl = data["web_url"]?.ToString(),
                stats = new
                {
                    additions = data["stats"]?["additions"]?.ToObject<int>(),
                    deletions = data["stats"]?["deletions"]?.ToObject<int>(),
                    total = data["stats"]?["total"]?.ToObject<int>(),
                },
            };
        }

        /// <summary>
        /// 获取单个 commit 的改动文件列表（diff）— 任务交付追溯用
        /// 调 /projects/{id}/repository/commits/{sha}/diff
        /// </summary>
        public async Task<List<CommitChangedFile>> GetCommitChanges(int projectId, string sha)
        {
            var data = await GetAsync($"/projects/{projectId}/repository/commits/{Encode(sha)}/diff");
            var arr = data as JArray ?? new JArray();
            return arr.Select(d => new CommitChangedFile
            {
                OldPath = d["old_path"]?.ToString() ?? "",
                NewPath = d["new_path"]?.ToString() ?? "",
                NewFile = d["new_file"]?.ToObject<bool>() ?? false,
                DeletedFile = d["deleted_file"]?.ToObject<bool>() ?? false,
                RenamedFile = d["renamed_file"]?.ToObject<bool>() ?? false,
            }).ToList();
        }

        /// <summary>
        /// 批量提交多个文件操作（创建/更新/删除/移动）
        /// </summary>
        public async Task<object> CreateCommit(int projectId, string branch, string commitMessage, List<CommitAction> actions)
        {
            var data = await PostAsync($"/projects/{projectId}/repository/commits", new
            {
                branch,
                commit_message = commitMessage,
                actions = actions.Select(a => new
                {
                    action = a.Action,
                    file_path = a.FilePath,
                    content = a.Content,
                    previous_path = a.PreviousPath,
                }),
            });
            return new
            {
                id = data["id"]?.ToString(),
                shortId = data["short_id"]?.ToString(),
                title = data["title"]?.ToString(),
                webUrl = data["web_url"]?.ToString(),
            };
        }

        #endregion

        #region 合并请求 Merge Requests

        /// <summary>
        /// 获取合并请求列表
        /// </summary>
        public async Task<object> GetMergeRequests(int projectId, string? state = null, int page = 1, int perPage = 20)
        {
            var path = $"/projects/{projectId}/merge_requests?page={page}&per_page={perPage}&order_by=created_at&sort=desc";
            if (!string.IsNullOrEmpty(state))
                path += $"&state={Encode(state)}";

            var data = await GetAsync(path);
            return ((JArray)data).Select(MapMergeRequest).ToList();
        }

        /// <summary>
        /// 获取合并请求详情
        /// </summary>
        public async Task<object> GetMergeRequestDetail(int projectId, int mrIid)
        {
            var data = await GetAsync($"/projects/{projectId}/merge_requests/{mrIid}");
            return MapMergeRequest(data);
        }

        /// <summary>
        /// 创建合并请求
        /// </summary>
        public async Task<object> CreateMergeRequest(int projectId, string sourceBranch, string targetBranch, string title, string? description = null)
        {
            var data = await PostAsync($"/projects/{projectId}/merge_requests", new
            {
                source_branch = sourceBranch,
                target_branch = targetBranch,
                title,
                description = description ?? "",
            });
            return MapMergeRequest(data);
        }

        /// <summary>
        /// 接受（合并）合并请求
        /// </summary>
        public async Task<object> AcceptMergeRequest(int projectId, int mrIid, bool squash = false, bool removeSourceBranch = false)
        {
            var data = await PutAsync($"/projects/{projectId}/merge_requests/{mrIid}/merge", new
            {
                squash,
                should_remove_source_branch = removeSourceBranch,
            });
            return MapMergeRequest(data);
        }

        /// <summary>
        /// 获取 MR 的变更文件列表（diff）— 用于 AI Code Review (#129)
        /// </summary>
        public async Task<List<MrChangedFile>> GetMergeRequestDiff(int projectId, int mrIid)
        {
            var data = await GetAsync($"/projects/{projectId}/merge_requests/{mrIid}/changes");
            var changes = data["changes"] as JArray ?? new JArray();
            return changes.Select(c => new MrChangedFile
            {
                OldPath = c["old_path"]?.ToString() ?? "",
                NewPath = c["new_path"]?.ToString() ?? "",
                NewFile = c["new_file"]?.ToObject<bool>() ?? false,
                DeletedFile = c["deleted_file"]?.ToObject<bool>() ?? false,
                RenamedFile = c["renamed_file"]?.ToObject<bool>() ?? false,
                Diff = c["diff"]?.ToString() ?? "",
            }).ToList();
        }

        /// <summary>
        /// 在 MR 评论区发布一条注释 — 用于 AI Code Review 结果写回 (#131)
        /// </summary>
        public async Task AddMergeRequestNote(int projectId, int mrIid, string body)
        {
            await PostAsync($"/projects/{projectId}/merge_requests/{mrIid}/notes", new { body });
        }

        private static object MapMergeRequest(JToken m) => new
        {
            id = m["id"]?.ToObject<int>(),
            iid = m["iid"]?.ToObject<int>(),
            title = m["title"]?.ToString(),
            description = m["description"]?.ToString(),
            state = m["state"]?.ToString(),
            sourceBranch = m["source_branch"]?.ToString(),
            targetBranch = m["target_branch"]?.ToString(),
            author = m["author"]?["name"]?.ToString(),
            webUrl = m["web_url"]?.ToString(),
            createdAt = m["created_at"]?.ToString(),
            mergedAt = m["merged_at"]?.ToString(),
        };

        #endregion

        #region 标签 Tags

        /// <summary>
        /// 获取标签列表
        /// </summary>
        public async Task<object> GetTags(int projectId, string? search = null, int page = 1, int perPage = 20)
        {
            var path = $"/projects/{projectId}/repository/tags?page={page}&per_page={perPage}&order_by=updated&sort=desc";
            if (!string.IsNullOrEmpty(search))
                path += $"&search={Encode(search)}";

            var data = await GetAsync(path);
            return ((JArray)data).Select(t => new
            {
                name = t["name"]?.ToString(),
                message = t["message"]?.ToString(),
                commitId = t["commit"]?["id"]?.ToString(),
                commitMessage = t["commit"]?["message"]?.ToString(),
                committedDate = t["commit"]?["committed_date"]?.ToString(),
            }).ToList();
        }

        /// <summary>
        /// 创建标签
        /// </summary>
        public async Task<object> CreateTag(int projectId, string tagName, string refName, string? message = null)
        {
            var data = await PostAsync($"/projects/{projectId}/repository/tags", new
            {
                tag_name = tagName,
                @ref = refName,
                message = message ?? "",
            });
            return new
            {
                name = data["name"]?.ToString(),
                commitId = data["commit"]?["id"]?.ToString(),
            };
        }

        #endregion

        #region Pipeline

        /// <summary>
        /// 触发 Pipeline（传入变量）
        /// </summary>
        public async Task<Dictionary<string, object>> TriggerPipeline(string projectId, string refName,
            Dictionary<string, string>? variables = null)
        {
            var body = new Dictionary<string, object> { { "ref", refName } };
            if (variables != null && variables.Count > 0)
            {
                body["variables"] = variables.Select(kv => new { key = kv.Key, value = kv.Value }).ToArray();
            }

            var data = await PostAsync($"/projects/{Encode(projectId)}/pipeline", body);
            return new Dictionary<string, object>
            {
                { "id", data["id"]?.ToObject<int>() ?? 0 },
                { "status", data["status"]?.ToString() ?? "" },
                { "web_url", data["web_url"]?.ToString() ?? "" },
                { "ref", data["ref"]?.ToString() ?? "" },
            };
        }

        /// <summary>
        /// 查询 Pipeline 状态
        /// </summary>
        public async Task<object> GetPipelineStatus(string projectId, int pipelineId)
        {
            var data = await GetAsync($"/projects/{Encode(projectId)}/pipelines/{pipelineId}");
            return new
            {
                id = data["id"]?.ToObject<int>(),
                status = data["status"]?.ToString(),
                webUrl = data["web_url"]?.ToString(),
                createdAt = data["created_at"]?.ToString(),
                startedAt = data["started_at"]?.ToString(),
                finishedAt = data["finished_at"]?.ToString(),
                duration = data["duration"]?.ToObject<int?>(),
            };
        }

        /// <summary>
        /// 获取 Pipeline Job 日志
        /// </summary>
        public async Task<string> GetJobTrace(string projectId, int jobId)
        {
            var (baseUrl, token) = GetConfig();
            var url = $"{baseUrl}/api/v4/projects/{Encode(projectId)}/jobs/{jobId}/trace";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("PRIVATE-TOKEN", token);
            var response = await _http.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// 获取 Pipeline 的所有 Jobs
        /// </summary>
        public async Task<object> GetPipelineJobs(string projectId, int pipelineId)
        {
            var data = await GetAsync($"/projects/{Encode(projectId)}/pipelines/{pipelineId}/jobs");
            return ((JArray)data).Select(j => new
            {
                id = j["id"]?.ToObject<int>(),
                name = j["name"]?.ToString(),
                stage = j["stage"]?.ToString(),
                status = j["status"]?.ToString(),
                duration = j["duration"]?.ToObject<double?>(),
                webUrl = j["web_url"]?.ToString(),
            }).ToList();
        }

        /// <summary>
        /// 获取 Pipeline 的 JUnit 测试报告（需 job 声明 artifacts:reports:junit 且 pipeline 已产出报告）。
        /// 运行中或未配置 JUnit 时 GitLab 可能返回 404，由上层捕获并提示。
        /// </summary>
        public async Task<object> GetPipelineTestReport(string projectId, int pipelineId)
        {
            var data = await GetAsync($"/projects/{Encode(projectId)}/pipelines/{pipelineId}/test_report");
            return new
            {
                totalCount = data["total_count"]?.ToObject<int>(),
                successCount = data["success_count"]?.ToObject<int>(),
                failedCount = data["failed_count"]?.ToObject<int>(),
                skippedCount = data["skipped_count"]?.ToObject<int>(),
                errorCount = data["error_count"]?.ToObject<int>(),
                totalTime = data["total_time"]?.ToObject<double?>(),
                suites = ((JArray)(data["test_suites"] ?? new JArray())).Select(s => new
                {
                    name = s["name"]?.ToString(),
                    totalCount = s["total_count"]?.ToObject<int>(),
                    successCount = s["success_count"]?.ToObject<int>(),
                    failedCount = s["failed_count"]?.ToObject<int>(),
                    skippedCount = s["skipped_count"]?.ToObject<int>(),
                    totalTime = s["total_time"]?.ToObject<double?>(),
                    cases = ((JArray)(s["test_cases"] ?? new JArray())).Select(tc => new
                    {
                        name = tc["name"]?.ToString(),
                        classname = tc["classname"]?.ToString(),
                        status = tc["status"]?.ToString(),
                        executionTime = tc["execution_time"]?.ToObject<double?>(),
                        systemOutput = tc["system_output"]?.ToString(),
                        stackTrace = tc["stack_trace"]?.ToString(),
                    }).ToList()
                }).ToList()
            };
        }

        #endregion

        #region 当前用户

        /// <summary>
        /// 获取当前 Token 对应的用户信息
        /// </summary>
        public async Task<object> GetCurrentUser()
        {
            var data = await GetAsync("/user");
            return new
            {
                id = data["id"]?.ToObject<int>(),
                username = data["username"]?.ToString(),
                name = data["name"]?.ToString(),
                email = data["email"]?.ToString(),
                avatarUrl = data["avatar_url"]?.ToString(),
                webUrl = data["web_url"]?.ToString(),
                isAdmin = data["is_admin"]?.ToObject<bool>() ?? false,
            };
        }

        #endregion
    }

    /// <summary>
    /// 批量提交的文件操作
    /// </summary>
    public class CommitAction
    {
        /// <summary>
        /// 操作类型: create, update, delete, move
        /// </summary>
        public string Action { get; set; } = "create";
        public string FilePath { get; set; } = "";
        public string? Content { get; set; }
        /// <summary>
        /// move 操作时的原路径
        /// </summary>
        public string? PreviousPath { get; set; }
    }

    /// <summary>
    /// MR 中单个文件的变更信息
    /// </summary>
    public class MrChangedFile
    {
        public string OldPath { get; set; } = "";
        public string NewPath { get; set; } = "";
        public bool NewFile { get; set; }
        public bool DeletedFile { get; set; }
        public bool RenamedFile { get; set; }
        public string Diff { get; set; } = "";
    }

    /// <summary>
    /// 单个 commit 改动的文件（不含 diff 正文，只列文件 + 变更类型）
    /// </summary>
    public class CommitChangedFile
    {
        public string OldPath { get; set; } = "";
        public string NewPath { get; set; } = "";
        public bool NewFile { get; set; }
        public bool DeletedFile { get; set; }
        public bool RenamedFile { get; set; }
        /// <summary>变更类型：added/deleted/renamed/modified</summary>
        public string Status => NewFile ? "added" : DeletedFile ? "deleted" : RenamedFile ? "renamed" : "modified";
    }
}

using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class SubProjectController : Controller
    {
        private readonly SubProjectService _service;
        private readonly GitLabApiService _gitLabApi;
        private readonly WorkspaceService _workspaceService;
        private readonly IFreeSql _fsql;

        public SubProjectController(SubProjectService service, GitLabApiService gitLabApi, WorkspaceService workspaceService, IFreeSql fsql)
        {
            _service = service;
            _gitLabApi = gitLabApi;
            _workspaceService = workspaceService;
            _fsql = fsql;
        }

        [HttpGet]
        public Result List(int? projectId = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 100)
        {
            return _service.GetList(projectId, keyword, field, order, page, pageSize);
        }

        /// <summary>
        /// 子项目详情（含 git 状态 + 关联的部署配置）
        /// </summary>
        [HttpGet]
        public async Task<Result> Detail(int id)
        {
            var sp = _service.Select
                .Include(c => c.Project)
                .Where(c => c.Id == id).First();
            if (sp == null) return Result.Fail("子项目不存在");

            // 关联的部署配置
            var deployConfig = _fsql.Select<ProjectServer>()
                .Include(c => c.Server)
                .Include(c => c.Environment)
                .Where(c => c.ProjectId == sp.ProjectId && c.SubProjectId == sp.Id)
                .First()
                ?? _fsql.Select<ProjectServer>()
                    .Include(c => c.Server)
                    .Include(c => c.Environment)
                    .Where(c => c.ProjectId == sp.ProjectId && c.SubProjectId == null && c.IsDefault)
                    .First();

            // Git 状态
            object? gitStatusData = null;
            try { gitStatusData = await _workspaceService.GetGitStatus(id); } catch { }

            // 仓库最近提交
            object? recentCommits = null;
            if (sp.GitlabProjectId.HasValue)
            {
                try { recentCommits = await _gitLabApi.GetCommits(sp.GitlabProjectId.Value, null, null, 1, 5); } catch { }
            }

            return Result.Success(new
            {
                subProject = sp,
                deploy = deployConfig == null ? null : new
                {
                    serverName = deployConfig.Server?.Name,
                    serverHost = deployConfig.Server?.Host,
                    envName = deployConfig.Environment?.Name,
                    deployConfig.ApiPort,
                    deployConfig.FrontendPort,
                    deployConfig.ApiDeployPath,
                    deployConfig.FrontendDeployPath,
                    deployConfig.SystemdServiceName,
                    deployConfig.BtSiteName,
                    deployConfig.DatabaseName,
                    deployConfig.PreferredDeployMethod,
                    deployConfig.Label,
                },
                gitStatus = gitStatusData,
                recentCommits,
            });
        }

        [HttpPost]
        public Result Add([FromForm] SubProject subProject)
        {
            return _service.Create(subProject);
        }

        [HttpPost]
        public Result Edit([FromForm] SubProject subProject)
        {
            return _service.Edit(subProject);
        }

        [HttpPost]
        public Result Del([FromForm] int id)
        {
            return _service.Del(id);
        }

        /// <summary>
        /// 批量保存子项目（前端表单提交用）
        /// </summary>
        [HttpPost]
        public Result SaveList([FromQuery] int projectId, [FromForm] SaveSubProjectsInput input)
        {
            return _service.SaveList(projectId, input.Items ?? new List<SubProject>());
        }

        public class SaveSubProjectsInput
        {
            public List<SubProject>? Items { get; set; }
        }

        /// <summary>
        /// 同步代码到服务器本地（clone 或 pull）
        /// </summary>
        [HttpPost]
        public async Task<Result> SyncCode([FromForm] int id)
        {
            try
            {
                var localPath = await _workspaceService.EnsureWorkspace(id);
                var branch = await _workspaceService.GetCurrentBranch(localPath);
                var commit = await _workspaceService.GetLatestCommit(localPath);
                return Result.Success("代码同步完成", new { localPath, branch, commit });
            }
            catch (Exception ex)
            {
                return Result.Fail($"代码同步失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 提交本地变更
        /// </summary>
        [HttpPost]
        public async Task<Result> GitCommit([FromForm] int id, [FromForm] string commitMessage)
        {
            try
            {
                var sp = _service.Select.Where(c => c.Id == id).First();
                if (sp == null) return Result.Fail("子项目不存在");
                if (string.IsNullOrEmpty(sp.LocalPath)) return Result.Fail("未同步到服务器");
                if (string.IsNullOrWhiteSpace(commitMessage)) return Result.Fail("提交信息不能为空");

                await _workspaceService.RunGit(sp.LocalPath, "add -A");
                var output = await _workspaceService.RunGit(sp.LocalPath, $"commit -m \"{commitMessage.Replace("\"", "\\\"")}\"");
                return Result.Success("提交成功", new { output = output.Trim() });
            }
            catch (Exception ex)
            {
                return Result.Fail($"提交失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 推送到远程
        /// </summary>
        [HttpPost]
        public async Task<Result> GitPush([FromForm] int id)
        {
            try
            {
                var sp = _service.Select.Where(c => c.Id == id).First();
                if (sp == null) return Result.Fail("子项目不存在");
                if (string.IsNullOrEmpty(sp.LocalPath)) return Result.Fail("未同步到服务器");

                var output = await _workspaceService.RunGit(sp.LocalPath, "push");
                return Result.Success("推送成功", new { output = output.Trim() });
            }
            catch (Exception ex)
            {
                return Result.Fail($"推送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取子项目本地仓库的 Git 状态
        /// </summary>
        [HttpGet]
        public async Task<Result> GitStatus(int id)
        {
            try
            {
                var status = await _workspaceService.GetGitStatus(id);
                return status != null ? Result.Success(status) : Result.Success("未同步", null);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 获取子项目的最近 Git 提交
        /// </summary>
        [HttpGet]
        public async Task<Result> Commits(int subProjectId, int limit = 5)
        {
            var sp = _service.Select.Where(c => c.Id == subProjectId).First();
            if (sp == null) return Result.Fail("子项目不存在");
            if (!sp.GitlabProjectId.HasValue) return Result.Success(new List<object>());
            try
            {
                var commits = await _gitLabApi.GetCommits(sp.GitlabProjectId.Value, null, null, 1, limit);
                return Result.Success(commits);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex.Message);
            }
        }
    }
}

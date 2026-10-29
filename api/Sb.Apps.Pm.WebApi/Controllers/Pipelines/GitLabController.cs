using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
    public class GitLabController : Controller
    {
        private readonly GitLabApiService _gitLabApi;

        public GitLabController(GitLabApiService gitLabApi)
        {
            _gitLabApi = gitLabApi;
        }

        #region 当前用户

        [HttpGet]
        public async Task<Result> CurrentUser()
        {
            try { return Result.Success(await _gitLabApi.GetCurrentUser()); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 群组

        [HttpGet]
        public async Task<Result> Groups(string? search = null, int page = 1, int perPage = 20)
        {
            try { return Result.Success(await _gitLabApi.GetGroups(search, page, perPage)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpGet]
        public async Task<Result> GroupDetail(int groupId)
        {
            try { return Result.Success(await _gitLabApi.GetGroupDetail(groupId)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpGet]
        public async Task<Result> GroupProjects(int groupId, string? search = null, int page = 1, int perPage = 20)
        {
            try { return Result.Success(await _gitLabApi.GetGroupProjects(groupId, search, page, perPage)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> CreateGroup([FromBody] CreateGroupInput input)
        {
            try { return Result.Success("群组创建成功", await _gitLabApi.CreateGroup(input.Name, input.Path, input.Description, input.Visibility)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 项目

        [HttpGet]
        public async Task<Result> Projects(string? search = null, int page = 1, int perPage = 20)
        {
            try { return Result.Success(await _gitLabApi.GetProjects(search, page, perPage)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpGet]
        public async Task<Result> ProjectDetail(int projectId)
        {
            try { return Result.Success(await _gitLabApi.GetProjectDetail(projectId)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> CreateProject([FromBody] CreateGitLabProjectInput input)
        {
            try { return Result.Success("项目创建成功", await _gitLabApi.CreateProject(input.NamespaceId, input.Name, input.Path, input.Description, input.Visibility)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 分支

        [HttpGet]
        public async Task<Result> Branches(int projectId, string? search = null)
        {
            try { return Result.Success(await _gitLabApi.GetBranches(projectId, search)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> CreateBranch(int projectId, string branchName, string fromRef)
        {
            try { return Result.Success("分支创建成功", await _gitLabApi.CreateBranch(projectId, branchName, fromRef)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> DeleteBranch(int projectId, string branchName)
        {
            try { await _gitLabApi.DeleteBranch(projectId, branchName); return Result.Success("分支删除成功"); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 文件树 & 文件

        [HttpGet]
        public async Task<Result> Tree(int projectId, string? path = null, string? refName = null, bool recursive = false)
        {
            try { return Result.Success(await _gitLabApi.GetTree(projectId, path, refName, recursive)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpGet]
        public async Task<Result> FileContent(int projectId, string filePath, string? refName = null)
        {
            try { return Result.Success(await _gitLabApi.GetFileContent(projectId, filePath, refName)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> CreateFile(int projectId, string filePath, [FromBody] FileOperationInput input)
        {
            try { return Result.Success("文件创建成功", await _gitLabApi.CreateFile(projectId, filePath, input.Content, input.Branch, input.CommitMessage)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> UpdateFile(int projectId, string filePath, [FromBody] FileOperationInput input)
        {
            try { return Result.Success("文件更新成功", await _gitLabApi.UpdateFile(projectId, filePath, input.Content, input.Branch, input.CommitMessage)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> DeleteFile(int projectId, string filePath, [FromBody] FileDeleteInput input)
        {
            try { await _gitLabApi.DeleteFile(projectId, filePath, input.Branch, input.CommitMessage); return Result.Success("文件删除成功"); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 提交

        [HttpGet]
        public async Task<Result> Commits(int projectId, string? refName = null, string? path = null, int page = 1, int perPage = 20)
        {
            try { return Result.Success(await _gitLabApi.GetCommits(projectId, refName, path, page, perPage)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpGet]
        public async Task<Result> CommitDetail(int projectId, string sha)
        {
            try { return Result.Success(await _gitLabApi.GetCommitDetail(projectId, sha)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> CreateCommit(int projectId, [FromBody] CreateCommitInput input)
        {
            try { return Result.Success("提交成功", await _gitLabApi.CreateCommit(projectId, input.Branch, input.CommitMessage, input.Actions)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 合并请求

        [HttpGet]
        public async Task<Result> MergeRequests(int projectId, string? state = null, int page = 1, int perPage = 20)
        {
            try { return Result.Success(await _gitLabApi.GetMergeRequests(projectId, state, page, perPage)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpGet]
        public async Task<Result> MergeRequestDetail(int projectId, int mrIid)
        {
            try { return Result.Success(await _gitLabApi.GetMergeRequestDetail(projectId, mrIid)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> CreateMergeRequest(int projectId, [FromBody] CreateMergeRequestInput input)
        {
            try { return Result.Success("合并请求创建成功", await _gitLabApi.CreateMergeRequest(projectId, input.SourceBranch, input.TargetBranch, input.Title, input.Description)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> AcceptMergeRequest(int projectId, int mrIid, bool squash = false, bool removeSourceBranch = false)
        {
            try { return Result.Success("合并成功", await _gitLabApi.AcceptMergeRequest(projectId, mrIid, squash, removeSourceBranch)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 标签

        [HttpGet]
        public async Task<Result> Tags(int projectId, string? search = null, int page = 1, int perPage = 20)
        {
            try { return Result.Success(await _gitLabApi.GetTags(projectId, search, page, perPage)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> CreateTag(int projectId, string tagName, string refName, string? message = null)
        {
            try { return Result.Success("标签创建成功", await _gitLabApi.CreateTag(projectId, tagName, refName, message)); }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        #endregion
    }

    #region Input Models

    public class FileOperationInput
    {
        public string Content { get; set; } = "";
        public string Branch { get; set; } = "main";
        public string CommitMessage { get; set; } = "";
    }

    public class FileDeleteInput
    {
        public string Branch { get; set; } = "main";
        public string CommitMessage { get; set; } = "";
    }

    public class CreateCommitInput
    {
        public string Branch { get; set; } = "main";
        public string CommitMessage { get; set; } = "";
        public List<CommitAction> Actions { get; set; } = new();
    }

    public class CreateMergeRequestInput
    {
        public string SourceBranch { get; set; } = "";
        public string TargetBranch { get; set; } = "main";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
    }

    public class CreateGroupInput
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Description { get; set; }
        public string Visibility { get; set; } = "private";
    }

    public class CreateGitLabProjectInput
    {
        public int NamespaceId { get; set; }
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Description { get; set; }
        public string Visibility { get; set; } = "private";
    }

    #endregion
}

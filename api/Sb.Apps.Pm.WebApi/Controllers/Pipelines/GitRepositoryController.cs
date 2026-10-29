using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>Git 仓库管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class GitRepositoryController : Controller
{
    private readonly GitRepositoryService _service;
    public GitRepositoryController(GitRepositoryService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] string? keyword, [FromQuery] string field = "",
        [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, keyword, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] GitRepository repo) => _service.Create(repo);

    [HttpPost]
    public Result Edit([FromForm] GitRepository repo) => _service.Edit(repo);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    [HttpPost]
    public Result Sync([FromForm] int id) => _service.Sync(id);

    [HttpGet]
    public async Task<Result> Branches([FromQuery] int id) => await _service.GetBranches(id);

    [HttpGet]
    public async Task<Result> Commits([FromQuery] int id, [FromQuery] string? branch, [FromQuery] int limit = 20)
        => await _service.GetCommits(id, branch, limit);
}

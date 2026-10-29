using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>Git 流水线管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class GitPipelineController : Controller
{
    private readonly GitPipelineService _service;
    public GitPipelineController(GitPipelineService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? repositoryId, [FromQuery] PipelineStatus? status, [FromQuery] string field = "",
        [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(repositoryId, status, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);
}

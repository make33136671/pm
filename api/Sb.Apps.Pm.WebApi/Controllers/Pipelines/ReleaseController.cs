using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>发布管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ReleaseController : Controller
{
    private readonly ReleaseService _service;
    public ReleaseController(ReleaseService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] int? environmentId, [FromQuery] ReleaseStatus? status,
        [FromQuery] string field = "", [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, environmentId, status, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Create([FromForm] ReleaseRecord record) => _service.Create(record);

    [HttpPost]
    public Result Approve([FromForm] int id, [FromForm] string? comment) => _service.Approve(id, comment);

    [HttpPost]
    public Result Reject([FromForm] int id, [FromForm] string? comment) => _service.Reject(id, comment);

    [HttpPost]
    public Result Deploy([FromForm] int id) => _service.UpdateDeployStatus(id, ReleaseStatus.Deploying);

    [HttpPost]
    public Result Rollback([FromForm] int releaseId) => _service.Rollback(releaseId);
}

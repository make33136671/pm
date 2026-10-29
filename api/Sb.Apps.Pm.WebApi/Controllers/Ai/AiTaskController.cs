using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Ai;

/// <summary>AI 任务管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class AiTaskController : Controller
{
    private readonly AiTaskService _service;
    public AiTaskController(AiTaskService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] AiTaskType? taskType, [FromQuery] AiTaskStatus? status,
        [FromQuery] string field = "", [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, taskType, status, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Create([FromForm] AiTask task) => _service.Create(task);

    [HttpPost]
    public Result Cancel([FromForm] int id) => _service.Cancel(id);
}

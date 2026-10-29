using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Ai;

/// <summary>AI 记忆管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class AiMemoryController : Controller
{
    private readonly AiMemoryService _service;
    public AiMemoryController(AiMemoryService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] AiMemoryScope? scope, [FromQuery] string? category,
        [FromQuery] string? keyword, [FromQuery] string field = "", [FromQuery] string order = "",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, scope, category, keyword, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] AiMemory memory) => _service.Create(memory);

    [HttpPost]
    public Result Edit([FromForm] AiMemory memory) => _service.Edit(memory);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    [HttpGet]
    public Result Search([FromQuery] string keyword, [FromQuery] AiMemoryScope? scope,
        [FromQuery] int? projectId, [FromQuery] int limit = 20)
        => _service.Search(keyword, scope, projectId, limit);
}

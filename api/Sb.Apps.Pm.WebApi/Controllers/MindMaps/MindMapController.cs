using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.MindMaps;

/// <summary>思维导图管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class MindMapController : Controller
{
    private readonly MindMapService _service;
    public MindMapController(MindMapService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] MindMapType? type, [FromQuery] string field = "",
        [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, type, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    /// <summary>获取项目主导图（不存在则自动创建）</summary>
    [HttpGet]
    public Result GetMain([FromQuery] int projectId) => _service.GetMain(projectId);

    /// <summary>从需求同步主导图</summary>
    [HttpPost]
    public Result SyncMain([FromForm] int projectId) => _service.SyncMain(projectId);

    [HttpPost]
    public Result Add([FromForm] MindMap mindMap) => _service.Create(mindMap);

    [HttpPost]
    public Result Edit([FromForm] MindMap mindMap) => _service.Edit(mindMap);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    [HttpPost]
    public Result GenerateRequirementMap([FromForm] int projectId) => _service.GenerateRequirementMap(projectId);
}

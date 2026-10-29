using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Ai;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>更新日志管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ChangelogController : Controller
{
    private readonly ChangelogService _service;
    private readonly IAiProvider? _aiProvider;

    public ChangelogController(ChangelogService service, IAiProvider? aiProvider = null)
    {
        _service = service;
        _aiProvider = aiProvider;
    }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] string? keyword, [FromQuery] string field = "",
        [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, keyword, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] Changelog changelog) => _service.Create(changelog);

    [HttpPost]
    public Result Edit([FromForm] Changelog changelog) => _service.Edit(changelog);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    /// <summary>AI 读取已完成任务列表并生成变更日志（无 AI Provider 时返回结构化模板）。</summary>
    [HttpPost]
    public Task<Result> AiGenerate([FromForm] int projectId, [FromForm] string version,
        [FromForm] DateTime? since = null, [FromForm] int? releaseRecordId = null)
        => _service.GenerateByAi(projectId, version, _aiProvider, since, releaseRecordId);
}

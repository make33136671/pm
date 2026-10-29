using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Ai;

/// <summary>AI 管线配置</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class AiPipelineController : Controller
{
    private readonly AiPipelineService _service;
    public AiPipelineController(AiPipelineService service) { _service = service; }

    /// <summary>获取群组的管线配置（含步骤）</summary>
    [HttpGet]
    public Result GetByGroup([FromQuery] int groupBindingId)
        => _service.GetByGroupBinding(groupBindingId);

    /// <summary>切换管线开关</summary>
    [HttpPost]
    public Result ToggleEnabled([FromForm] int pipelineId, [FromForm] bool enabled)
        => _service.ToggleEnabled(pipelineId, enabled);

    /// <summary>切换实时分析开关</summary>
    [HttpPost]
    public Result ToggleRealtime([FromForm] int pipelineId, [FromForm] bool realtimeEnabled)
        => _service.ToggleRealtime(pipelineId, realtimeEnabled);

    /// <summary>添加管线步骤</summary>
    [HttpPost]
    public Result AddStep([FromForm] AiPipelineStep step) => _service.AddStep(step);

    /// <summary>编辑管线步骤</summary>
    [HttpPost]
    public Result EditStep([FromForm] AiPipelineStep step) => _service.EditStep(step);

    /// <summary>删除管线步骤</summary>
    [HttpPost]
    public Result DeleteStep([FromForm] int stepId) => _service.DeleteStep(stepId);
}

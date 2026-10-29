using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Requirements;
using SB.Feature.AccessToken.Services;
using SB.Framework.Core.Authentication;
using Sb.Apps.Pm.Services.Requirements;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

[ApiController]
[Authorize(AuthenticationSchemes = "JwtAdmin")]
[Scope("write:requirement")]   // Controller 级默认覆盖 POST 端点;GET 端点用 Action 级 [Scope("read:requirement")] 覆盖
[Route("/agent/requirement/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentRequirementController : ControllerBase
{
    private readonly RequirementService _service;
    private readonly IApiKeyContext _agentCtx;
    private readonly IAiProvider? _aiProvider;

    public AgentRequirementController(RequirementService service, IApiKeyContext agentCtx, IAiProvider? aiProvider = null)
    {
        _service = service;
        _agentCtx = agentCtx;
        _aiProvider = aiProvider;
    }

    [HttpGet, Scope("read:requirement")]
    public Result List(int? projectId = null, RequirementStatus? status = null,
        RequirementType? type = null, string? keyword = null, string? batchId = null,
        int? parentId = null, int? customerId = null,
        string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetList(projectId, status, type, keyword, batchId, parentId, null, customerId,
            field, order, page, pageSize);

    [HttpGet, Scope("read:requirement")]
    public Result Detail(int id) => _service.Detail(id);

    [HttpPost]
    public Result Create([FromForm] Requirement req)
    {
        try
        {
            return _service.Create(req);
        }
        catch (Exception ex)
        {
            return Result.Fail($"创建需求失败: {ex.Message}");
        }
    }

    [HttpPost]
    public Result Update([FromForm] Requirement req) => _service.Edit(req);

    [HttpPost]
    public Result Delete([FromForm] int id) => _service.Del(id);

    [HttpPost]
    public Result UpdateStatus([FromForm] int id, [FromForm] RequirementStatus status)
        => _service.BatchUpdateStatus(new List<int> { id }, status);

    [HttpPost]
    public Result AddComment(
        [FromForm] int requirementId,
        [FromForm] string content,
        [FromServices] RequirementCommentService commentService)
    {
        var c = new RequirementComment
        {
            RequirementId = requirementId,
            Content = content
        };
        return commentService.Create(c);
    }

    /// <summary>AI 将需求拆解为任务暂存，返回 batchId 供后续确认。</summary>
    [HttpPost]
    public Task<Result> AiSplitTasks([FromForm] int requirementId)
        => _service.SplitTasks(requirementId, _aiProvider);
}

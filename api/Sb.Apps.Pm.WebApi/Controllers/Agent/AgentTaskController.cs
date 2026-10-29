using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using SB.Feature.AccessToken.Services;
using SB.Framework.Core.Authentication;
using Sb.Apps.Pm.Services.Tasks;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

[ApiController]
[Authorize(AuthenticationSchemes = "JwtAdmin")]
[Scope("write:task")]   // Controller 级默认覆盖 POST;GET 用 [Scope("read:task")] 覆盖
[Route("/agent/task/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentTaskController : ControllerBase
{
    private readonly TaskItemService _service;
    private readonly IApiKeyContext _agentCtx;

    public AgentTaskController(TaskItemService service, IApiKeyContext agentCtx)
    {
        _service = service;
        _agentCtx = agentCtx;
    }

    [HttpGet, Scope("read:task")]
    public Result List(int? projectId = null, int? requirementId = null,
        int? featureModuleId = null, TaskItemClaimStatus? claimStatus = null,
        TaskItemExecStatus? execStatus = null, TaskItemType? type = null,
        string? assignee = null, string? keyword = null,
        string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetList(projectId, requirementId, featureModuleId, claimStatus, execStatus,
            type, assignee, keyword, field, order, page, pageSize);

    [HttpGet, Scope("read:task")]
    public Result Detail(int id) => _service.Detail(id);

    [HttpGet, Scope("read:task")]
    public Result Available(int? projectId = null) => _service.GetAvailable(projectId);

    [HttpGet, Scope("read:task")]
    public Result Logs(int taskItemId, int page = 1, int pageSize = 20)
        => _service.GetLogs(taskItemId, page, pageSize);

    [HttpPost]
    public Result Claim([FromForm] int? projectId = null)
        => _service.ClaimNext(_agentCtx.KeyName, projectId);

    [HttpPost]
    public Result Start([FromForm] int id)
        => _service.ReportStart(id, _agentCtx.KeyName);

    [HttpPost]
    public Result Complete([FromForm] int id, [FromForm] string? message = null)
        => _service.ReportSuccess(id, _agentCtx.KeyName, message);

    [HttpPost]
    public Result Fail([FromForm] int id, [FromForm] string message)
        => _service.ReportFail(id, _agentCtx.KeyName, message);

    [HttpPost]
    public Result Cancel([FromForm] int id) => _service.Cancel(id);

    [HttpPost]
    public Result Resume([FromForm] int id) => _service.Resume(id);
}

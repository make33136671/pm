using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;

namespace Sb.Apps.Pm.WebApi.Controllers.Tasks;

/// <summary>
/// Agent 接单公开 API — 使用 X-Api-Key Header 认证
/// </summary>
[ApiController, Route("/api/task"), AllowJsonBody]
public class TaskAgentController : Controller
{
    private readonly TaskItemService _service;
    private readonly SystemSettingService _settingService;

    public TaskAgentController(TaskItemService service, SystemSettingService settingService)
    {
        _service = service;
        _settingService = settingService;
    }

    /// <summary>接单：获取并锁定一个待处理任务</summary>
    [HttpPost("claim")]
    public Result Claim([FromBody] AgentClaimRequest req)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return _service.ClaimNext(req.AgentName, req.ProjectId);
    }

    /// <summary>上报开始执行</summary>
    [HttpPost("{id}/start")]
    public Result Start(int id, [FromBody] AgentBaseRequest req)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return _service.ReportStart(id, req.AgentName);
    }

    /// <summary>上报执行成功</summary>
    [HttpPost("{id}/success")]
    public Result Success(int id, [FromBody] AgentResultRequest req)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return _service.ReportSuccess(id, req.AgentName, req.Message);
    }

    /// <summary>上报执行失败</summary>
    [HttpPost("{id}/fail")]
    public Result Fail(int id, [FromBody] AgentResultRequest req)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return _service.ReportFail(id, req.AgentName, req.Message ?? "未知错误");
    }

    /// <summary>上报执行超时</summary>
    [HttpPost("{id}/timeout")]
    public Result Timeout(int id, [FromBody] AgentBaseRequest req)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return _service.ReportTimeout(id, req.AgentName);
    }

    /// <summary>查看可接单任务列表</summary>
    [HttpGet("available")]
    public Result Available(int? projectId = null)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return _service.GetAvailable(projectId);
    }

    /// <summary>提交验收 — Running → PendingReview，必填修复说明，可附带 commit（#107）</summary>
    [HttpPost("{id}/submit-review")]
    public async Task<Result> SubmitForReview(int id, [FromBody] AgentSubmitReviewRequest req)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return await _service.SubmitForReview(id, req.AgentName, req.FixedDescription, req.CommitSha, req.CommitBranch);
    }

    /// <summary>验收通过 — PendingReview → Success（#107）</summary>
    [HttpPost("{id}/review-approve")]
    public Result ReviewApprove(int id, [FromBody] AgentResultRequest req)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return _service.ReviewApprove(id, req.AgentName, req.Message);
    }

    /// <summary>退回重修 — PendingReview → Pending/Idle，必填退回原因（#107）</summary>
    [HttpPost("{id}/review-reject")]
    public Result ReviewReject(int id, [FromBody] AgentResultRequest req)
    {
        if (!ValidateApiKey()) return Result.Fail("API Key 无效", 401);
        return _service.ReviewReject(id, req.AgentName, req.Message ?? "退回重修");
    }

    private bool ValidateApiKey()
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey)) return false;

        var configuredKey = _settingService.GetSystemSettingByKey<string>("TaskAgentApiKey");
        return !string.IsNullOrEmpty(configuredKey) && apiKey == configuredKey;
    }
}

public class AgentBaseRequest
{
    public string AgentName { get; set; } = "";
}

public class AgentClaimRequest : AgentBaseRequest
{
    public int? ProjectId { get; set; }
}

public class AgentResultRequest : AgentBaseRequest
{
    public string? Message { get; set; }
}

public class AgentSubmitReviewRequest : AgentBaseRequest
{
    public string FixedDescription { get; set; } = "";
    /// <summary>交付的 Git commit SHA（可选）</summary>
    public string? CommitSha { get; set; }
    /// <summary>提交所在分支（可选）</summary>
    public string? CommitBranch { get; set; }
}

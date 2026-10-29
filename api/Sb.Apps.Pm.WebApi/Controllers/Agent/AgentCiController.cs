using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Feature.AccessToken.Services;
using SB.Framework.Core.Authentication;
using Sb.Apps.Pm.Services.Pipelines;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

/// <summary>
/// 给 AI / Agent 调用的 CI 流水线接口（访问令牌:Authorization: Bearer sk_ key + [Scope] 鉴权）。
/// 触发流水线、查状态 / Job / 日志、看测试报告、把结果回写关联到需求 / 任务。
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = "JwtAdmin")]
[Scope("write:ci")] // Controller 级默认覆盖写操作；只读 Action 用 [Scope("read:ci")] 覆盖
[Route("/agent/ci/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentCiController : ControllerBase
{
    private readonly PipelineProfileService _profile;
    private readonly BuildRecordService _build;

    public AgentCiController(PipelineProfileService profile, BuildRecordService build)
    {
        _profile = profile;
        _build = build;
    }

    /// <summary>
    /// 触发一条流水线。autoApprove=true（默认）会在创建构建后立即审批通过并真正调 GitLab 跑起来；
    /// false 则只创建待审批构建，需人工在后台审批。
    /// </summary>
    [HttpPost]
    public async Task<Result> Trigger([FromForm] int profileId, [FromForm] string? branch = null,
        [FromForm] string? version = null, [FromForm] bool autoApprove = true)
    {
        var triggered = _profile.TriggerBuild(profileId, branch, version);
        if (triggered.Code != 0) return triggered;

        if (!autoApprove) return triggered;

        // 从 TriggerBuild 的返回数据里取出 build_id，自动审批触发 GitLab Pipeline
        int? buildId = TryGetBuildId(triggered.Data);
        if (buildId is null or 0)
            return Result.Fail("触发成功但未能解析 build_id，无法自动审批");

        var approved = await _profile.ApproveBuild(buildId.Value, "AI 自动审批触发");
        return approved;
    }

    /// <summary>查询构建 / 流水线状态</summary>
    [HttpGet, Scope("read:ci")]
    public Result Status(int buildId) => _profile.GetStatus(buildId, null);

    /// <summary>获取流水线所有 Job（含状态、stage）</summary>
    [HttpGet, Scope("read:ci")]
    public Task<Result> Jobs(int buildId) => _build.GetJobs(buildId);

    /// <summary>获取某个 Job 的控制台日志</summary>
    [HttpGet, Scope("read:ci")]
    public Task<Result> JobTrace(int buildId, int jobId) => _build.GetJobTrace(buildId, jobId);

    /// <summary>获取 JUnit 测试报告明细（通过 / 失败用例）</summary>
    [HttpGet, Scope("read:ci")]
    public Task<Result> TestReport(int buildId, bool refresh = false) => _build.GetTestReport(buildId, refresh);

    /// <summary>把本次 CI 结果关联到需求 / 任务</summary>
    [HttpPost]
    public Result Link([FromForm] int buildId, [FromForm] int? requirementId, [FromForm] int? taskItemId)
        => _build.LinkTarget(buildId, requirementId, taskItemId);

    private static int? TryGetBuildId(object? data)
    {
        if (data == null) return null;
        var prop = data.GetType().GetProperty("build_id");
        var val = prop?.GetValue(data);
        return val == null ? null : Convert.ToInt32(val);
    }
}

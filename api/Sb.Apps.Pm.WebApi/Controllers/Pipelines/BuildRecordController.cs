using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>构建记录管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class BuildRecordController : Controller
{
    private readonly BuildRecordService _service;
    public BuildRecordController(BuildRecordService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] BuildStatus? status, [FromQuery] string field = "",
        [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, status, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] BuildRecord record) => _service.Create(record);

    /// <summary>获取构建对应 Pipeline 的所有 Job（状态轮询）</summary>
    [HttpGet]
    public Task<Result> Jobs([FromQuery] int buildId) => _service.GetJobs(buildId);

    /// <summary>获取某 Job 的控制台日志</summary>
    [HttpGet]
    public Task<Result> JobTrace([FromQuery] int buildId, [FromQuery] int jobId) => _service.GetJobTrace(buildId, jobId);

    /// <summary>获取 Pipeline 的 JUnit 测试报告明细</summary>
    [HttpGet]
    public Task<Result> TestReport([FromQuery] int buildId, [FromQuery] bool refresh = false) => _service.GetTestReport(buildId, refresh);

    /// <summary>把 CI 结果关联到需求 / 任务</summary>
    [HttpPost]
    public Result Link([FromForm] int buildId, [FromForm] int? requirementId, [FromForm] int? taskItemId)
        => _service.LinkTarget(buildId, requirementId, taskItemId);
}

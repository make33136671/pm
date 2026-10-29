using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>流水线配置预设管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
public class PipelineProfileController : Controller
{
    private readonly PipelineProfileService _service;
    public PipelineProfileController(PipelineProfileService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.GetDetail(id);

    /// <summary>某子项目的所有部署方案 + 最近构建/部署状态</summary>
    [HttpGet]
    public Result BySubProject([FromQuery] int subProjectId) => _service.GetSchemesBySubProject(subProjectId);

    /// <summary>一键 CI/CD:构建 →(无需审批则)部署。立即返回 build_id,前端轮询 Status</summary>
    [HttpPost]
    public Result OneClickDeploy([FromBody] OneClickDeployDto dto)
        => _service.OneClickDeploy(dto.ProfileId, dto.Branch, dto.Version);

    [HttpPost]
    public Result Add([FromBody] PipelineProfile profile) => _service.Add(profile);

    [HttpPost]
    public Result Edit([FromBody] PipelineProfile profile) => _service.Edit(profile);

    [HttpPost]
    public Result Del([FromBody] IdDto dto) => _service.Del(dto.Id);

    /// <summary>
    /// 触发构建（提交构建审批）
    /// </summary>
    [HttpPost]
    public Result TriggerBuild([FromBody] TriggerBuildDto dto)
        => _service.TriggerBuild(dto.ProfileId, dto.Branch, dto.Version, dto.Variables);

    /// <summary>
    /// 审批通过构建
    /// </summary>
    [HttpPost]
    public async Task<Result> ApproveBuild([FromBody] ApproveDto dto)
        => await _service.ApproveBuild(dto.TargetId, dto.Comment);

    /// <summary>
    /// 驳回构建
    /// </summary>
    [HttpPost]
    public Result RejectBuild([FromBody] ApproveDto dto)
        => _service.RejectBuild(dto.TargetId, dto.Comment);

    // ==================== 部署 ====================

    /// <summary>触发部署（提交部署审批）</summary>
    [HttpPost]
    public Result TriggerDeploy([FromBody] TriggerDeployDto dto)
        => _service.TriggerDeploy(dto.BuildId, dto.ServerIds);

    /// <summary>审批通过部署</summary>
    [HttpPost]
    public async Task<Result> ApproveDeploy([FromBody] ApproveDto dto)
        => await _service.ApproveDeploy(dto.TargetId, dto.Comment);

    /// <summary>驳回部署</summary>
    [HttpPost]
    public Result RejectDeploy([FromBody] ApproveDto dto)
        => _service.RejectDeploy(dto.TargetId, dto.Comment);

    // ==================== 回滚 ====================

    /// <summary>发起回滚</summary>
    [HttpPost]
    public Result TriggerRollback([FromBody] TriggerRollbackDto dto)
        => _service.TriggerRollback(dto.ProfileId, dto.TargetVersion);

    // ==================== 查询 ====================

    /// <summary>查看历史版本</summary>
    [HttpGet]
    public Result Versions([FromQuery] int profileId, [FromQuery] int limit = 10)
        => _service.GetVersions(profileId, limit);

    /// <summary>查询构建或部署状态</summary>
    [HttpGet]
    public Result Status([FromQuery] int? buildId, [FromQuery] int? deployId)
        => _service.GetStatus(buildId, deployId);
}

public class TriggerBuildDto
{
    public int ProfileId { get; set; }
    public string? Branch { get; set; }
    public string? Version { get; set; }
    public Dictionary<string, string>? Variables { get; set; }
}

public class OneClickDeployDto
{
    public int ProfileId { get; set; }
    public string? Branch { get; set; }
    public string? Version { get; set; }
}

public class ApproveDto
{
    public int TargetId { get; set; }
    public string? Comment { get; set; }
}

public class TriggerDeployDto
{
    public int BuildId { get; set; }
    public List<int>? ServerIds { get; set; }
}

public class TriggerRollbackDto
{
    public int ProfileId { get; set; }
    public string? TargetVersion { get; set; }
}

public class IdDto
{
    public int Id { get; set; }
}

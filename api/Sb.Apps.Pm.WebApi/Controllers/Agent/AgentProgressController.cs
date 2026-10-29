using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using SB.Feature.AccessToken.Services;
using SB.Framework.Core.Authentication;
using Sb.Apps.Pm.Services.Pipelines;
using DashboardService = Sb.Apps.Pm.Services.Projects.DashboardService;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

[ApiController]
[Authorize(AuthenticationSchemes = "JwtAdmin")]
[Scope("read:progress")]
[Route("/agent/progress/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentProgressController : ControllerBase
{
    private readonly DashboardService _dashboard;
    private readonly BuildRecordService _build;
    private readonly ReleaseService _release;
    private readonly ChangelogService _changelog;

    public AgentProgressController(
        DashboardService dashboard,
        BuildRecordService build,
        ReleaseService release,
        ChangelogService changelog)
    {
        _dashboard = dashboard;
        _build = build;
        _release = release;
        _changelog = changelog;
    }

    [HttpGet]
    public Result HealthScore(int projectId) => _dashboard.HealthScore(projectId);

    [HttpGet]
    public Result Builds(int? projectId = null, BuildStatus? status = null,
        int page = 1, int pageSize = 20)
        => _build.GetList(projectId, status, "", "", page, pageSize);

    [HttpGet]
    public Result Releases(int? projectId = null, int? environmentId = null,
        ReleaseStatus? status = null, int page = 1, int pageSize = 20)
        => _release.GetList(projectId, environmentId, status, "", "", page, pageSize);

    [HttpGet]
    public Result Changelog(int? projectId = null, string? keyword = null,
        int page = 1, int pageSize = 20)
        => _changelog.GetList(projectId, keyword, "", "", page, pageSize);
}

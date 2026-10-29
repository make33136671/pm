using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using SB.Feature.AccessToken.Services;
using SB.Framework.Core.Authentication;
using Sb.Apps.Pm.Services.Projects;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

[ApiController]
[Authorize(AuthenticationSchemes = "JwtAdmin")]
[Scope("read:project")]
[Route("/agent/project/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentProjectController : ControllerBase
{
    private readonly ProjectService _projectService;
    private readonly SubProjectService _subService;

    public AgentProjectController(ProjectService projectService, SubProjectService subService)
    {
        _projectService = projectService;
        _subService = subService;
    }

    [HttpGet]
    public Result List(string? keyword = null, ProjectStatus? status = null,
        int page = 1, int pageSize = 20)
        => _projectService.GetList(keyword, status, "", "", page, pageSize);

    [HttpGet]
    public Result Detail(int id) => _projectService.Detail(id);

    [HttpGet]
    public Result SubList(int projectId) => _subService.GetList(projectId);

    [HttpPost]
    public Result Create([FromForm] string name, [FromForm] string? code = null,
        [FromForm] string? description = null, [FromForm] ProjectStatus? status = null)
        => _projectService.Create(new Project
        {
            Name = name,
            Code = code,
            Description = description,
            Status = status ?? ProjectStatus.Planning,
        });
}

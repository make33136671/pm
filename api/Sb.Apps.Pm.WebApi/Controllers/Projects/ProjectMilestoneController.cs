using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProjectMilestoneController : Controller
{
    private readonly ProjectMilestoneService _service;
    public ProjectMilestoneController(ProjectMilestoneService service) { _service = service; }

    [HttpGet]
    public Result List(int projectId, string field = "", string order = "", int page = 1, int pageSize = 50)
        => _service.GetList(projectId, field, order, page, pageSize);

    [HttpPost]
    public Result Add([FromForm] ProjectMilestone milestone) => _service.Create(milestone);

    [HttpPost]
    public Result Edit([FromForm] ProjectMilestone milestone) => _service.Edit(milestone);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProjectActivityController : Controller
{
    private readonly ProjectActivityService _service;
    public ProjectActivityController(ProjectActivityService service) { _service = service; }

    [HttpGet]
    public Result List(int projectId, int page = 1, int pageSize = 20) => _service.GetList(projectId, page, pageSize);
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProjectEnvironmentController : Controller
{
    private readonly ProjectEnvironmentService _service;
    public ProjectEnvironmentController(ProjectEnvironmentService service) { _service = service; }

    [HttpGet]
    public Result List(int projectId, string field = "", string order = "", int page = 1, int pageSize = 50)
        => _service.GetList(projectId, field, order, page, pageSize);

    [HttpPost]
    public Result Add([FromForm] ProjectEnvironment env) => _service.Create(env);

    [HttpPost]
    public Result Edit([FromForm] ProjectEnvironment env) => _service.Edit(env);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

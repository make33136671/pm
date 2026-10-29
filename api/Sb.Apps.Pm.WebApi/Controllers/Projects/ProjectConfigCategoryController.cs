using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProjectConfigCategoryController : Controller
{
    private readonly ProjectConfigCategoryService _service;
    public ProjectConfigCategoryController(ProjectConfigCategoryService service) { _service = service; }

    [HttpGet]
    public Result List(string field = "", string order = "", int page = 1, int pageSize = 50)
        => _service.GetList(field, order, page, pageSize);

    [HttpPost]
    public Result Add([FromForm] ProjectConfigCategory category) => _service.Create(category);

    [HttpPost]
    public Result Edit([FromForm] ProjectConfigCategory category) => _service.Edit(category);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

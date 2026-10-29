using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProjectTemplateController : Controller
{
    private readonly ProjectTemplateService _service;
    public ProjectTemplateController(ProjectTemplateService service) { _service = service; }

    [HttpGet]
    public Result List(string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetList(field, order, page, pageSize);

    [HttpGet]
    public Result Detail(int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] ProjectTemplate template) => _service.Create(template);

    [HttpPost]
    public Result Edit([FromForm] ProjectTemplate template) => _service.Edit(template);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

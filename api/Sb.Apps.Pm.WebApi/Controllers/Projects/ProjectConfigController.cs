using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
public class ProjectConfigController : Controller
{
    private readonly ProjectConfigService _service;
    public ProjectConfigController(ProjectConfigService service) { _service = service; }

    [HttpGet]
    public Result List(int projectId, int? environmentId = null, int? categoryId = null)
        => _service.GetList(projectId, environmentId, categoryId);

    [HttpPost]
    public Result Save([FromBody] List<ProjectConfig> configs) => _service.Save(configs);

    [HttpPost]
    public Result InitFromTemplate([FromForm] int projectId, [FromForm] int templateId)
        => _service.InitFromTemplate(projectId, templateId);

    [HttpGet]
    public Result DecryptValue(int configId) => _service.DecryptValue(configId);
}

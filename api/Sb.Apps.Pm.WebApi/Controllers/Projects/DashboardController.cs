using DashboardService = Sb.Apps.Pm.Services.Projects.DashboardService;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class DashboardController : Controller
{
    private readonly DashboardService _service;
    public DashboardController(DashboardService service) { _service = service; }

    [HttpGet]
    public Result Workbench() => _service.Workbench();

    [HttpGet]
    public Result TodoList(int page = 1, int pageSize = 20) => _service.TodoList(page, pageSize);

    [HttpGet]
    public Result CompletedList(int page = 1, int pageSize = 20) => _service.CompletedList(page, pageSize);

    [HttpGet]
    public Result HealthScore([FromQuery] int projectId) => _service.HealthScore(projectId);

    [HttpGet]
    public Result OptimizationSuggestions() => _service.OptimizationSuggestions();
}

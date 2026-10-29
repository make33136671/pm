using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

/// <summary>客户管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProjectCustomerController : Controller
{
    private readonly ProjectCustomerService _service;
    public ProjectCustomerController(ProjectCustomerService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] CustomerStatus? status, [FromQuery] string? keyword,
        [FromQuery] string field = "", [FromQuery] string order = "",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, status, keyword, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] ProjectCustomer customer) => _service.Create(customer);

    [HttpPost]
    public Result Edit([FromForm] ProjectCustomer customer) => _service.Edit(customer);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

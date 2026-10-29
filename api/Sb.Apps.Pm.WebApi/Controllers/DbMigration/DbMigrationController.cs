using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.DbMigration;

/// <summary>数据库迁移脚本管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class DbMigrationController : Controller
{
    private readonly DbMigrationScriptService _service;
    public DbMigrationController(DbMigrationScriptService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] int? environmentId, [FromQuery] MigrationStatus? status,
        [FromQuery] string field = "", [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, environmentId, status, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] DbMigrationScript script) => _service.Create(script);

    [HttpPost]
    public Result Edit([FromForm] DbMigrationScript script) => _service.Edit(script);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    [HttpPost]
    public Result Execute([FromForm] int id) => _service.Execute(id);
}

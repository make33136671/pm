using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.ApiEndpoints;
using Sb.Apps.Pm.Services.ApiEndpoints;

namespace Sb.Apps.Pm.WebApi.Controllers.ApiEndpoints;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
public class ApiEndpointController : Controller
{
    private readonly ApiEndpointService _service;

    public ApiEndpointController(ApiEndpointService service)
    {
        _service = service;
    }

    [HttpGet]
    public Result List(int? projectId = null, ApiSide? side = null, ApiEndpointStatus? status = null,
        int? featureModuleId = null, string? keyword = null,
        string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetList(projectId, side, status, featureModuleId, keyword, field, order, page, pageSize);

    [HttpGet]
    public Result Detail(int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] ApiEndpoint endpoint) => _service.Create(endpoint);

    [HttpPost]
    public Result Edit([FromForm] ApiEndpoint endpoint) => _service.Edit(endpoint);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    /// <summary>保存接口的功能模块关联（覆盖式）。apiEndpointId 走 query，body 是功能模块 id 数组</summary>
    [HttpPost]
    public Result SaveRelations(int apiEndpointId, [FromBody] List<int> featureModuleIds)
        => _service.SaveRelations(apiEndpointId, featureModuleIds);

    /// <summary>获取接口关联的功能模块 id 列表</summary>
    [HttpGet]
    public Result GetRelations(int apiEndpointId) => _service.GetRelations(apiEndpointId);
}

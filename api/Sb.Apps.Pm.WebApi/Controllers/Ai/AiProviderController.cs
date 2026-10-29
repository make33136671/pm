using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;

namespace Sb.Apps.Pm.WebApi.Controllers.Ai;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class AiProviderController : Controller
{
    private readonly AiProviderService _service;

    public AiProviderController(AiProviderService service) { _service = service; }

    [HttpGet]
    public Result List(string? keyword = null, string? providerType = null,
        string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetList(keyword, providerType, field, order, page, pageSize);

    [HttpGet]
    public Result Detail(int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] AiProvider entity) => _service.Create(entity);

    [HttpPost]
    public Result Edit([FromForm] AiProvider entity) => _service.Edit(entity);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    /// <summary>获取已注册的适配器类型列表</summary>
    [HttpGet]
    public Result AdapterTypes() => _service.GetAdapterTypes();

    /// <summary>获取可用的 AI 提供商（Active + 有适配器）</summary>
    [HttpGet]
    public Result Available() => _service.GetAvailable();
}

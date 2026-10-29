using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Chat;

/// <summary>群组绑定管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ChatGroupController : Controller
{
    private readonly ChatGroupBindingService _service;

    public ChatGroupController(ChatGroupBindingService service)
    {
        _service = service;
    }

    [HttpGet]
    public Result List([FromQuery] int? accountId, [FromQuery] int? projectId, [FromQuery] ChatGroupBindingStatus? status,
        [FromQuery] string field = "", [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(accountId, projectId, status, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] ChatGroupBinding binding) => _service.Create(binding);

    [HttpPost]
    public Result Edit([FromForm] ChatGroupBinding binding)
    {
        var count = _service.UpdateDiy.UpdateColumns(g => new
        {
            g.AccountId, g.ProjectId, g.GroupName, g.AiEnabled, g.AiRealtimeEnabled, g.AutoCreateRequirement, g.Status
        }).SetSource(binding).ExecuteAffrows();
        return count > 0 ? Result.Success("更新成功!") : Result.Fail("更新失败!");
    }

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

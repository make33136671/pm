using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Chat;

/// <summary>用户绑定管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ChatUserController : Controller
{
    private readonly ChatUserBindingService _service;
    public ChatUserController(ChatUserBindingService service) { _service = service; }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] string? platformType, [FromQuery] ChatUserRole? role,
        [FromQuery] string? keyword, [FromQuery] string field = "", [FromQuery] string order = "",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, platformType, role, keyword, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    /// <summary>编辑外部联系人信息（姓名/公司/角色）</summary>
    [HttpPost]
    public Result Edit([FromForm] ChatUserBinding binding)
    {
        var count = _service.UpdateDiy.UpdateColumns(b => new
        {
            b.ExternalName, b.ExternalCompany, b.Role
        }).SetSource(binding).ExecuteAffrows();
        return count > 0 ? Result.Success("更新成功!") : Result.Fail("更新失败!");
    }

    /// <summary>为内部用户生成绑定验证码</summary>
    [HttpPost]
    public Result GenerateVerifyCode([FromForm] int adminId, [FromForm] int projectId,
        [FromForm] string platformType = "Telegram")
        => _service.GenerateVerifyCode(adminId, projectId, platformType);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

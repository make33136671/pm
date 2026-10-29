using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Chat;

/// <summary>聊天平台账号管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ChatAccountController : Controller
{
    private readonly ChatAccountService _service;
    private readonly ChatConnectorManager _connectorManager;

    public ChatAccountController(ChatAccountService service, ChatConnectorManager connectorManager)
    {
        _service = service;
        _connectorManager = connectorManager;
    }

    [HttpGet]
    public Result List([FromQuery] string? platformType, [FromQuery] ChatAccountType? accountType,
        [FromQuery] string field = "", [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(platformType, accountType, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] ChatAccount account) => _service.Create(account);

    [HttpPost]
    public Result Edit([FromForm] ChatAccount account) => _service.Edit(account);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    /// <summary>连接/重连账号</summary>
    [HttpPost]
    public async Task<Result> Connect([FromForm] int id)
    {
        var account = _service.Select.Where(a => a.Id == id).First();
        if (account == null) return Result.Fail("账号不存在!");

        try
        {
            await _connectorManager.DisconnectAccount(id);
            await _connectorManager.ConnectAccount(account);
            return Result.Success("连接成功!");
        }
        catch (Exception ex)
        {
            return Result.Fail($"连接失败: {ex.Message}");
        }
    }

    /// <summary>断开账号连接</summary>
    [HttpPost]
    public async Task<Result> Disconnect([FromForm] int id)
    {
        await _connectorManager.DisconnectAccount(id);
        return Result.Success("已断开连接");
    }

    /// <summary>设置 Webhook（Bot 账号）</summary>
    [HttpPost]
    public async Task<Result> SetWebhook([FromForm] int id, [FromForm] string webhookUrl)
    {
        var bot = _connectorManager.GetBot(id);
        if (bot == null) return Result.Fail("Bot 未连接，请先连接账号!");

        try
        {
            await bot.SetWebhookAsync(webhookUrl);
            return Result.Success($"Webhook 已设置: {webhookUrl}");
        }
        catch (Exception ex)
        {
            return Result.Fail($"设置 Webhook 失败: {ex.Message}");
        }
    }

    /// <summary>删除 Webhook（Bot 账号）</summary>
    [HttpPost]
    public async Task<Result> DeleteWebhook([FromForm] int id)
    {
        var bot = _connectorManager.GetBot(id);
        if (bot == null) return Result.Fail("Bot 未连接!");

        await bot.DeleteWebhookAsync();
        return Result.Success("Webhook 已删除");
    }

    /// <summary>获取连接状态</summary>
    [HttpGet]
    public Result ConnectionStatus([FromQuery] int id)
    {
        var bot = _connectorManager.GetBot(id);
        if (bot != null) return Result.Success(new { connected = true, type = "Bot" });

        var client = _connectorManager.GetClient(id);
        if (client != null) return Result.Success(new { connected = true, type = "Personal" });

        return Result.Success(new { connected = false, type = (string?)null });
    }
}

using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Services;

namespace Sb.Apps.Pm.WebApi.Controllers.Chat;

/// <summary>
/// Telegram Webhook 接收端点
/// URL 格式: /api/webhook/telegram/{accountId}
/// </summary>
[ApiController]
[Route("/api/webhook/telegram")]
public class TelegramWebhookController : Controller
{
    private readonly TelegramWebhookHandler _handler;

    public TelegramWebhookController(TelegramWebhookHandler handler)
    {
        _handler = handler;
    }

    /// <summary>接收 Telegram Webhook 推送</summary>
    [HttpPost("{accountId}")]
    public async Task<IActionResult> Receive([FromRoute] int accountId)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        await _handler.HandleWebhook(accountId, payload);

        return Ok();
    }
}

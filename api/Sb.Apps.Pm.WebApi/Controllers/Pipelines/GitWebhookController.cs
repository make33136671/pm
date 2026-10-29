using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>Webhook 接收端点</summary>
[ApiController, Route("/api/webhook")]
public class GitWebhookController : Controller
{
    private readonly GitWebhookService _webhookService;
    public GitWebhookController(GitWebhookService webhookService) { _webhookService = webhookService; }

    [HttpPost("gitlab/{repoId}"), AllowAnonymous]
    public async Task<IActionResult> ReceiveGitLab(int repoId)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        var result = await _webhookService.ProcessGitLabWebhook(repoId, payload);
        return Ok(result);
    }

    [HttpPost("github/{repoId}"), AllowAnonymous]
    public async Task<IActionResult> ReceiveGitHub(int repoId)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        var eventType = Request.Headers["X-GitHub-Event"].FirstOrDefault() ?? "unknown";
        var result = await _webhookService.ProcessGitHubWebhook(repoId, eventType, payload);
        return Ok(result);
    }
}

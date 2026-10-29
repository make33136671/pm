using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Pipelines;
using System.Text.Json;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>
/// AI Code Review 管理端 API — 手动触发 / Webhook (#128 #130 #131)
/// </summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
public class AiCodeReviewController : Controller
{
    private readonly AiCodeReviewService _service;

    public AiCodeReviewController(AiCodeReviewService service)
    {
        _service = service;
    }

    /// <summary>手动触发 AI Review 指定 MR (#128 #130 #131)</summary>
    [HttpPost]
    public async Task<Result> ReviewMr([FromForm] int gitlabProjectId, [FromForm] int mrIid)
    {
        var (success, result) = await _service.ReviewMergeRequest(gitlabProjectId, mrIid);
        return success
            ? Result.Success("Review 完成，已写入 MR 评论", new { result })
            : Result.Fail(result);
    }

    /// <summary>
    /// 接收 GitLab MR Webhook，自动触发 AI Review (#128)
    /// 需在 GitLab Project 的 Webhooks 设置中填写本地址，事件选「Merge request events」
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> GitlabWebhook()
    {
        // 读取原始 body
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        try
        {
            var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            // 只处理 MR 打开/更新事件
            var eventType = root.TryGetProperty("object_kind", out var kind) ? kind.GetString() : null;
            if (eventType != "merge_request")
                return Ok(new { message = "非 MR 事件，已忽略" });

            var attrs = root.GetProperty("object_attributes");
            var action = attrs.TryGetProperty("action", out var act) ? act.GetString() : null;
            // 只在 open / reopen / update 时触发
            if (action != "open" && action != "reopen" && action != "update")
                return Ok(new { message = $"MR action={action}，已忽略" });

            var projectId = root.TryGetProperty("project", out var proj)
                ? proj.TryGetProperty("id", out var pid) ? pid.GetInt32() : 0
                : 0;
            var mrIid = attrs.TryGetProperty("iid", out var iid) ? iid.GetInt32() : 0;

            if (projectId == 0 || mrIid == 0)
                return BadRequest(new { message = "无法解析 projectId 或 mrIid" });

            // 异步触发，不阻塞 Webhook 响应
            _ = Task.Run(() => _service.ReviewMergeRequest(projectId, mrIid));

            return Ok(new { message = $"AI Review 已触发: project={projectId} mr=!{mrIid}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Webhook 解析失败: {ex.Message}" });
        }
    }
}

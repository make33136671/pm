using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using FreeSql;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Ai;
using System.Text;
using System.Text.Json;

namespace Sb.Apps.Pm.WebApi.Controllers.Tasks;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
public class TaskItemController : Controller
{
    private readonly TaskItemService _service;
    private readonly AiProviderFactory _aiFactory;
    private readonly IAiProvider _aiProvider;
    private readonly IFreeSql _fsql;

    public TaskItemController(TaskItemService service, AiProviderFactory aiFactory, IAiProvider aiProvider, IFreeSql fsql)
    {
        _service = service;
        _aiFactory = aiFactory;
        _aiProvider = aiProvider;
        _fsql = fsql;
    }

    [HttpGet]
    public Result List(int? projectId = null, int? requirementId = null, int? featureModuleId = null,
        TaskItemClaimStatus? claimStatus = null, TaskItemExecStatus? execStatus = null,
        TaskItemType? type = null,
        string? assignee = null, string? keyword = null,
        string field = "", string order = "", int page = 1, int pageSize = 20,
        string? source = null)
        => _service.GetList(projectId, requirementId, featureModuleId, claimStatus, execStatus,
            type, assignee, keyword, field, order, page, pageSize, source);

    [HttpGet]
    public Result Detail(int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] TaskItem task) => _service.Create(task);

    [HttpPost]
    public Result Edit([FromForm] TaskItem task) => _service.Edit(task);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    [HttpGet]
    public Result Kanban(int projectId) => _service.Kanban(projectId);

    [HttpPost]
    public Result Resume([FromForm] int id) => _service.Resume(id);

    [HttpPost]
    public Result Cancel([FromForm] int id) => _service.Cancel(id);

    [HttpGet]
    public Result Logs(int taskItemId, int page = 1, int pageSize = 20)
        => _service.GetLogs(taskItemId, page, pageSize);

    /// <summary>写入审计日志(#99):供 pm.sh / auto-loop 等直接记录操作轨迹。</summary>
    [HttpPost]
    public Result WriteLog([FromForm] int taskItemId, [FromForm] string action,
        [FromForm] string? message = null, [FromForm] string? @operator = null)
        => _service.WriteLogExternal(taskItemId, action, message, @operator);

    /// <summary>接单：获取并锁定一个待处理任务</summary>
    [HttpPost]
    public Result Claim([FromForm] string agentName = "AI-MCP", [FromForm] int? projectId = null)
        => _service.ClaimNext(agentName, projectId);

    /// <summary>上报开始执行</summary>
    [HttpPost]
    public Result ReportStart([FromForm] int taskItemId, [FromForm] string agentName = "AI-MCP")
        => _service.ReportStart(taskItemId, agentName);

    /// <summary>上报执行成功</summary>
    [HttpPost]
    public Result ReportSuccess([FromForm] int taskItemId, [FromForm] string? message = null, [FromForm] string agentName = "AI-MCP")
        => _service.ReportSuccess(taskItemId, agentName, message);

    /// <summary>上报执行失败</summary>
    [HttpPost]
    public Result ReportFail([FromForm] int taskItemId, [FromForm] string reason, [FromForm] string agentName = "AI-MCP")
        => _service.ReportFail(taskItemId, agentName, reason);

    /// <summary>获取可接单任务列表</summary>
    [HttpGet]
    public Result Available(int? projectId = null)
        => _service.GetAvailable(projectId);

    /// <summary>提交验收 — Running → PendingReview，可附带 commit（#107）</summary>
    [HttpPost]
    public async Task<Result> SubmitForReview([FromForm] int taskItemId, [FromForm] string fixedDescription,
        [FromForm] string agentName = "AI-MCP", [FromForm] string? commitSha = null, [FromForm] string? commitBranch = null)
        => await _service.SubmitForReview(taskItemId, agentName, fixedDescription, commitSha, commitBranch);

    /// <summary>验收通过 — PendingReview → Success（#102）</summary>
    [HttpPost]
    public Result ReviewApprove([FromForm] int taskItemId, [FromForm] string? comment = null,
        [FromForm] string operatorName = "admin")
        => _service.ReviewApprove(taskItemId, operatorName, comment);

    /// <summary>退回重修 — PendingReview → Pending/Idle（#107）</summary>
    [HttpPost]
    public Result ReviewReject([FromForm] int taskItemId, [FromForm] string reason,
        [FromForm] string operatorName = "admin")
        => _service.ReviewReject(taskItemId, operatorName, reason);

    /// <summary>AI 自动验证 — 调 AI 判断修复说明是否满足要求（#108）</summary>
    [HttpPost]
    public async Task<Result> AutoVerify([FromForm] int taskItemId)
        => await _service.AutoVerify(taskItemId, _aiProvider);

    /// <summary>上传附件（验收/提交/退回时）(#103)</summary>
    [HttpPost]
    public async Task<Result> UploadAttachment([FromForm] int taskItemId, [FromForm] IFormFile file,
        [FromForm] string stage = "submit_review")
    {
        var username = User?.Identity?.Name;
        return await _service.UploadAttachment(taskItemId, file, stage, username);
    }

    /// <summary>获取任务附件列表 (#103)</summary>
    [HttpGet]
    public Result GetAttachments(int taskItemId, string? stage = null)
        => _service.GetAttachments(taskItemId, stage);

    /// <summary>删除附件 (#103)</summary>
    [HttpPost]
    public Result DeleteAttachment([FromForm] int attachmentId)
        => _service.DeleteAttachment(attachmentId, User?.Identity?.Name);

    /// <summary>快速补录：一键创建已完成的需求+任务记录（用于 AI 直接修复后补录）</summary>
    [HttpPost]
    public Result LogQuickFix([FromForm] int projectId, [FromForm] string title,
        [FromForm] string? content, [FromForm] string type = "Bugfix",
        [FromForm] decimal? actualHours = null)
        => _service.LogQuickFix(projectId, title, content, type, actualHours);

    /// <summary>从需求快速生成任务（返回预览，不写入DB）</summary>
    [HttpPost]
    public Result GenerateFromRequirements([FromBody] GenerateTaskRequest req)
        => _service.QuickGenerateFromRequirements(req.RequirementIds, req.ProjectId);

    /// <summary>确认创建任务（批量写入DB）</summary>
    [HttpPost]
    public Result ConfirmCreate([FromBody] List<TaskItem> tasks)
        => _service.ConfirmCreate(tasks);

    /// <summary>AI 执行任务（非流式）</summary>
    [HttpPost]
    public async Task<Result> ExecByAi([FromForm] int taskItemId, [FromForm] int aiProviderId)
        => await _service.ExecByAi(taskItemId, aiProviderId);

    /// <summary>AI 执行任务（SSE 流式）</summary>
    [HttpGet]
    public async Task StreamExec(int taskItemId, int aiProviderId, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        var (adapter, config, task, error) = _service.PrepareAiExec(taskItemId, aiProviderId);
        if (error != null)
        {
            await WriteSseEvent("error", new { message = error });
            return;
        }

        await WriteSseEvent("start", new { taskItemId, provider = config!.Name });

        var fullContent = new StringBuilder();
        try
        {
            var prompt = _service.BuildTaskPrompt(task!);
            await foreach (var chunk in adapter!.StreamGenerateAsync(config, prompt, ct))
            {
                fullContent.Append(chunk);
                await WriteSseEvent("chunk", new { text = chunk });
            }

            _service.FinishAiExec(taskItemId, true, fullContent.ToString(), $"AI:{config.Name}");
            var duration = task!.ExecStartTime.HasValue ? (int)(AppTime.Now - task.ExecStartTime.Value).TotalSeconds : 0;
            await WriteSseEvent("done", new { success = true, duration, message = "执行完成" });
        }
        catch (OperationCanceledException)
        {
            _service.FinishAiExec(taskItemId, false, "用户取消", $"AI:{config!.Name}");
            await WriteSseEvent("error", new { message = "已取消" });
        }
        catch (Exception ex)
        {
            _service.FinishAiExec(taskItemId, false, ex.Message, $"AI:{config!.Name}");
            var t = _service.Select.Where(c => c.Id == taskItemId).First();
            await WriteSseEvent("error", new { message = ex.Message, failCount = t?.FailCount, maxRetry = t?.MaxRetryCount });
        }
    }

    private async Task WriteSseEvent(string eventName, object data)
    {
        var json = JsonSerializer.Serialize(data);
        await Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n");
        await Response.Body.FlushAsync();
    }
}

public class GenerateTaskRequest
{
    public List<int> RequirementIds { get; set; } = new();
    public int ProjectId { get; set; }
}

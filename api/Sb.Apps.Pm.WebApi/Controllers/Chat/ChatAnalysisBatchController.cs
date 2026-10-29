using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Chat;

/// <summary>分析批次管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ChatAnalysisBatchController : Controller
{
    private readonly ChatAnalysisBatchService _service;
    private readonly ChatMessageService _messageService;

    public ChatAnalysisBatchController(ChatAnalysisBatchService service, ChatMessageService messageService)
    {
        _service = service;
        _messageService = messageService;
    }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] ChatBatchStatus? status,
        [FromQuery] string field = "", [FromQuery] string order = "",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, status, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.BatchDetail(id);

    /// <summary>创建批次</summary>
    [HttpPost]
    public Result Create([FromForm] int projectId, [FromForm] int? groupBindingId, [FromForm] string title)
        => _service.CreateBatch(projectId, groupBindingId, title);

    /// <summary>导入消息到批次（自动去重）</summary>
    [HttpPost]
    public Result ImportMessages([FromForm] int id, [FromForm] string messageIds)
    {
        var ids = messageIds.Split(',').Select(int.Parse).ToList();
        return _service.ImportMessages(id, ids);
    }

    /// <summary>AI 接单分析</summary>
    [HttpPost]
    public async Task<Result> Analyze([FromForm] int id) => await _service.Analyze(id);

    /// <summary>关联任务到批次</summary>
    [HttpPost]
    public Result LinkTasks([FromForm] int id, [FromForm] string taskIds)
    {
        var ids = taskIds.Split(',').Select(int.Parse).ToList();
        return _service.LinkTasks(id, ids);
    }

    /// <summary>关联正式需求到批次</summary>
    [HttpPost]
    public Result LinkRequirements([FromForm] int id, [FromForm] string requirementIds)
    {
        var ids = requirementIds.Split(',').Select(int.Parse).ToList();
        return _service.LinkRequirements(id, ids);
    }

    /// <summary>从 Markdown 导入需求到批次</summary>
    [HttpPost]
    public async Task<Result> ImportMd([FromForm] int id, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) return Result.Fail("请上传 MD 文件");
        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        return _service.ImportRequirementsFromMd(id, content);
    }

    /// <summary>导出批次消息为 Markdown</summary>
    [HttpPost]
    public IActionResult ExportMd([FromForm] int id)
    {
        var fsql = HttpContext.RequestServices.GetRequiredService<IFreeSql>();
        var messageIds = fsql.Select<ChatBatchMessage>()
            .Where(m => m.BatchId == id)
            .ToList(m => m.MessageId);
        if (messageIds.Count == 0) return BadRequest("批次没有消息");

        var baseUrl = HttpContext.RequestHostWithScheme();
        var md = _messageService.GenerateExportMd(messageIds, baseUrl);
        var bytes = System.Text.Encoding.UTF8.GetBytes(md);
        return File(bytes, "text/markdown", $"batch-{id}-export-{AppTime.Now:yyyyMMdd-HHmmss}.md");
    }

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

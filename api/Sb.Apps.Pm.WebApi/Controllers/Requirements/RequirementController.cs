using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Ai;
using Sb.Apps.Pm.Services.Tasks;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Requirements;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
public class RequirementController : Controller
{
    private readonly RequirementService _service;
    private readonly IAiProvider? _aiProvider;
    public RequirementController(RequirementService service, IAiProvider? aiProvider = null)
    {
        _service = service;
        _aiProvider = aiProvider;
    }

    [HttpGet]
    public Result List(int? projectId = null, RequirementStatus? status = null, RequirementType? type = null,
        string? keyword = null, string? batchId = null, int? parentId = null, bool? onlyRoot = null,
        int? customerId = null, string field = "", string order = "", int page = 1, int pageSize = 20,
        RequirementSource? source = null)
        => _service.GetList(projectId, status, type, keyword, batchId, parentId, onlyRoot, customerId, field, order, page, pageSize, source);

    [HttpGet]
    public Result Detail(int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] Requirement req) => _service.Create(req);

    [HttpPost]
    public Result Edit([FromForm] Requirement req) => _service.Edit(req);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    [HttpPost]
    public Result BatchUpdateStatus([FromForm] List<int> ids, [FromForm] RequirementStatus status)
        => _service.BatchUpdateStatus(ids, status);

    [HttpGet]
    public Result Kanban(int projectId) => _service.Kanban(projectId);

    /// <summary>AI 分析拆分需求</summary>
    [HttpPost]
    public async Task<Result> AnalyzeAndSplit([FromForm] int projectId, [FromForm] string content)
        => await _service.AnalyzeAndSplit(projectId, content, _aiProvider);

    /// <summary>获取暂存需求（含已标记删除的）</summary>
    [HttpGet]
    public Result StagingList(string batchId) => _service.GetPendingConfirmAll(batchId);

    /// <summary>编辑暂存需求</summary>
    [HttpPost]
    public Result EditStaging([FromForm] int id, [FromForm] string title, [FromForm] string? content, [FromForm] RequirementType type)
        => _service.EditStaging(id, title, content, type);

    /// <summary>单条确认需求</summary>
    [HttpPost]
    public Result ConfirmSingle([FromForm] int id) => _service.ConfirmSingle(id);

    /// <summary>全部确认（跳过已删除的）</summary>
    [HttpPost]
    public Result ConfirmAll([FromForm] string batchId) => _service.ConfirmAll(batchId);

    /// <summary>标记删除暂存需求</summary>
    [HttpPost]
    public Result RejectSingle([FromForm] int id) => _service.RejectSingle(id);

    /// <summary>撤销标记删除</summary>
    [HttpPost]
    public Result RestoreSingle([FromForm] int id) => _service.RestoreSingle(id);

    /// <summary>丢弃整批</summary>
    [HttpPost]
    public Result DiscardBatch([FromForm] string batchId) => _service.DiscardBatch(batchId);

    /// <summary>MCP 提交 AI 分析结果（批量暂存）</summary>
    [HttpPost]
    public async Task<Result> SubmitAnalysis([FromBody] SubmitAnalysisRequest req)
        => await _service.SubmitAnalysis(req.ProjectId, req.Requirements, req.BatchLabel, req.BatchDescription);

    /// <summary>获取分析批次列表</summary>
    [HttpGet]
    public Result AnalysisBatchList(int? projectId = null, int page = 1, int pageSize = 20)
        => _service.GetAnalysisBatchList(projectId, page, pageSize);

    /// <summary>向已有批次添加单条需求</summary>
    [HttpPost]
    public Result AddToBatch([FromForm] string batchId, [FromForm] string title, [FromForm] string? content, [FromForm] string? type)
        => _service.AddToExistingBatch(batchId, title, content, type);

    /// <summary>按任务完成度自动派生需求状态/工时汇总(#95)。</summary>
    [HttpPost]
    public Result SyncStatusFromTasks([FromForm] int? projectId = null)
        => _service.SyncStatusFromTasks(projectId);

    /// <summary>AI 将需求拆解为任务暂存（需 AI Provider），返回 batchId 供前端预览确认。</summary>
    [HttpPost]
    public Task<Result> AiSplitTasks([FromForm] int requirementId)
        => _service.SplitTasks(requirementId, _aiProvider);

    /// <summary>确认 AI 拆解的任务批次，写入正式 TaskItem 并删除暂存。</summary>
    [HttpPost]
    public Result AiConfirmTasks([FromForm] string batchId, [FromServices] TaskItemService taskService)
        => taskService.ConfirmCreate(
            taskService.GetBatchedTasks(batchId));
}

public class SubmitAnalysisRequest
{
    public int ProjectId { get; set; }
    public string? BatchLabel { get; set; }
    public string? BatchDescription { get; set; }
    public List<SubmitAnalysisItem> Requirements { get; set; } = new();
}

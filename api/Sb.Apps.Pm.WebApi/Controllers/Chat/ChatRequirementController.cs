using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Chat;

/// <summary>AI 提炼需求管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ChatRequirementController : Controller
{
    private readonly ChatRequirementService _service;
    private readonly AiPipelineExecutor _pipelineExecutor;
    private readonly ChatConnectorManager _connectorManager;
    private readonly ChatGroupBindingService _groupService;

    public ChatRequirementController(
        ChatRequirementService service,
        AiPipelineExecutor pipelineExecutor,
        ChatConnectorManager connectorManager,
        ChatGroupBindingService groupService)
    {
        _service = service;
        _pipelineExecutor = pipelineExecutor;
        _connectorManager = connectorManager;
        _groupService = groupService;
    }

    [HttpGet]
    public Result List([FromQuery] int? groupBindingId, [FromQuery] int? projectId,
        [FromQuery] ChatRequirementStatus? status, [FromQuery] string? keyword,
        [FromQuery] string field = "", [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(groupBindingId, projectId, status, keyword, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    /// <summary>编辑提炼需求的标题/描述</summary>
    [HttpPost]
    public Result Edit([FromForm] ChatRequirement req)
    {
        var count = _service.UpdateDiy.UpdateColumns(r => new
        {
            r.Title, r.Description
        }).SetSource(req).ExecuteAffrows();
        return count > 0 ? Result.Success("更新成功!") : Result.Fail("更新失败!");
    }

    /// <summary>确认需求</summary>
    [HttpPost]
    public Result Confirm([FromForm] int id, [FromForm] int confirmedBy)
        => _service.Confirm(id, confirmedBy);

    /// <summary>拒绝需求</summary>
    [HttpPost]
    public Result Reject([FromForm] int id)
        => _service.Reject(id);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    /// <summary>整理需求 — AI 批量分析群组未处理消息，提取需求</summary>
    [HttpPost]
    public async Task<Result> Analyze([FromForm] int groupBindingId)
    {
        try
        {
            var requirements = await _pipelineExecutor.ProcessBatch(groupBindingId);
            return Result.Success($"分析完成，提取到 {requirements.Count} 条需求", requirements);
        }
        catch (Exception ex)
        {
            return Result.Fail($"分析失败: {ex.Message}");
        }
    }

    /// <summary>一键发到群里让客户确认</summary>
    [HttpPost]
    public async Task<Result> SendToGroup([FromForm] int groupBindingId, [FromForm] string? requirementIds)
    {
        var groupBinding = _groupService.Select
            .Where(g => g.Id == groupBindingId)
            .Include(g => g.Account)
            .First();

        if (groupBinding?.PlatformGroupId == null)
            return Result.Fail("群组绑定不存在或未完成绑定!");

        var bot = _connectorManager.GetBot(groupBinding.AccountId);
        if (bot == null)
            return Result.Fail("Bot 未连接!");

        // 查询要发送的需求
        var query = _service.Select
            .Where(r => r.GroupBindingId == groupBindingId);

        if (!string.IsNullOrEmpty(requirementIds))
        {
            var ids = requirementIds.Split(',').Select(int.Parse).ToList();
            query = query.Where(r => ids.Contains(r.Id));
        }
        else
        {
            // 默认发送所有 Draft 状态的需求
            query = query.Where(r => r.Status == ChatRequirementStatus.Draft);
        }

        var requirements = query.OrderBy(r => r.Id).ToList();
        if (requirements.Count == 0)
            return Result.Fail("没有待确认的需求!");

        // 构建消息
        var lines = requirements.Select((r, i) =>
            $"{i + 1}. <b>{r.Title}</b>\n   {r.Description ?? ""}");

        var text = "📋 <b>需求确认</b>\n\n" +
                   "以下需求由 AI 从群聊中提取，请确认是否准确：\n\n" +
                   string.Join("\n\n", lines) +
                   "\n\n请回复确认或提出修改意见。";

        await bot.SendTextAsync(groupBinding.PlatformGroupId, text);

        // 更新状态为待确认
        foreach (var req in requirements)
        {
            req.Status = ChatRequirementStatus.PendingConfirm;
            _service.UpdateDiy.UpdateColumns(r => new { r.Status })
                .SetSource(req).ExecuteAffrows();
        }

        return Result.Success($"已发送 {requirements.Count} 条需求到群组确认");
    }
}

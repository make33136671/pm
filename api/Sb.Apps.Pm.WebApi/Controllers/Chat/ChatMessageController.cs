using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Chat;

/// <summary>聊天消息记录</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ChatMessageController : Controller
{
    private readonly ChatMessageService _service;
    private readonly ChatMediaService _mediaService;
    private readonly IConfiguration _configuration;

    public ChatMessageController(ChatMessageService service, ChatMediaService mediaService, IConfiguration configuration)
    {
        _service = service;
        _mediaService = mediaService;
        _configuration = configuration;
    }

    [HttpGet]
    public Result List([FromQuery] int? groupBindingId, [FromQuery] string? keyword,
        [FromQuery] ChatMessageType? messageType, [FromQuery] bool? aiProcessed,
        [FromQuery] string field = "", [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(groupBindingId, keyword, messageType, aiProcessed, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    /// <summary>获取媒体文件</summary>
    [HttpGet]
    [Route("/api/chat/media/{id}")]
    [AllowAnonymous]
    public IActionResult Media([FromRoute] int id)
    {
        var media = _mediaService.Select.Where(m => m.Id == id).First();
        if (media?.LocalPath == null) return NotFound();

        var uploadPath = _configuration.GetValue<string>("UploadPath") ?? "storage/uploads/";
        var fullPath = Path.Combine(uploadPath, "chat-media", media.LocalPath);

        if (!System.IO.File.Exists(fullPath)) return NotFound();

        var contentType = media.MimeType ?? "application/octet-stream";
        return PhysicalFile(Path.GetFullPath(fullPath), contentType);
    }

    /// <summary>通过 Bot 发送消息到群组</summary>
    [HttpPost]
    public async Task<Result> Send([FromForm] int groupBindingId, [FromForm] string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return Result.Fail("内容不能为空");

        var fsql = HttpContext.RequestServices.GetRequiredService<IFreeSql>();
        var group = fsql.Select<ChatGroupBinding>()
            .Where(g => g.Id == groupBindingId && g.Status == ChatGroupBindingStatus.Active)
            .First();
        if (group?.PlatformGroupId == null) return Result.Fail("群组未绑定或未激活");

        var connMgr = HttpContext.RequestServices.GetRequiredService<ChatConnectorManager>();
        var bot = connMgr.GetBot(group.AccountId);
        if (bot == null) return Result.Fail("Bot 未连接");

        await bot.SendTextAsync(group.PlatformGroupId, content);

        // 存库（用 Bot 名称 + 管理员信息）
        var adminId = User.Id();
        var account = fsql.Select<ChatAccount>().Where(a => a.Id == group.AccountId).First();
        var msgService = HttpContext.RequestServices.GetRequiredService<ChatMessageService>();
        msgService.SaveMessage(new ChatMessage
        {
            GroupBindingId = groupBindingId,
            AccountId = group.AccountId,
            SenderId = $"admin:{adminId}",
            SenderName = account?.AccountName ?? "Bot",
            MessageType = ChatMessageType.Text,
            Content = content,
        });

        return Result.Success("发送成功");
    }

    /// <summary>获取用户头像（自动缓存）</summary>
    [HttpGet]
    [Route("/api/chat/avatar/{senderId}")]
    [AllowAnonymous]
    public async Task<IActionResult> Avatar([FromRoute] string senderId)
    {
        // 系统管理员头像：admin:123 格式
        if (senderId.StartsWith("admin:"))
        {
            var adminIdStr = senderId[6..];
            if (int.TryParse(adminIdStr, out var adminId))
            {
                var fsql = HttpContext.RequestServices.GetRequiredService<IFreeSql>();
                var admin = fsql.Select<SB.Framework.Models.Admin>().Where(a => a.Id == adminId).First();
                if (admin?.Avatar != null)
                {
                    // 头像可能是相对路径或URL
                    var uploadPath = _configuration.GetValue<string>("UploadPath") ?? "storage/uploads/";
                    var avatarPath = Path.Combine(uploadPath, admin.Avatar.TrimStart('/'));
                    if (System.IO.File.Exists(avatarPath))
                        return PhysicalFile(Path.GetFullPath(avatarPath), "image/jpeg");
                }
            }
            return NotFound();
        }

        // Telegram 用户头像：先检查缓存
        var basePath = _configuration.GetValue<string>("UploadPath") ?? "storage/uploads/";
        var avatarDir = Path.Combine(basePath, "chat-avatars");
        var cachedPath = Path.Combine(avatarDir, $"{senderId}.jpg");

        if (System.IO.File.Exists(cachedPath))
        {
            return PhysicalFile(Path.GetFullPath(cachedPath), "image/jpeg");
        }

        // 从 Bot 下载
        var connMgr = HttpContext.RequestServices.GetRequiredService<ChatConnectorManager>();
        foreach (var bot in connMgr.GetAllBots().Values)
        {
            try
            {
                var stream = await bot.GetUserAvatarAsync(senderId);
                if (stream == null) continue;

                if (!Directory.Exists(avatarDir)) Directory.CreateDirectory(avatarDir);
                await using var fs = System.IO.File.Create(cachedPath);
                await stream.CopyToAsync(fs);

                stream.Position = 0;
                return File(stream, "image/jpeg");
            }
            catch { /* 下一个 bot 重试 */ }
        }

        return NotFound();
    }

    /// <summary>导出选中消息为 Markdown</summary>
    [HttpPost]
    public IActionResult ExportMd([FromForm] string messageIds)
    {
        if (string.IsNullOrEmpty(messageIds)) return BadRequest("请选择消息");

        var ids = messageIds.Split(',').Select(int.Parse).ToList();
        var baseUrl = HttpContext.RequestHostWithScheme();
        var md = _service.GenerateExportMd(ids, baseUrl);
        var bytes = System.Text.Encoding.UTF8.GetBytes(md);
        return File(bytes, "text/markdown", $"chat-export-{AppTime.Now:yyyyMMdd-HHmmss}.md");
    }

    /// <summary>AI 分析选中的消息</summary>
    [HttpPost]
    public async Task<Result> AnalyzeSelected([FromForm] string messageIds)
    {
        if (string.IsNullOrEmpty(messageIds)) return Result.Fail("请选择消息");

        var ids = messageIds.Split(',').Select(int.Parse).ToList();
        var fsql = HttpContext.RequestServices.GetRequiredService<IFreeSql>();

        var messages = fsql.Select<ChatMessage>()
            .Where(m => ids.Contains(m.Id))
            .OrderBy(m => m.Id)
            .ToList();

        if (messages.Count == 0) return Result.Fail("未找到消息");

        var groupBindingId = messages.First().GroupBindingId;
        if (groupBindingId == null) return Result.Fail("消息未关联群组");

        var groupBinding = fsql.Select<ChatGroupBinding>()
            .Where(g => g.Id == groupBindingId.Value)
            .First();
        if (groupBinding == null) return Result.Fail("群组绑定不存在");

        var executor = HttpContext.RequestServices.GetRequiredService<AiPipelineExecutor>();
        var requirements = await executor.ProcessSelectedMessages(messages, groupBinding);
        return Result.Success($"分析完成，提取到 {requirements.Count} 条需求（消息ID: {messageIds}）",
            new { requirements, analyzedMessageIds = ids });
    }
}

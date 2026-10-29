using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;
using System.Text;

namespace Sb.Apps.Pm.WebApi.Controllers.Documents;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class DocVersionController : Controller
{
    private readonly DocVersionService _service;

    public DocVersionController(DocVersionService service) { _service = service; }

    /// <summary>获取版本列表</summary>
    [HttpGet]
    public Result Versions(string sourceType, int sourceId) => _service.GetVersions(sourceType, sourceId);

    /// <summary>获取指定版本详情</summary>
    [HttpGet]
    public Result Detail(int id) => _service.GetVersionDetail(id);

    /// <summary>获取最新版本（预览用）</summary>
    [HttpGet]
    public Result Latest(string sourceType, int sourceId) => _service.GetLatest(sourceType, sourceId);

    /// <summary>回档到指定版本</summary>
    [HttpPost]
    public Result Rollback([FromForm] string sourceType, [FromForm] int sourceId, [FromForm] int versionId)
    {
        var username = HttpContext.User?.Username();
        return _service.Rollback(sourceType, sourceId, versionId, username);
    }

    /// <summary>下载 MD 文档</summary>
    [HttpGet]
    public IActionResult Download(string sourceType, int sourceId)
    {
        var result = _service.GetLatest(sourceType, sourceId);
        if (result.Code != 0) return NotFound("文档不存在");

        var doc = result.Data as DocVersion;
        var content = doc?.Content ?? "";
        var fileName = $"{sourceType}_{sourceId}_v{doc?.Version}.md";

        return File(Encoding.UTF8.GetBytes(content), "text/markdown", fileName);
    }
}

/// <summary>公开文档访问（无需认证）</summary>
[ApiController, Route("/api/doc")]
public class PublicDocController : Controller
{
    private readonly DocVersionService _service;

    public PublicDocController(DocVersionService service) { _service = service; }

    /// <summary>公开查看最新文档（直接返回 MD 文本）</summary>
    [HttpGet("{sourceType}/{sourceId}")]
    public IActionResult View(string sourceType, int sourceId)
    {
        var result = _service.GetLatest(sourceType, sourceId);
        if (result.Code != 0) return NotFound("文档不存在");

        var doc = result.Data as DocVersion;
        return Content(doc?.Content ?? "", "text/markdown; charset=utf-8");
    }

    /// <summary>公开下载 MD 文档</summary>
    [HttpGet("{sourceType}/{sourceId}/download")]
    public IActionResult Download(string sourceType, int sourceId)
    {
        var result = _service.GetLatest(sourceType, sourceId);
        if (result.Code != 0) return NotFound("文档不存在");

        var doc = result.Data as DocVersion;
        var content = doc?.Content ?? "";
        var fileName = $"{sourceType}_{sourceId}_v{doc?.Version}.md";

        return File(Encoding.UTF8.GetBytes(content), "text/markdown", fileName);
    }
}

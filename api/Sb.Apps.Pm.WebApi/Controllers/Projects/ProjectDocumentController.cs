using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Projects;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

/// <summary>项目文档管理</summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProjectDocumentController : Controller
{
    private readonly ProjectDocumentService _service;
    private readonly IConfiguration _configuration;
    public ProjectDocumentController(ProjectDocumentService service, IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    [HttpGet]
    public Result List([FromQuery] int? projectId, [FromQuery] string? category, [FromQuery] string? keyword,
        [FromQuery] string? metadataStatus = null,
        [FromQuery] string field = "", [FromQuery] string order = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _service.GetList(projectId, category, keyword, metadataStatus, field, order, page, pageSize);

    [HttpGet]
    public Result Detail([FromQuery] int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] ProjectDocument doc) => _service.Create(doc);

    [HttpPost]
    public Result Edit([FromForm] ProjectDocument doc) => _service.Edit(doc);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    /// <summary>获取文档文件（预览/下载）</summary>
    [HttpGet]
    public IActionResult File([FromQuery] int id)
    {
        var doc = _service.Select.Where(d => d.Id == id).First();
        if (string.IsNullOrEmpty(doc?.FilePath)) return NotFound();

        var uploadPath = _configuration.GetValue<string>("UploadPath") ?? "storage/uploads/";
        var fullPath = Path.Combine(uploadPath, doc.FilePath);

        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var contentType = (doc.FileType?.ToLower()) switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "svg" => "image/svg+xml",
            "webp" => "image/webp",
            "pdf" => "application/pdf",
            "doc" or "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls" or "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "zip" => "application/zip",
            "mp4" => "video/mp4",
            "mp3" => "audio/mpeg",
            _ => "application/octet-stream"
        };

        return PhysicalFile(Path.GetFullPath(fullPath), contentType);
    }

    /// <summary>生成项目状态快照文档(#84)。</summary>
    [HttpPost]
    public Result SaveSnapshot([FromForm] int projectId, [FromForm] string? title = null)
        => _service.SaveProjectSnapshot(projectId, title ?? "");

    /// <summary>生成项目审计报告文档(#85)。</summary>
    [HttpPost]
    public Result GenerateAuditReport([FromForm] int projectId)
        => _service.GenerateAuditReport(projectId);
}

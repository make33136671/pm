using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Services.ImageProcessing;

namespace Sb.Apps.Pm.WebApi.Controllers.ImageProcessing
{
    /// <summary>
    /// 图片处理：多分辨率生成 / 格式转换 / 生成 favicon / 缩放裁剪。
    /// 处理类端点直接返回文件流（下载）；项目 Logo 上传则落盘 storage 并返回 URL。
    /// </summary>
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class ImageProcessingController : Controller
    {
        private readonly ImageProcessingService _service;
        private readonly IConfiguration _configuration;

        public ImageProcessingController(ImageProcessingService service, IConfiguration configuration)
        {
            _service = service;
            _configuration = configuration;
        }

        private const long MaxUploadBytes = 20 * 1024 * 1024; // 20MB

        // ── 1. 多分辨率生成（zip 下载）────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> MultiResolution(
            [FromForm] IFormFile file, [FromForm] string? sizes = null,
            [FromForm] string format = "png", [FromForm] int quality = 90,
            [FromForm] string fit = "contain", [FromForm] string? background = null)
        {
            var err = Validate(file);
            if (err != null) return BadRequest(err);

            var sizeArr = ParseSizes(sizes);
            await using var stream = file.OpenReadStream();
            var zip = await _service.GenerateMultiResolutionZipAsync(stream, sizeArr, format, quality, fit, background);
            return File(zip, "application/zip", $"{SafeName(file.FileName)}-resolutions.zip");
        }

        // ── 2. 格式转换（文件下载）────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Convert(
            [FromForm] IFormFile file, [FromForm] string format = "png",
            [FromForm] int quality = 90, [FromForm] int? width = null, [FromForm] int? height = null,
            [FromForm] string fit = "contain", [FromForm] string? background = null)
        {
            var err = Validate(file);
            if (err != null) return BadRequest(err);

            await using var stream = file.OpenReadStream();
            var (bytes, contentType, ext) = await _service.ConvertFormatAsync(stream, format, quality, width, height, fit, background);
            return File(bytes, contentType, $"{SafeName(file.FileName)}.{ext}");
        }

        // ── 3. 生成 favicon.ico（文件下载）────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Favicon([FromForm] IFormFile file, [FromForm] string? sizes = null)
        {
            var err = Validate(file);
            if (err != null) return BadRequest(err);

            var sizeArr = ParseSizes(sizes);
            await using var stream = file.OpenReadStream();
            var ico = await _service.GenerateFaviconAsync(stream, sizeArr.Length > 0 ? sizeArr : null);
            return File(ico, "image/x-icon", "favicon.ico");
        }

        // ── 4. 缩放/裁剪（文件下载）───────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Resize(
            [FromForm] IFormFile file, [FromForm] int width, [FromForm] int height,
            [FromForm] string fit = "contain", [FromForm] string? background = null,
            [FromForm] string format = "png", [FromForm] int quality = 90)
        {
            var err = Validate(file);
            if (err != null) return BadRequest(err);
            if (width <= 0 && height <= 0) return BadRequest("宽高至少指定一个");

            await using var stream = file.OpenReadStream();
            var (bytes, contentType, ext) = await _service.ResizeAsync(stream, width, height, fit, background, format, quality);
            return File(bytes, contentType, $"{SafeName(file.FileName)}-{width}x{height}.{ext}");
        }

        // ── 5. 项目 Logo 上传（落盘 storage，返回 URL）─────────────────
        /// <summary>
        /// 上传图片作为项目 Logo：自动处理成 512x512 PNG，存到 storage 并返回可访问 URL。
        /// 供「新建项目」第一步调用。
        /// </summary>
        [HttpPost]
        public async Task<Result> UploadProjectLogo([FromForm] IFormFile file)
        {
            var err = Validate(file);
            if (err != null) return Result.Fail(err);

            try
            {
                await using var stream = file.OpenReadStream();
                // 统一规格化成 512x512 透明 PNG
                var (bytes, _, _) = await _service.ResizeAsync(stream, 512, 512, "contain", null, "png", 100);

                var relDir = Path.Combine("storage/uploads", "project-logos");
                var absDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relDir);
                if (!Directory.Exists(absDir)) Directory.CreateDirectory(absDir);

                var fileName = $"{Guid.NewGuid():N}.png";
                var absPath = Path.Combine(absDir, fileName);
                await System.IO.File.WriteAllBytesAsync(absPath, bytes);

                // 存相对 URL（前端通过 /storage/... 访问）
                var url = $"/{relDir.Replace('\\', '/')}/{fileName}";
                return Result.Success("Logo 上传成功", new { url });
            }
            catch (Exception ex)
            {
                return Result.Fail($"Logo 处理失败: {ex.Message}");
            }
        }

        // ── 私有工具 ─────────────────────────────────────────────────
        private static string? Validate(IFormFile? file)
        {
            if (file == null || file.Length == 0) return "请上传图片文件";
            if (file.Length > MaxUploadBytes) return "图片超过 20MB 限制";
            var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
            var allowed = new[] { "png", "jpg", "jpeg", "webp", "bmp", "gif", "svg", "ico", "tiff", "tif" };
            if (!allowed.Contains(ext)) return $"不支持的图片格式: {ext}";
            return null;
        }

        private static int[] ParseSizes(string? sizes)
        {
            if (string.IsNullOrWhiteSpace(sizes)) return Array.Empty<int>();
            return sizes.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                .Where(n => n > 0)
                .ToArray();
        }

        private static string SafeName(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(name)) name = "image";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 50 ? name[..50] : name;
        }
    }
}

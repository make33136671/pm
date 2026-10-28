using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SB.Feature.FileManager.Models;
using SB.Feature.FileManager.Services;
using SB.Framework.Core.Expand;
using SB.Framework.Models;

namespace SB.Feature.FileManager.Controllers
{
    /// <summary>
    /// 服务器文件管理器（admin 后台，JwtAdmin 鉴权）。
    ///
    /// 路由：`[controller]/[action]` → AdminRoutePrefixConvention 自动加 `admin/` 前缀，
    /// 最终为 `/admin/FileManager/*`（前端 baseURL 已含 `/admin`，故前端写 `/FileManager/*`）。
    ///
    /// 权限模型：与已有 web shell 对等（管理员即运维）。路径边界由 appsettings
    /// `Features:FileManager` 的 AllowedRoots / DeniedPaths 控制，默认全盘开放。
    /// </summary>
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class FileManagerController : ControllerBase
    {
        private readonly FileManagerService _service;

        public FileManagerController(FileManagerService service) => _service = service;

        private string? Actor => User.Username() is { Length: > 0 } u ? u : null;

        #region 浏览

        /// <summary>
        /// 列举目录内容。GET /admin/FileManager/List?path=/opt/app
        /// </summary>
        [HttpGet]
        public Result List(string? path, bool showHidden = false)
        {
            try { return Result.Success(_service.List(path, showHidden)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message, 404); }
        }

        /// <summary>
        /// 只列子目录（左侧目录树懒加载）。GET /admin/FileManager/Directories?path=/
        /// </summary>
        [HttpGet]
        public Result Directories(string? path)
        {
            try { return Result.Success(_service.ListDirectories(path)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message, 404); }
        }

        /// <summary>
        /// 可浏览的根目录列表。GET /admin/FileManager/Roots
        /// </summary>
        [HttpGet]
        public Result Roots() => Result.Success(_service.Roots());

        /// <summary>
        /// 磁盘用量。GET /admin/FileManager/DiskUsage?path=/
        /// </summary>
        [HttpGet]
        public Result DiskUsage(string? path)
        {
            try { return Result.Success(_service.DiskUsage(path)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
        }

        #endregion

        #region 读 / 下载

        /// <summary>
        /// 读取文本内容（编辑器用）。GET /admin/FileManager/Read?path=/etc/hosts
        /// </summary>
        [HttpGet]
        public Result Read(string? path)
        {
            try { return Result.Success(_service.Read(path)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message, 404); }
        }

        /// <summary>
        /// 下载文件（GET 以便浏览器原生保存）。
        /// GET /admin/FileManager/Download?path=...
        /// </summary>
        [HttpGet]
        public IActionResult Download(string? path)
        {
            try
            {
                var (stream, length, name) = _service.OpenDownload(path);
                Response.Headers.ContentDisposition = FileManagerService.BuildContentDisposition(name);
                return File(stream, "application/octet-stream", enableRangeProcessing: true);
            }
            catch (FileManagerForbiddenException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, Result.Fail(ex.Message, 403));
            }
            catch (FileManagerException ex)
            {
                return NotFound(Result.Fail(ex.Message, 404));
            }
        }

        #endregion

        #region 写操作

        /// <summary>
        /// 保存文本内容。POST /admin/FileManager/Save
        /// <paramref name="expectedHash"/> 为空表示不校验并发（新建时为空）。
        /// </summary>
        [HttpPost]
        public Result Save([FromForm] string path, [FromForm] string content,
            [FromForm] string? expectedHash = null, [FromForm] bool createBackup = false)
        {
            try
            {
                return Result.Success(_service.Save(path, content ?? string.Empty, expectedHash, createBackup, Actor));
            }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>
        /// 新建文件或目录。POST /admin/FileManager/Create
        /// </summary>
        [HttpPost]
        public Result Create([FromForm] string path, [FromForm] bool isDirectory = false)
        {
            try { return Result.Success(_service.Create(path, isDirectory, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>
        /// 重命名 / 同目录改名。POST /admin/FileManager/Rename
        /// </summary>
        [HttpPost]
        public Result Rename([FromForm] string path, [FromForm] string newName)
        {
            try { return Result.Success(_service.Rename(path, newName, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>
        /// 删除文件或目录。POST /admin/FileManager/Delete
        /// </summary>
        [HttpPost]
        public Result Delete([FromForm] string path, [FromForm] bool recursive = false)
        {
            try { return Result.Success(_service.Delete(path, recursive, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
            catch (IOException ex) { return Result.Fail($"删除失败: {ex.Message}"); }
            catch (UnauthorizedAccessException) { return Result.Fail("没有权限删除该文件或目录"); }
        }

        /// <summary>
        /// 修改权限位。POST /admin/FileManager/Chmod
        /// </summary>
        [HttpPost]
        public Result Chmod([FromForm] string path, [FromForm] string mode)
        {
            try { return Result.Success(_service.Chmod(path, mode, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>
        /// 上传文件到指定目录。POST /admin/FileManager/Upload
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
        public async Task<Result> Upload([FromForm] string dirPath, IFormFile file,
            [FromForm] bool overwrite = false, CancellationToken ct = default)
        {
            try
            {
                return Result.Success(await _service.UploadAsync(dirPath, file, overwrite, Actor, ct));
            }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
            catch (OperationCanceledException) { return Result.Fail("上传已取消"); }
            catch (IOException ex) { return Result.Fail($"上传失败: {ex.Message}"); }
        }

        #endregion
    }
}

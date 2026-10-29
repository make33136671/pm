using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models.FileManager;
using Sb.Apps.Pm.Services.FileManager;
using SB.Framework.Core.Expand;
using SB.Framework.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.FileManager
{
    /// <summary>
    /// 服务器文件管理器（admin 后台，JwtAdmin 鉴权）。
    ///
    /// 路由：`[controller]/[action]` → AdminRoutePrefixConvention 自动加 `admin/` 前缀，
    /// 最终为 `/admin/FileManager/*`（前端 baseURL 已含 `/admin`，故前端写 `/FileManager/*`）。
    ///
    /// 权限模型：与网页终端（webterm，本地 root PTY）**对等** —— 管理员即运维。
    /// 路径边界由 appsettings 的 `FileManager` 节 AllowedRoots / DeniedPaths 控制，默认全盘开放。
    /// </summary>
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class FileManagerController : ControllerBase
    {
        private readonly FileManagerService _service;
        private readonly RecycleBinService _recycle;
        private readonly ArchiveService _archive;

        public FileManagerController(FileManagerService service, RecycleBinService recycle, ArchiveService archive)
        {
            _service = service;
            _recycle = recycle;
            _archive = archive;
        }

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
        /// 默认打开路径（来自系统设置 FileManagerDefaultPath）。
        /// GET /admin/FileManager/DefaultPath
        /// </summary>
        [HttpGet]
        public Result DefaultPath()
        {
            try { return Result.Success(_service.GetDefaultPath()); }
            catch (Exception ex) { return Result.Fail($"读取默认路径失败: {ex.Message}"); }
        }

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

        #region 复制 / 移动

        /// <summary>复制。POST /admin/FileManager/Copy</summary>
        [HttpPost]
        public Result Copy([FromForm] string source, [FromForm] string target,
            [FromForm] bool overwrite = false)
        {
            try { return Result.Success(_service.CopyOrMove(source, target, move: false, overwrite, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>移动 / 剪切。POST /admin/FileManager/Move</summary>
        [HttpPost]
        public Result Move([FromForm] string source, [FromForm] string target,
            [FromForm] bool overwrite = false)
        {
            try { return Result.Success(_service.CopyOrMove(source, target, move: true, overwrite, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 批量

        /// <summary>批量删除。POST /admin/FileManager/BatchDelete —— paths 用换行分隔</summary>
        [HttpPost]
        public Result BatchDelete([FromForm] string paths, [FromForm] bool recursive = true)
        {
            try { return Result.Success(_service.BatchDelete(SplitPaths(paths), recursive, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>批量复制 / 移动。POST /admin/FileManager/BatchCopyMove</summary>
        [HttpPost]
        public Result BatchCopyMove([FromForm] string paths, [FromForm] string targetDir,
            [FromForm] bool move = false, [FromForm] bool overwrite = false)
        {
            try { return Result.Success(_service.BatchCopyMove(SplitPaths(paths), targetDir, move, overwrite, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>批量 chmod。POST /admin/FileManager/BatchChmod</summary>
        [HttpPost]
        public Result BatchChmod([FromForm] string paths, [FromForm] string mode)
        {
            try { return Result.Success(_service.BatchChmod(SplitPaths(paths), mode, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>前端用换行分隔多个路径，这里统一切分并去重。</summary>
        private static List<string> SplitPaths(string? raw)
            => (raw ?? string.Empty)
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .ToList();

        #endregion

        #region 回收站

        /// <summary>回收站列表。GET /admin/FileManager/RecycleList</summary>
        [HttpGet]
        public Result RecycleList()
        {
            try { return Result.Success(_recycle.List()); }
            catch (Exception ex) { return Result.Fail($"读取回收站失败: {ex.Message}"); }
        }

        /// <summary>恢复。POST /admin/FileManager/RecycleRestore</summary>
        [HttpPost]
        public Result RecycleRestore([FromForm] string id, [FromForm] string? restoreTo = null)
        {
            try { return Result.Success(_recycle.Restore(id, restoreTo, Actor ?? "-")); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>永久删除单条。POST /admin/FileManager/RecyclePurge</summary>
        [HttpPost]
        public Result RecyclePurge([FromForm] string id)
        {
            try
            {
                _recycle.PurgeOne(id, Actor ?? "-");
                return Result.Success("已永久删除");
            }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>清空回收站。POST /admin/FileManager/RecyclePurgeAll</summary>
        [HttpPost]
        public Result RecyclePurgeAll()
        {
            try { return Result.Success($"已清空回收站（{_recycle.PurgeAll(Actor ?? "-")} 项）"); }
            catch (Exception ex) { return Result.Fail($"清空失败: {ex.Message}"); }
        }

        #endregion

        #region 压缩 / 解压

        /// <summary>压缩。POST /admin/FileManager/Compress</summary>
        [HttpPost]
        public Result Compress([FromForm] string paths, [FromForm] string? outputDir = null,
            [FromForm] string? name = null, [FromForm] string format = "zip",
            [FromForm] int level = 6, [FromForm] bool encrypt = false, [FromForm] string? password = null)
        {
            try
            {
                var req = new CompressRequestDto
                {
                    Paths = SplitPaths(paths),
                    OutputDir = outputDir,
                    Name = name,
                    Format = format,
                    Level = level,
                    Encrypt = encrypt,
                    Password = password,
                };
                return Result.Success(_archive.Compress(req, Actor));
            }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
            catch (Exception ex) { return Result.Fail($"压缩失败: {ex.Message}"); }
        }

        /// <summary>预览压缩包内容。GET /admin/FileManager/ArchivePreview?path=...</summary>
        [HttpGet]
        public Result ArchivePreview(string path)
        {
            try { return Result.Success(_archive.Preview(path)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
            catch (Exception ex) { return Result.Fail($"读取失败: {ex.Message}"); }
        }

        /// <summary>解压。POST /admin/FileManager/Extract</summary>
        [HttpPost]
        public Result Extract([FromForm] string path, [FromForm] string? outputDir = null,
            [FromForm] string? password = null, [FromForm] bool overwrite = false,
            [FromForm] bool useGbkNames = false)
        {
            try
            {
                var req = new ExtractRequestDto
                {
                    Path = path,
                    OutputDir = outputDir,
                    Password = password,
                    Overwrite = overwrite,
                    UseGbkNames = useGbkNames,
                };
                return Result.Success(_archive.Extract(req, Actor));
            }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
            catch (Exception ex) { return Result.Fail($"解压失败: {ex.Message}"); }
        }

        /// <summary>支持的压缩格式（前端下拉）。GET /admin/FileManager/ArchiveFormats</summary>
        [HttpGet]
        public Result ArchiveFormats() => Result.Success(ArchiveService.OutputFormats);

        #endregion

        #region 搜索 / 统计

        /// <summary>文件搜索。GET /admin/FileManager/Search</summary>
        [HttpGet]
        public Result Search(string path, string pattern = "*", bool recursive = true,
            string? contentContains = null, int maxResults = 0, int maxScan = 0)
        {
            try
            {
                return Result.Success(_service.Search(new SearchRequestDto
                {
                    Path = path,
                    Pattern = pattern,
                    Recursive = recursive,
                    ContentContains = contentContains,
                    MaxResults = maxResults,
                    MaxScan = maxScan,
                }));
            }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
            catch (Exception ex) { return Result.Fail($"搜索失败: {ex.Message}"); }
        }

        /// <summary>路径大小统计（属性面板）。GET /admin/FileManager/PathSize?path=...</summary>
        [HttpGet]
        public Result PathSize(string path)
        {
            try { return Result.Success(_service.MeasurePath(path)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        #endregion

        #region 属主 / 备注

        /// <summary>修改属主。POST /admin/FileManager/Chown</summary>
        [HttpPost]
        public Result Chown([FromForm] string path, [FromForm] string owner,
            [FromForm] string? group = null, [FromForm] bool recursive = false)
        {
            try { return Result.Success(_service.Chown(path, owner, group, recursive, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
            catch (FileManagerException ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>系统用户列表（chown 下拉）。GET /admin/FileManager/SystemUsers</summary>
        [HttpGet]
        public Result SystemUsers() => Result.Success(_service.ListSystemUsers());

        /// <summary>读备注。GET /admin/FileManager/GetNote?path=...</summary>
        [HttpGet]
        public Result GetNote(string path)
        {
            try { return Result.Success(_service.GetNote(path)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
        }

        /// <summary>保存备注 / 收藏。POST /admin/FileManager/SaveNote</summary>
        [HttpPost]
        public Result SaveNote([FromForm] string path, [FromForm] string? note = null,
            [FromForm] bool favorite = false)
        {
            try { return Result.Success(_service.SaveNote(path, note, favorite, Actor)); }
            catch (FileManagerForbiddenException ex) { return Result.Fail(ex.Message, 403); }
        }

        /// <summary>收藏列表。GET /admin/FileManager/Favorites</summary>
        [HttpGet]
        public Result Favorites() => Result.Success(_service.ListFavorites());

        #endregion
    }
}

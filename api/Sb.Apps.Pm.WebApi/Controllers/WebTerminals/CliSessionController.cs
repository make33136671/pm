using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.WebTerminals;

namespace Sb.Apps.Pm.WebApi.Controllers.WebTerminals
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class CliSessionController : Controller
    {
        private readonly CliSessionService _service;
        public CliSessionController(CliSessionService service) { _service = service; }

        [HttpGet]
        public Result List(int? projectId, CliSessionStatus? status, string? owner,
            string field = "", string order = "", int page = 1, int pageSize = 20)
            => _service.GetList(projectId, status, owner, field, order, page, pageSize);

        [HttpGet]
        public Result Detail(int id) => _service.Detail(id);

        [HttpPost]
        public Result Create([FromForm] string? name, [FromForm] int? projectId, [FromForm] int? subProjectId,
            [FromForm] int cols = 120, [FromForm] int rows = 30, [FromForm] string? cwd = null)
            => _service.Create(name, projectId, subProjectId, cols, rows, cwd);

        [HttpPost]
        public Result Rename([FromForm] int id, [FromForm] string name) => _service.Rename(id, name);

        [HttpPost]
        public Result Destroy([FromForm] int id) => _service.Destroy(id);

        [HttpPost]
        public Result ClearLog([FromForm] int id) => _service.ClearLog(id);

        [HttpPost]
        public Result Del([FromForm] int id) => _service.Del(id);

        [HttpGet]
        public IActionResult DownloadLog(int id)
        {
            var abs = _service.GetLogAbsPath(id);
            if (abs == null || !System.IO.File.Exists(abs))
                return NotFound();
            return File(System.IO.File.OpenRead(abs), "text/plain", $"webterm-{id}.log");
        }
    }
}

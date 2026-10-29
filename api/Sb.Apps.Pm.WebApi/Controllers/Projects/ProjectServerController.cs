using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class ProjectServerController : Controller
    {
        private readonly ProjectServerService _service;

        public ProjectServerController(ProjectServerService service)
        {
            _service = service;
        }

        [HttpGet]
        public Result List(int? projectId, int? subProjectId, int? serverId,
            string field = "", string order = "", int page = 1, int pageSize = 20)
            => _service.GetList(projectId, subProjectId, serverId, field, order, page, pageSize);

        [HttpGet]
        public Result Detail(int id) => _service.Detail(id);

        [HttpPost]
        public Result Add([FromForm] ProjectServer entity) => _service.Create(entity);

        [HttpPost]
        public Result Edit([FromForm] ProjectServer entity) => _service.Edit(entity);

        [HttpPost]
        public Result Del([FromForm] int id) => _service.Del(id);
    }
}

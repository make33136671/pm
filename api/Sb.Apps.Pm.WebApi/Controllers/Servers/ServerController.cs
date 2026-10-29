using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services;

namespace Sb.Apps.Pm.WebApi.Controllers.Servers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class ServerController : Controller
    {
        private readonly ServerService _service;

        public ServerController(ServerService service)
        {
            _service = service;
        }

        [HttpGet]
        public Result List(string? keyword, ServerStatus? status,
            string field = "", string order = "", int page = 1, int pageSize = 20)
            => _service.GetList(keyword, status, field, order, page, pageSize);

        [HttpGet]
        public Result Detail(int id) => _service.Detail(id);

        [HttpGet]
        public Result SimpleList() => _service.SimpleList();

        [HttpPost]
        public Result Add([FromForm] Server server) => _service.Create(server);

        [HttpPost]
        public Result Edit([FromForm] Server server) => _service.Edit(server);

        [HttpPost]
        public Result Del([FromForm] int id) => _service.Del(id);

        [HttpGet]
        public Result DecryptField(int serverId, string fieldName)
            => _service.DecryptField(serverId, fieldName);

        [HttpPost]
        public async Task<Result> Discover([FromForm] int id)
            => await _service.DiscoverFromBaota(id);

        [HttpPost]
        public async Task<Result> TestConnection([FromForm] int id)
            => await _service.TestConnection(id);
    }
}

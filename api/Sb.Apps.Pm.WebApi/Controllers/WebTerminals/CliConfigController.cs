using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.WebTerminals;
using Sb.Apps.Pm.Services.WebTerminals;

namespace Sb.Apps.Pm.WebApi.Controllers.WebTerminals
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class CliConfigController : Controller
    {
        private readonly CliConfigService _service;
        public CliConfigController(CliConfigService service) { _service = service; }

        [HttpGet]
        public Result Get() => _service.Get();

        [HttpPost]
        public Result Edit([FromForm] CliConfig cfg) => _service.Edit(cfg);
    }
}

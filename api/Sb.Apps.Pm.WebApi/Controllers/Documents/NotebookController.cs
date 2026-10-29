using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Documents;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class NotebookController : Controller
{
    private readonly NotebookService _service;
    public NotebookController(NotebookService service) { _service = service; }

    [HttpGet]
    public Result List(string? keyword = null, string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetList(keyword, field, order, page, pageSize);

    [HttpPost]
    public Result Add([FromForm] Notebook notebook) => _service.Create(notebook);

    [HttpPost]
    public Result Edit([FromForm] Notebook notebook) => _service.Edit(notebook);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    [HttpPost]
    public Result Pin([FromForm] int id) => _service.Pin(id);
}

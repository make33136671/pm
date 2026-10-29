using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Requirements;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class RequirementCommentController : Controller
{
    private readonly RequirementCommentService _service;
    public RequirementCommentController(RequirementCommentService service) { _service = service; }

    [HttpGet]
    public Result List(int requirementId, int page = 1, int pageSize = 50) => _service.GetList(requirementId, page, pageSize);

    [HttpPost]
    public Result Add([FromForm] RequirementComment comment) => _service.Create(comment);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);
}

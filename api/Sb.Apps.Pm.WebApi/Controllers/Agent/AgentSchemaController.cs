using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Services.Agent;
using SB.Feature.AccessToken.Services;
using SB.Framework.Core.Authentication;
using SB.Framework.Models;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

/// <summary>
/// PM 数据模型 Schema API(#90/#91)。
/// AI Agent 调 GET /agent/schema 一次拿到 PM 全模型:实体/字段/外键/枚举/派生规则/关联约束。
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = "JwtAdmin")]
[Scope("read:project")]
[Route("/agent/schema")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentSchemaController : ControllerBase
{
    private readonly PmSchemaService _schemaService;

    public AgentSchemaController(PmSchemaService schemaService) => _schemaService = schemaService;

    /// <summary>返回 PM 数据模型全貌(实体/字段/派生规则/关联约束)。</summary>
    [HttpGet]
    public Result Get() => Result.Success(_schemaService.GetSchema());
}

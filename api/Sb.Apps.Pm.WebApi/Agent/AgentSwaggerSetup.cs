using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Sb.Apps.Pm.WebApi.Agent;

public static class AgentSwaggerSetup
{
    public static void AddAgentSwaggerDoc(SwaggerGenOptions opt)
    {
        opt.SwaggerDoc("agent", new OpenApiInfo
        {
            Title = "pm Agent API",
            Version = "v1",
            Description = "外部 AI Agent 用的 REST 接口(Bearer Token 鉴权)。" +
                          "Spec: docs/superpowers/specs/2026-05-15-pm-agent-api-design.md"
        });
        // framework 内置 doc 名 "v1";无 GroupName 的 controller 默认归 v1,
        // group=agent 的 controller(后续 PR2-PR4 加 [ApiExplorerSettings(GroupName = "agent")])
        // 只进 /swagger/agent/swagger.json,避免污染 v1 doc。
        opt.DocInclusionPredicate((docName, apiDesc) =>
        {
            var group = apiDesc.GroupName ?? "v1";
            return docName == "v1" ? group == "v1" : group == docName;
        });
        opt.AddSecurityDefinition("AgentBearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Authorization: Bearer sk_pm_..."
        });
        opt.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "AgentBearer"
                }
            }] = Array.Empty<string>()
        });
    }
}

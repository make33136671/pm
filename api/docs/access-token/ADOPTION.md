# PM 接入框架统一 AccessToken — 改动清单

> 背景:`MasterKey` 与 token 已统一为框架功能包 `SB.Feature.AccessToken`(账号即 token)。
> PM 原自有 agent token 栈退役,`/agent/*` 改用框架的 `[Scope]` + `IApiKeyContext`。
>
> **前置(阻塞项):** 本接入需先发布框架包再做 —— PM 经 NuGet 包引用框架
> (`SB.Framework.*.NoLicense` 0.3.1)。必须:
> 1. 框架 `feature/access-token-unified-auth` 分支打包发布:`SB.Framework.*`(含核心缝)新版本
>    + 新增 `SB.Feature.AccessToken`(及 `.NoLicense` 变体)到内部 NuGet feed。
> 2. PM 按下方改 `csproj` 引用新版本,再编译。
> 离线环境无法发包,故以下为可直接执行的改动清单(发包后套用即编译通过)。

## 1. csproj

`Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj`:加引用
```xml
<PackageReference Include="SB.Feature.AccessToken.NoLicense" Version="0.1.0" />
```
并把 `SB.Framework.*.NoLicense` 由 `0.3.1` 升到含核心缝的新版本(如 `0.4.0`)。

## 2. /agent/* 控制器(5 个:Project/Progress/Requirement/Task/Ci)

逐文件机械替换:
| 原 | 新 |
|---|---|
| `using Sb.Apps.Pm.Services.Agent;` | `using SB.Feature.AccessToken.Services;` |
| `[Authorize(AuthenticationSchemes = AgentBearerOptions.SchemeName)]` | `[Authorize(AuthenticationSchemes = "JwtAdmin")]` |
| `[AgentScope("…")]` | `[Scope("…")]` |
| `IAgentContext` | `IApiKeyContext` |

scope 字符串不变(`read:task` / `write:requirement` …),语义一致。
注:框架 `ScopeFilter` 对**受限 key 默认 deny 未声明 [Scope] 的端点**,与原 agent 行为一致;
密码登录的管理员可照常访问 /agent/*(非 key 请求放行)。

## 3. Program.cs

- 删 `using Sb.Apps.Pm.Services.Agent;`
- `AgentTokenCli.TryHandle(...)`:删除(CLI 子命令由框架侧或后续 nexus CLI 承担)
- `AgentSwaggerSetup`:可保留(只是 /agent swagger 分组),无依赖删除问题

## 4. 删除 PM 自有 agent 栈

- 删目录 `Sb.Apps.Pm.Services/Agent/`(整套:Service/Handler/RateLimiter/Audit/Scope/CLI…)
- 删目录 `Sb.Apps.Pm.Models/Agent/`(AgentToken / AgentTokenAuditLog)
- 删 `Sb.Apps.Pm.WebApi/Controllers/Agent/AgentTokenController.cs`(管理端由框架 `/admin/AccessToken/*` 取代)
- `PmCoreFeature.ConfigureServices` 里删除 `services.AddAgentAuth();`(框架功能包自注册认证/scope 过滤)

## 5. 数据迁移

执行 `migrate-agent-token.sql`(见同目录)。搬 TokenHash → 已签发 bot key 明文不变继续可用。

## 6. 验证(发包+套用后)

- `dotnet build` PM 通过
- 旧 `sk_pm_…` 明文 `Authorization: Bearer` 调 `/agent/task/list` → 200(迁移保活)
- 无 scope 的 key 调未声明 [Scope] 的 admin 接口 → 403
- 错误 key 连发 → 触发 IP 节流(401)
- `X-Master-Key` 调 PM → 401(静态通道已废)

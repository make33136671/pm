# pm Agent API 设计(一期 MVP)

| 项 | 值 |
|---|---|
| 创建日期 | 2026-05-15 |
| 作者 | saibotianlong + Claude Code |
| 状态 | Approved,准备进 writing-plans |
| 关联仓库 | `apps/pm/api`(主)、`apps/pm/admin`(零改动,二期再加 token 管理页)、`apps/pm/mcp`(无关) |

## 背景

用户希望把自建的 AI 助理(Bot / Agent)接入 pm 系统,让 AI 能:

- 管理需求(增删改查 + 状态流转)
- 管理开发进度(任务流转 + 项目健康度 + 发布/构建记录)
- (二期)读取和操作网页终端(webterm)

外部 Agent 形态:**用户自建的 HTTP client**(协议自由,REST 最直接)。

pm 现状:已有完整业务实体 + Service 层 + admin JWT 鉴权 + WebTerminal 模块(WebSocket + PtyManager),但 **没有面向外部 Agent 的 HTTP 接口和鉴权机制**。pm mcp 已经暴露 9 大类工具,但走 stdio 模式,只能本地 MCP 客户端用,不能给外部 Bot 调。

## 目标(一期)

1. 提供独立的 Agent 鉴权机制(Bearer Token,跟 admin JWT 隔离)
2. 镜像现有 pm 业务能力为面向 Agent 的 REST 接口(21 端点)
3. 操作者审计:`ProjectActivity.OperatorName` / `TaskItemLog.Operator` 用 `agent:<token-name>` 约定前缀区分 admin 操作和 Agent 操作
4. 给 Agent 端单独导出一份 swagger doc(`/swagger/agent/swagger.json`)
5. 命令行子命令(`dotnet run -- agent-token create/list/revoke`)

## 非目标(一期不做)

- webterm 多订阅者改造 + Agent webterm 接入(二期 — `PtyManager.RuntimeSession.Client` 当前是单 client,二期改 `Clients: Dict<Guid, IPtyClient>` 广播)
- AI Memory / Notebook / Mindmap / FeatureModule / Document 暴露(三期按需)
- 反向触发(pm 主动调 Agent webhook,即 brainstorming 阶段评估的 C 方案)
- API 限流(等量上来再加,字段保留)
- 跨项目细粒度 scope(`Scopes` 字段保留,一期 token 默认全权)
- admin 端 Agent token 管理页(一期 CLI 创建够用)
- TS/Python SDK 包(等需要再做)

## §1 架构

```
                              ┌─────────────────────────────┐
   外部 Agent (你的 Bot) ────►│  pm api (.NET 8)            │
   HTTP + Bearer              │                             │
       │                      │  ┌──────────────────────┐   │
       │                      │  │ AgentBearerAuthHandler│  │  鉴权
       ▼                      │  └──────────────────────┘   │
   ┌─────────────────────┐    │             │               │
   │ /agent/...          │◄───┤  AgentControllers (新增):    │
   │   requirement/*     │    │   - AgentRequirementCtrl    │  业务镜像
   │   task/*            │    │   - AgentTaskCtrl           │
   │   project/*         │    │   - AgentProjectCtrl        │
   │   progress/*        │    │   - AgentProgressCtrl       │
   │                     │    │   (薄壳 — 复用既有 Service) │
   └─────────────────────┘    │             │               │
                              │  ┌──────────▼─────────────┐ │
                              │  │ 既有 Services           │ │  零改造
                              │  │  RequirementService     │ │
                              │  │  TaskService            │ │
                              │  │  ProjectService         │ │
                              │  │  HealthScoreService     │ │
                              │  │  ReleaseService         │ │
                              │  └────────────────────────┘ │
                              └─────────────────────────────┘
```

三块边界:

1. **Agent Controllers 层**(新增):4 个 controller(`/agent/{requirement,task,project,progress}/*`),薄壳,直接转调既有 Service。**不复用 admin controller** — 因为鉴权 scheme 不同 + 命名/字段对 Agent 友好。
2. **Token 鉴权层**(新增):`AgentBearerAuthenticationHandler` + `AgentToken` entity。和 admin JWT scheme 完全隔离。
3. **既有 Service 层**:零改造,Service 接受 `string operator` 参数,Controller 传 `agent:<token-name>` 进去就完事。

变更范围(行数估算):

- 新增 entity:1 个(`AgentToken`)
- 新增 controller:4 个(每个 ~100 行,共 ~400 行)
- 新增 auth handler + DI 注册:~150 行
- 新增 CLI 子命令:~100 行
- swagger group 注册:~30 行
- 测试:~400 行
- pm admin 改动:0(一期)

## §2 Token 鉴权 + 操作者追踪

### 2.1 `AgentToken` entity

```csharp
[Table(Name = "sb_pm_agent_token")]
[Index("idx_agent_token_token_hash", nameof(TokenHash), true)]  // unique
[Index("idx_agent_token_name", nameof(Name), false)]
public class AgentToken : IEntity, ISoftDelete
{
    [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Required, StringLength(100)]
    [Column(Name = "Name")]
    public string Name { get; set; }

    // 只存 sha256 hash,明文不进 db
    [Required, StringLength(64)]
    [Column(Name = "TokenHash")]
    public string TokenHash { get; set; }

    // 前 N 位明文给 list 显示识别(如 'sk_pm_abc...')
    [Required, StringLength(16)]
    [Column(Name = "TokenPrefix")]
    public string TokenPrefix { get; set; }

    // 'requirement,task,project,progress' 逗号分隔,null = 全权(一期默认 null)
    [Column(Name = "Scopes", StringLength = 500)]
    public string? Scopes { get; set; }

    [Column(Name = "ExpiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [Column(Name = "LastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime CreatedTime { get; set; }

    [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
    public bool IsDeleted { get; set; }
}
```

约束清单(memory 教训内化):

- `ISoftDelete` → FreeSql 自动追加 `IsDeleted = false` filter(吊销走软删,正常查询自动过滤)
- 索引名带 `idx_agent_token_*` 前缀(sqlite 测试同名冲撞 + PG silent skip 双重风险)
- `InsertValueSql = "false"` 不是 `"0"`(PG 兼容)
- `Scopes` `StringLength = 500` 是 varchar,不是 longtext;如果未来 scope 复杂化升 longtext 用 `-2`

### 2.2 鉴权 scheme

```csharp
public class AgentBearerOptions : AuthenticationSchemeOptions { }

public class AgentBearerAuthenticationHandler : AuthenticationHandler<AgentBearerOptions>
{
    // 解析 Authorization: Bearer sk_pm_xxxx
    // sha256 hash 比对 db → 找到 AgentToken → 检 ExpiresAt + IsDeleted
    // 成功:写 ClaimsPrincipal,带 claim agent_token_id / agent_token_name
    // 失败:返回 AuthenticateResult.Fail(...) → middleware 给 401 + Result.code = 4002/4003
}

// Program.cs
builder.Services
    .AddAuthentication()
    .AddScheme<AgentBearerOptions, AgentBearerAuthenticationHandler>("AgentBearer", _ => { });
```

`/agent/*` controller 全部用 `[Authorize(AuthenticationSchemes = "AgentBearer")]`。和 `JwtAdmin` scheme 完全隔离,不会用 admin token 误鉴权进 Agent 接口。

### 2.3 `IAgentContext` 跨 controller 共享

```csharp
public interface IAgentContext
{
    int TokenId { get; }
    string TokenName { get; }
    bool HasScope(string scope);   // 一期 Scopes=null 时永真
    string OperatorTag => $"agent:{TokenName}";   // 写审计用
}
```

在 `AgentBearerAuthenticationHandler` 成功后写 `HttpContext.Items["agent"] = ...`,再 `services.AddScoped<IAgentContext>(sp => sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.Items["agent"] as IAgentContext ?? throw ...)`。Controller `[FromServices] IAgentContext agentCtx` 拿到。

### 2.4 命令行子命令

pm api 入口加参数解析,识别 `agent-token` 子命令:

```bash
# 创建
dotnet run --project Sb.Apps.Pm.WebApi -- agent-token create --name my-bot
# 输出:
#   created: id=1, name=my-bot, prefix=sk_pm_abc
#   secret (复制保存,只显示一次): sk_pm_abcdef1234567890abcdef...

# 列表
dotnet run --project Sb.Apps.Pm.WebApi -- agent-token list

# 吊销(软删)
dotnet run --project Sb.Apps.Pm.WebApi -- agent-token revoke --id 1
```

明文 token 生成:`sk_pm_` 前缀 + 32 字节 `RandomNumberGenerator.GetBytes` base64url 编码。

### 2.5 操作者追踪(零 schema 改)

现状 codebase:

- `ProjectActivity.OperatorName`(string,100):全局项目级活动审计
- `TaskItemLog.Operator`(string,100):任务级日志,带 Action + Message + Duration
- `Requirement.Source` enum + `SourceRef`:记录"这条需求是谁/哪儿建的"(Manual/Chat/Ai),`Source` 不变,`SourceRef` 不变

约定:Service 写入 Activity/TaskLog 时,Operator 参数从 controller 传入:

- admin 调用 → `admin:<adminUsername>`
- Agent 调用 → `agent:<tokenName>`

admin 前端展示时按前缀着色 / 加 icon。审计查询按 `Operator LIKE 'agent:%'` 筛 Agent 操作。

**零 schema 改动**(对比"加 ActorType 字段"或"每张表加 ActorType+ActorRef" 方案,实现成本最低)。

## §3 业务 REST 端点(21 个)

### 3.1 端点清单

```
[Auth: Bearer AgentToken]  ;  [Content-Type: application/x-www-form-urlencoded]

/agent/requirement/                                     ── 7 端点
  GET  list?projectId=&subProjectId=&status=&keyword=&page=&pageSize=&field=&order=
  GET  detail?id=
  POST create        [Title, ProjectId, Content?, SubProjectId?, Type, Complexity?, Assignee?, ParentId?]
  POST update        [Id, Title?, Content?, Status?, Complexity?, Assignee?, DueDate?, ...]
  POST delete        [Id]
  POST updateStatus  [Id, Status, Comment?]
  POST addComment    [RequirementId, Content]

/agent/task/                                            ── 8 端点
  GET  list?projectId=&status=&assignee=&page=&pageSize=
  GET  detail?id=
  GET  available?projectId=
  GET  logs?taskId=&page=
  POST claim         [Id]
  POST start         [Id]
  POST complete      [Id, Message?]
  POST fail          [Id, Message]
  POST cancel        [Id, Message?]
  POST resume        [Id]

/agent/project/                                         ── 3 端点
  GET  list?keyword=&status=&page=
  GET  detail?id=
  GET  subList?projectId=

/agent/progress/                                        ── 3 端点
  GET  healthScore?projectId=
  GET  builds?projectId=&page=
  GET  releases?projectId=&page=
  GET  changelog?projectId=&fromVersion=&toVersion=
```

### 3.2 风格约定

- GET = query string,POST = `[FromForm]`(对齐 pm 既有 controller 风格,memory 反复强调)
- 嵌套数据用 `XxxJson` 字段(form 不支持嵌套)
- POST 不写 `[FromBody]`(memory:违反 → 415)
- 字段命名沿用既有 entity 的 PascalCase

### 3.3 响应

复用 framework `Result<T>` / `Result`:

```json
{ "code": 0, "message": "ok", "data": { ... } }
{ "code": 1001, "message": "需求不存在", "data": null }
```

### 3.4 OpenAPI 导出

```csharp
// Program.cs
services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("admin", new OpenApiInfo { Title = "pm admin", Version = "v1" });
    c.SwaggerDoc("agent", new OpenApiInfo { Title = "pm agent api", Version = "v1" });

    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        var group = apiDesc.GroupName ?? "admin";
        return group == docName;
    });
});

// Agent controllers
[ApiExplorerSettings(GroupName = "agent")]
public class AgentRequirementController : ControllerBase { ... }
```

暴露 `/swagger/agent/swagger.json`,Agent 端能用 openapi-typescript / openapi-generator 生成 client。

## §4 错误处理 + 边界 + 测试

### 4.1 错误码

```
code 取值(沿用 Result.code 体系):
   0       成功
1xxx       业务错(资源不存在 / 状态非法 / ...)
2xxx       参数错(必填缺失 / 格式错)
4001       token 缺失
4002       token 无效 / 已吊销 / hash 不匹配
4003       token 过期(ExpiresAt < now())
4004       scope 不足(一期不启用)
4290       限流(一期不启用)
5000       服务器错(异常已记 saibo log)
```

HTTP status:200(业务结果)/ 401(token 错)/ 403(scope 不足)/ 429(限流)/ 500(异常)。

### 4.2 边界场景

- **同名 token**:Name 不强制唯一(允许"my-bot v1" / "my-bot v2" 都用 my-bot 调试)— 但 TokenHash 唯一
- **吊销 token**:`IsDeleted = true`,ISoftDelete 自动 filter 让后续查 token 失败 → 4002
- **过期 token**:`ExpiresAt < DateTime.Now` → 4003。ExpiresAt = null = 永不过期(家用默认)
- **LastUsedAt 更新**:成功鉴权后异步 fire-and-forget 更新,不阻塞请求(避免每次请求都等一次 update commit)
- **明文 token 显示**:`agent-token create` 输出后,db 只存 hash;`agent-token list` 只能看 prefix。**遗忘 = 重新创建**

### 4.3 测试

最小集(一期必做):

- `AgentTokenServiceTests`:create 后 hash 一致性 / list 不包含明文 / revoke 软删 / 过期判断
- `AgentBearerAuthenticationHandlerTests`:有效 / 过期 / 吊销 / 缺失 header / 错误 prefix / hash 不匹配
- 每个 Agent controller:1 个 happy path 集成测试(in-memory sqlite,跟 pm 既有测试一致)

不做(留二期):

- 限流测试
- scope 测试(一期 token 默认全权)
- 并发压测

### 4.4 framework 回流评估

- `AgentToken` entity:**留 pm**(业务实体,各派生项目的 token 表语义可能不同)
- `Bearer*AuthenticationHandler` 模式:一期先在 pm,跑稳后看是否**回流到 `framework/core`** 给其他派生项目用(对齐 memory "派生项目要用 framework 工具,不要重复造轮子")
- token CLI 命令骨架(参数解析 / 子命令分发):一期先在 pm 内联,若 nexus / sb-lottery 也要类似 CLI 子命令,再下沉

## §5 实施切分(4 PR)

| 顺序 | PR | 内容 | 估时 |
|---|---|---|---|
| 1 | **基础设施** | `AgentToken` entity + FreeSql 迁移 + `AgentBearerAuthenticationHandler` + DI 注册 + `IAgentContext` + CLI 子命令(create/list/revoke)+ agent swagger doc 注册 + `AgentTokenServiceTests` + `AgentBearerAuthenticationHandlerTests` | 1-1.5 天 |
| 2 | **Requirement** | `AgentRequirementController`(7 端点)+ 接 `IAgentContext.OperatorTag` 到 `RequirementService` operator 参数 + 集成测试 | 1 天 |
| 3 | **Task** | `AgentTaskController`(8 端点)+ `TaskItemLog.Operator` 接 agent context + 集成测试 | 1 天 |
| 4 | **Project + Progress** | `AgentProjectController`(3 端点)+ `AgentProgressController`(3 端点)+ 收尾 README/部署说明 + 完整端到端 curl smoke test | 1 天 |

每个 PR 都能独立 deploy。Ship 节奏:

- PR 1 合完:系统能鉴权,但还没业务接口
- PR 2 合完:Agent 能管需求
- PR 3 合完:Agent 能管任务
- PR 4 合完:一期收官,健康度 / 发布记录可读

每 PR 验收(verification-before-completion 风格):

- 单元 + 集成测试全绿
- 手动 curl 一遍 happy path,粘贴输出到 PR 描述
- swagger UI 能看到新加端点
- pm api 重启后旧 admin 功能没回归(memory:后端代码改完自动重启)

## 决策记录(brainstorming 过程)

| # | 问题 | 决策 |
|---|---|---|
| Q1 | AI 助理形态? | **外部 AI(第三方 ChatBot / 自建 LLM 服务)** |
| Q2 | 调用协议? | **自建 Bot / Agent,自己写 HTTP client** → REST 最直接 |
| Q3 | 终端协作模式? | **独立 + 共享会话两种都要**(放二期) |
| Q4 | 架构方案? | **A 开放 Agent API** |
| Q5 | Token 管理? | **简单 db + 命令行创建**(admin 页留二期) |
| Q6 | 业务表 modified-by 怎么标? | (Q6 选项最终被 Q7 替换) |
| Q7 | 操作者追踪方式? | **a 零 schema 改,OperatorName 用 `agent:<name>` 约定前缀** |
| Q8 | 一期范围? | **21 端点全上 + 不限流 + agent swagger 独立 doc** |

## 附录 A:开发自检清单(写代码时贴在 PR 描述)

- [ ] `AgentToken` entity 索引名带 `idx_agent_token_*` 前缀
- [ ] `bool IsDeleted` 用 `InsertValueSql = "false"`(PG 兼容)
- [ ] Controller 用 `[FromForm]` 不写 `[FromBody]`
- [ ] 嵌套字段用 `XxxJson` 接 JSON string
- [ ] 改完后端立即重启 pm api(memory)
- [ ] commit 用中文 + Conventional Commit(`feat(agent-api): ...`)
- [ ] `git add` 显式列文件,不用 `-A` / `.`
- [ ] 不跳 hooks,不强推 main
- [ ] 派生项目漂移审计:本次改动是否应回流 framework(`framework/core` Bearer auth handler 共用价值评估)

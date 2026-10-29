# Agent Token 增强计划

> **目标**: 基于现有 `AgentToken` 认证体系，补齐管理 API、细粒度权限控制、使用审计和安全管理功能。
> **状态**: Phase 1-8 全部完成（含集成测试 + §8 人工验收 11/11 通过）
> **创建时间**: 2026-05-31
> **版本**: v2.3 (2026-06-02 §8 人工验收通过 + Skill v2.1)

---

## 修订记录

| 版本 | 日期 | 审阅者 | 修订要点 |
|------|------|--------|----------|
| v2.0 | 2026-05-31 | 原作者 | 基于代码审计重写，废弃原 AccessKey 独立方案 |
| v2.1 | 2026-05-31 | Claude | 修正与实际代码不符的事实(端点数、软删除过滤器);设计优化(写放大、Scope/RateLimit、审计简化);时间估算调整 |
| v2.2 | 2026-06-02 | Claude | 补齐 Phase 6.5/6.6 原跳过的集成测试(Filter 9 + Controller 12 + AuditService 5,共 26 用例,全量 54→80 通过);修复审计表 CreatedTime 的 PostgreSQL 方言隐患(InsertValueSql="now()" → 应用层赋值);§3.2 实体示例同步 |

> **[2026-05-31 修订 · Claude]** 本文档所有 Claude 审阅修订点在正文中以 `> **[2026-05-31 修订 · Claude]**` 前缀的引用块标注;原方案保留不删除,以便对照差异。

---

## 1. 代码审计结论

### 1.1 现有 Agent Token 体系（已完成）

PM 项目已实现完整的 Agent Bearer Token 认证体系：

| 组件 | 状态 | 文件 |
|------|------|------|
| Token 实体 | ✅ 已实现 | `Sb.Apps.Pm.Models/Agent/AgentToken.cs` |
| Token 服务 | ✅ 已实现 | `Sb.Apps.Pm.Services/Agent/AgentTokenService.cs` |
| Bearer 认证 Handler | ✅ 已实现 | `AgentBearerAuthenticationHandler.cs` |
| DI 注册 | ✅ 已实现 | `AgentAuthExtensions.cs` |
| IAgentContext | ✅ 已实现 | `IAgentContext.cs` / `AgentContext.cs` |
| CLI 管理工具 | ✅ 已实现 | `AgentTokenCli.cs` (create/list/revoke) |
| Swagger 分组 | ✅ 已实现 | `AgentSwaggerSetup.cs` |
| 独立认证 Scheme | ✅ 已实现 | `AgentBearer` 与 `JwtAdmin` 隔离 |

**Token 安全设计**：
- SHA-256 哈希存储，明文不入库 ✅
- 32 字节随机熵 (`sk_pm_<base64url>`) ✅
- 软删除实现吊销 ✅
- ExpiresAt 过期控制 ✅
- TokenPrefix 仅展示前 12 位 ✅

### 1.2 现有 Agent REST API（已完成）

AgentBearer 体系下 4 个 Controller，共 **24 个端点**;另有 1 个独立 `TaskAgentController`(X-Api-Key 鉴权),合计 **30 端点**:

| Controller | 端点数 | 路由 | 鉴权 |
|-----------|--------|------|------|
| AgentRequirementController | 7 | `/agent/requirement/[action]` | AgentBearer |
| AgentTaskController | 10 | `/agent/task/[action]` | AgentBearer |
| AgentProjectController | 3 | `/agent/project/[action]` | AgentBearer |
| AgentProgressController | 4 | `/agent/progress/[action]` | AgentBearer |
| TaskAgentController | 6 | `Controllers/Tasks/TaskAgent/*` | **X-Api-Key Header** |

> **[2026-05-31 修订 · Claude]** v2.0 漏掉了 `TaskAgentController`(独立 X-Api-Key 体系)。本次增强**默认只覆盖 AgentBearer 体系**(Scope 检查、审计日志均不应用到 TaskAgent);若需统一鉴权,需在本计划完成后单独立项迁移 TaskAgent → AgentBearer,届时再决定是否保留 X-Api-Key 兼容期。**决策必须显式写入实施前的 PR 描述**,避免漏覆盖盲区。

### 1.3 缺失功能（本次计划要做的）

| 缺失项 | 严重度 | 说明 |
|--------|--------|------|
| **Admin HTTP API** | 🔴 高 | 只有 CLI，无 HTTP API 管理 Token，无法在 UI 或 CI/CD 中远程管理 |
| **Scope 权限启用** | 🔴 高 | `Scopes` 字段存在但未接入任何 Controller 拦截 |
| **使用审计日志** | 🟡 中 | 只有 `LastUsedAt` 时间戳，无 IP/路径/端点/结果记录 |
| **Token 创建者/操作者** | 🟡 中 | 无 `CreatedBy`/`ModifiedBy`/`RevokedBy` 字段 |
| **使用统计** | 🟢 低 | 无请求计数、无速率限制 |
| **Token 详情/更新** | 🟢 低 | 无 `Update`/`Detail` 管理接口 |

---

## 2. 设计原则

### 2.1 不推翻现有体系

现有 AgentToken 体系设计合理、安全性好。本次计划**只做增量增强**，不改变现有 Token 生成、存储、认证逻辑。

### 2.2 向后兼容

- 已创建的 Token 无需迁移
- `Scopes=null` 仍表示全权（兼容一期默认行为）
- 新增字段全部可空，不破坏现有表结构

---

## 3. 数据模型变更

### 3.1 `AgentToken` 实体增强

**文件**: `Sb.Apps.Pm.Models/Agent/AgentToken.cs`

> **[2026-05-31 修订 · Claude]** 字段清单经写放大评估后大幅精简,设计原则:
> - **主表只存身份/状态/配额**,不存高频更新的"使用痕迹"和事件型审计数据
> - `LastUsedIp` / `LastUsedPath` / `RequestCount` → 改入 `AgentTokenAuditLog`(本来每次请求就要写,无新增写入)
> - `RevokedBy` → 改入 `AgentTokenAuditLog`(Revoke 是事件,事件型数据归审计)
> - `LastUsedAt` 保留在主表,但**节流到每分钟最多更新一次**(避免每请求 UPDATE 主表导致行锁)
> - `MaxRequests` 累计上限改为 `RateLimitPerMinute` 滑动窗口速率限制(累计上限对常驻 Agent 不友好,见 §4 修订)

新增字段（**修订后**）:

```csharp
// 在现有字段后追加：

/// <summary>创建者标识 (admin username 或 "cli")</summary>
[StringLength(50)]
[Column(Name = "CreatedBy")]
public string CreatedBy { get; set; } = "";

/// <summary>最后修改者</summary>
[StringLength(50)]
[Column(Name = "ModifiedBy")]
public string? ModifiedBy { get; set; }

/// <summary>吊销原因 (RevokedBy/RevokedAt 见审计表)</summary>
[StringLength(500)]
[Column(Name = "RevokeReason")]
public string? RevokeReason { get; set; }

/// <summary>速率限制：每分钟最大请求数 (0 或 null = 不限)</summary>
[Column(Name = "RateLimitPerMinute")]
public int? RateLimitPerMinute { get; set; }

/// <summary>备注</summary>
[Column(Name = "Notes", StringLength = 2000)]
public string? Notes { get; set; }

/// <summary>最后更新时间</summary>
[Column(Name = "UpdatedTime", DbType = "timestamp")]
public DateTime? UpdatedTime { get; set; }
```

**索引变更**：
- 新增 `idx_agent_token_created_by` (CreatedBy)
- 新增 `idx_agent_token_updated` (UpdatedTime)

> **[2026-05-31 修订 · Claude]** **删除字段**(v2.0 原方案有,v2.1 移除):
> | 原字段 | 去向 | 理由 |
> |--------|------|------|
> | `RevokedBy` | 改入 `AgentTokenAuditLog.Operator`(Action=Revoked 行) | 事件型数据 |
> | `RequestCount` | 改入 `AgentTokenAuditLog`,按需 `COUNT(*)` 聚合 | 写放大 |
> | `MaxRequests` | 替换为 `RateLimitPerMinute` | 累计上限不适合常驻 Agent |
> | `LastUsedIp` | 改入 `AgentTokenAuditLog.IpAddress` | 写放大 |
> | `LastUsedPath` | 改入 `AgentTokenAuditLog.RequestPath` | 写放大 |
>
> **保留但节流**:`LastUsedAt`(已存在)— 每个 Token 最多每分钟更新一次,通过内存缓存 `Dictionary<int, DateTime>` 节流。

### 3.2 `AgentTokenAuditLog` 新实体

**文件**: `Sb.Apps.Pm.Models/Agent/AgentTokenAuditLog.cs` (新建)

```csharp
[Table(Name = "sb_pm_agent_token_audit")]
[Index("idx_audit_token_id", nameof(TokenId), false)]
[Index("idx_audit_time", nameof(CreatedTime), false)]
[Index("idx_audit_action", nameof(Action), false)]
public class AgentTokenAuditLog : IEntity
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(Name = "TokenId")]
    public int TokenId { get; set; }

    /// <summary>Token 名称 (冗余，防止 Token 删除后审计丢失上下文)</summary>
    [Column(Name = "TokenName", StringLength = 100)]
    public string TokenName { get; set; } = "";

    /// <summary>Token 前缀 (冗余)</summary>
    [Column(Name = "TokenPrefix", StringLength = 16)]
    public string TokenPrefix { get; set; } = "";

    /// <summary>动作: Created/Used/Revoked/Updated/Expired/RateLimited</summary>
    [Column(Name = "Action", StringLength = 20)]
    public string Action { get; set; } = "";

    /// <summary>结果: Success/Denied</summary>
    [Column(Name = "Result", StringLength = 10)]
    public string Result { get; set; } = "";

    /// <summary>来源 IP</summary>
    [Column(Name = "IpAddress", StringLength = 45)]
    public string? IpAddress { get; set; }

    /// <summary>请求路径</summary>
    [Column(Name = "RequestPath", StringLength = 500)]
    public string? RequestPath { get; set; }

    /// <summary>拒绝原因 (仅 Denied 时有值)</summary>
    [Column(Name = "DenyReason", StringLength = 200)]
    public string? DenyReason { get; set; }

    /// <summary>操作者 (管理操作时有值)</summary>
    [Column(Name = "Operator", StringLength = 50)]
    public string? Operator { get; set; }

    // [2026-06-02 修订] 不用 InsertValueSql="now()"（PostgreSQL 方言，sqlite 等会插入失败）；
    // 由 AgentTokenAuditService.WriteAsync 写入时兜底赋值 DateTime.Now
    [Column(Name = "CreatedTime", DbType = "timestamp")]
    public DateTime CreatedTime { get; set; }
}
```

**设计决策**：
- 审计日志**不做软删除**（审计数据需永久保留）
- Token 名称和前缀冗余存储，防止 Token 删除后审计记录失去上下文
- 按 90 天自动清理（后台任务）

---

## 4. Scope 权限设计

> **[2026-05-31 修订 · Claude]** v2.0 原方案混合了"资源型"(requirement/task/project)和"动作型"(read/write/all)Scope,**会产生语义歧义**:
> - `requirement` 是否含写?(语义重叠 `write`)
> - "只读所有 + 只写需求" 怎么表达?(原方案没有合适组合)
>
> 改为 **RBAC 风格** `action:resource` 双段命名,语义清晰、组合无歧义。

### 4.1 Scope 定义（修订后）

| Scope | 含义 | 可访问端点 |
|-------|------|-----------|
| `read:*` | 所有读权限 | 所有 GET 端点 |
| `read:requirement` | 只读需求 | `GET /agent/requirement/*` |
| `read:task` | 只读任务 | `GET /agent/task/*` |
| `read:project` | 只读项目 | `GET /agent/project/*` |
| `read:progress` | 只读进度 | `GET /agent/progress/*` |
| `write:requirement` | 写需求 | `POST /agent/requirement/*` |
| `write:task` | 写任务 | `POST /agent/task/*` |
| `*` | 全权 | 所有端点 |

> **[2026-05-31 修订 · Claude]** `*` 替换 v2.0 的 `all`,与配置/glob 风格一致(更符合开发者直觉)。
> **向后兼容**:`AgentToken.Scopes == null` 仍表示全权(等同 `*`),沿用一期默认行为。
> **典型场景示例**:
> - Hermes 只读监控:`read:*`
> - Hermes 创建需求 + 读所有:`read:*,write:requirement`
> - CI 只汇报任务进度:`write:task`

### 4.2 Scope 检查实现方案

在 Agent Controller 中新增 `[AgentScope]` 自定义特性：

```csharp
public class AgentScopeAttribute : Attribute
{
    public string RequiredScope { get; }
    public AgentScopeAttribute(string scope) => RequiredScope = scope;
}
```

配合 ActionFilter 在请求执行前检查（**修订后逻辑**）：
1. `AgentToken.Scopes == null` → 全权通过（向后兼容）
2. Token 持有 `*` → 通过
3. Token 持有与 `RequiredScope` 完全相同的 scope → 通过
4. Token 持有同动作的通配 scope(如 `RequiredScope=read:task`,持有 `read:*`)→ 通过
5. 否则 403 + 审计 `Action=Used, Result=Denied, DenyReason=ScopeMismatch`

> **[2026-05-31 修订 · Claude]** 通配匹配逻辑由 `IAgentContext.HasScope()` 集中实现(扩展现有方法),避免 ActionFilter 重复实现。

### 4.3 Controller Scope 映射（修订后）

| Controller | 端点类型 | RequiredScope |
|-----------|---------|---------------|
| AgentRequirementController | GET | `read:requirement` |
| AgentRequirementController | POST | `write:requirement` |
| AgentTaskController | GET | `read:task` |
| AgentTaskController | POST | `write:task` |
| AgentProjectController | GET | `read:project` |
| AgentProgressController | GET | `read:progress` |

> **[2026-05-31 修订 · Claude]** 每个端点只声明**最小所需 scope**,由 §4.2 的通配匹配规则负责"`read:*` 也能通过 `read:task`"。这样 Controller 代码不会出现 "或" 的列举,清晰。

---

## 5. Admin HTTP API 设计

### 5.1 端点列表

**路由**: `/admin/AgentToken/[action]`

| 方法 | 端点 | 说明 | 认证 |
|------|------|------|------|
| POST | `/admin/AgentToken/Create` | 创建 Token | JwtAdmin |
| GET | `/admin/AgentToken/List` | 列表 (分页+搜索) | JwtAdmin |
| GET | `/admin/AgentToken/Detail/{id}` | 详情 | JwtAdmin |
| POST | `/admin/AgentToken/Update` | 更新名称/Scope/备注/过期 | JwtAdmin |
| POST | `/admin/AgentToken/Revoke` | 吊销 (软删除+原因) | JwtAdmin |
| GET | `/admin/AgentToken/Usage` | 使用统计 | JwtAdmin |
| GET | `/admin/AgentToken/Audit` | 审计日志查询 | JwtAdmin |

### 5.2 输入输出格式

遵循现有 PM 项目约定：
- 输入: `application/x-www-form-urlencoded` (`[FromForm]`)
- 输出: 框架标准 `Result` 对象
- 分页: `Result.Page(data, total)`

### 5.3 Create 响应特殊处理

创建 Token 时，明文仅在首次响应中返回：

```json
{
  "code": 0,
  "message": "ok",
  "data": {
    "id": 5,
    "name": "hermes-agent",
    "token": "sk_pm_xxxxxxxxxxxxxxxxxxxxxx",
    "scopes": "read:*,write:requirement,write:task",
    "expiresAt": "2027-05-31T00:00:00",
    "rateLimitPerMinute": 60,
    "notes": "Hermes Agent 专用"
  }
}
```

⚠️ 后续 Detail/List 不再返回完整 token,仅返回 `TokenPrefix`。

> **[2026-05-31 修订 · Claude]** 示例 scopes 改为 RBAC 风格(`read:*,write:requirement,write:task`);新增 `rateLimitPerMinute` 字段;与 §3.1 字段表保持一致。

---

## 6. 架构变更

### 6.1 审计写入流程

```
AgentBearerAuthenticationHandler.HandleAuthenticateAsync()
  │
  ├─ 验证成功:
  │     ├─ 节流更新 LastUsedAt (每分钟最多一次, 内存缓存判断)
  │     ├─ 异步写入审计日志 (Action=Used, Result=Success, IP/Path 都在审计记录里)
  │     └─ 检查 RateLimitPerMinute (滑动窗口, 见下文)
  │
  └─ 验证失败:
        ├─ 异步写入审计日志 (Action=Used, Result=Denied, DenyReason=...)
        └─ 返回 401
```

**审计写入策略（修订后）**：
- 使用 `Task.Run(() => _auditService.WriteAsync(entry))` fire-and-forget
- 异常时降级为 `ILogger.LogWarning`，不阻塞认证
- 与现有 `AgentTokenService.TouchLastUsed` 的实现风格一致(参考 `AgentTokenService.cs:100-112`)

> **[2026-05-31 修订 · Claude]** v2.0 原方案是 `Channel<AuditEntry>` + 批量 50 条/3 秒 + 独立 HostedService 消费者。**对当前体量过度工程**:
> - PM 项目内部使用,Agent QPS 预估 < 1(Hermes 一个 Token + 偶发 CI 调用)
> - `Task.Run` + 单条 Insert 在 QPS < 50 时性能足够,复杂度低 5 倍
> - 框架已有 `CliCleanupHostedService` 模式可参考,但**不在 v2.1 引入**;等监控数据显示 QPS > 50 时再升级
>
> **速率限制实现**:用 `IMemoryCache` 按 TokenId 维护 1 分钟过期的计数器,达到 `RateLimitPerMinute` 返回 429 并写审计 `Action=RateLimited`。

### 6.2 审计日志清理

- 保留期：默认 **90 天**,通过 `appsettings.json` 配置 `Agent:AuditRetentionDays` 覆盖
- 通过 `IHostedService` 每天 02:00 执行清理
- 清理时记录日志：`Deleted {count} audit entries older than {days} days`

> **[2026-05-31 修订 · Claude]** v2.0 硬编码 90 天 → 改为可配置。读取代码示例:
> ```csharp
> var days = config.GetValue<int?>("Agent:AuditRetentionDays") ?? 90;
> ```
> 这样运维可按合规要求(如某些场景需保留 180 天/1 年)调整,不需要改代码。

---

## 7. 实施步骤

### Phase 1: 数据层增强 (~40 min)

| # | 文件 | 操作 | 说明 | 状态 |
|---|------|------|------|------|
| 1.1 | `AgentToken.cs` | 修改 | 新增 CreatedBy/ModifiedBy/RevokeReason/RateLimitPerMinute/Notes/UpdatedTime(LastUsedIp/LastUsedPath/RequestCount/RevokedBy 改入审计表,见 §3 修订) | ✅ **2026-05-31 完成 (Claude)** |
| 1.2 | `AgentTokenAuditLog.cs` | 新建 | 审计日志实体 (`int Id`,IEntity 兼容);AgentTokenService.GetEntityTypes 已注册 | ✅ **2026-05-31 完成 (Claude)** |
| ~~1.3~~ | ~~`PmFreeSqlConfigurator.cs`~~ | ~~修改~~ | ~~新增 AgentToken 软删除过滤器~~ | ⊘ 已存在,无需修改 |

> **[2026-05-31 Phase 1 完成 · Claude]** 数据层增强代码完成,`dotnet build` 0 Error。注意:`AgentTokenAuditLog.Id` 必须是 `int`(`IEntity.Id` 约束),不是 v2.1 原稿的 `long`。下次 PM 启动时,framework `DatabaseSeeder` 会自动建 `sb_pm_agent_token_audit` 表 + 新字段 ALTER。

> **[2026-05-31 修订 · Claude]** **Phase 1.3 已删除** — 代码核实发现 `PmFreeSqlConfigurator.cs:57` 已注册 AgentToken 软删除过滤器,无需新增。若开发者按原计划重复添加会触发 FreeSql 重复过滤器警告。
> 字段清单也同步精简:`LastUsedIp` / `LastUsedPath` / `RequestCount` / `RevokedBy` 从主表移入 `AgentTokenAuditLog`,详见 §3.1 修订说明;`MaxRequests` 重命名为 `RateLimitPerMinute`,详见 §3.1 修订说明。

### Phase 2: 服务层增强 (~90 min)

| # | 文件 | 操作 | 说明 | 状态 |
|---|------|------|------|------|
| 2.1 | `AgentTokenService.cs` | 修改 | Create 加 createdBy/rateLimitPerMinute/notes(默认参数,旧 3 参调用仍可用);Update 方法支持 unsetExpires/unsetRateLimit;Revoke 含 revoker(入审计,主表也写到 ModifiedBy)+ reason(入主表);TouchLastUsed 用进程内 `ConcurrentDictionary<int, DateTime>` 节流,每 TokenId 最多每分钟一次主表 UPDATE | ✅ **2026-05-31 完成 (Claude)** |
| 2.2 | `AgentTokenAuditService.cs` | 新建 | Task.Run fire-and-forget 单条 Insert + LogWarning 兜底 | ✅ **2026-05-31 完成 (Claude)** |
| 2.3 | `AgentRateLimiter.cs` | 新建 | `IMemoryCache` 简化滑动窗口,`TryAcquire(tokenId, limit?)` 返回 `(allowed, current, limit)` | ✅ **2026-05-31 完成 (Claude)** |
| 2.4 | `AgentTokenAuditCleanupService.cs` | 新建 | BackgroundService 每 6 小时跑一次清理 `Agent:AuditRetentionDays`(默认 90 天),启动延迟 60s | ✅ **2026-05-31 完成 (Claude)** |
| 2.5 | `AgentBearerAuthenticationHandler.cs` | 修改 | 注入 AuditService,鉴权失败写匿名 `Used Denied`(用 token 前 12 位定位),鉴权成功写 `Used Success`(IP/Path 入审计) | ✅ **2026-05-31 完成 (Claude)** |
| 2.6 | `AgentContext.cs` + `IAgentContext.cs` | 修改 | `HasScope` 改 RBAC 通配匹配:`*` 全权 / `read:*` 通配 / 精确匹配。IAgentContext 暴露 `Token` 实体便于 Filter 读 RateLimitPerMinute | ✅ **2026-05-31 完成 (Claude)** |
| 2.7 | `AgentAuthExtensions.cs` | 修改 | 注册 AgentRateLimiter / AgentTokenAuditService / AddMemoryCache / AddHostedService cleanup | ✅ **2026-05-31 完成 (Claude)** |

> **[2026-05-31 Phase 2 完成 · Claude]** 服务层全部就绪,`dotnet build` 0 Error。RateLimit 强制检查在 Phase 4 ActionFilter 中接入(Handler 阶段只做认证 + 审计写入)。`Used Denied` 因匿名场景无法确定 TokenId,记 `TokenId=0` + 前缀;管理员从审计可看到 "有未知 token 被尝试"。

> **[2026-05-31 修订 · Claude]** v2.0 把审计描述为 "Channel 异步批量写入" + HostedService 消费者(2 个服务)。v2.1 简化为 `Task.Run` fire-and-forget(1 个服务方法),时间从 60min → 90min 是因为新增了 `AgentRateLimiter` 实现。

### Phase 3: Admin API (~40 min)

| # | 文件 | 操作 | 说明 | 状态 |
|---|------|------|------|------|
| 3.1 | `Controllers/Agent/AgentTokenController.cs` | 新建 | Admin 7 端点:Create / List / Detail / Update / Revoke / Usage / Audit。Create 响应一次性 token 明文。Update 支持 unsetExpiresAt/unsetRateLimit 显式清空字段。所有写操作 fire-and-forget 写审计 | ✅ **2026-05-31 完成 (Claude)** |
| 3.2 | `AgentTokenService.cs` | 修改 | 补 `GetMenus()` 返回 `{"/system/agent-token", "Agent Token", "lucide:key-round"}`,在同一 IModuleInitializer 类内覆写,避免新增独立 Initializer | ✅ **2026-05-31 完成 (Claude)** |

> **[2026-05-31 Phase 3 完成 · Claude]** Admin Controller 路由 `/admin/AgentToken/[action]`(framework AdminRoutePrefixConvention 已自动加 admin 前缀,这里显式写为防混淆)。`dotnet build` 0 Error。Admin 前端页面 `/agent-token/index` 需要在 sb-apps-pm-admin 项目里新建 Vue 组件(本计划不含,留作后续 admin 侧任务)。

> **[2026-05-31 修订 · Claude]** Phase 3.2 措辞明确化:`AgentTokenService` 已实现 `IModuleInitializer` 接口(见 `AgentTokenService.cs:14`),但只覆盖了 `GetEntityTypes()`。补菜单的位置是在**同一个类**里追加 `GetMenus()` 覆写,**不是**新建一个 `AgentModuleInitializer`。这能减少 framework 自动发现的初始化器数量,符合现有结构。

### Phase 4: Scope 权限接入 (~40 min)

| # | 文件 | 操作 | 说明 | 状态 |
|---|------|------|------|------|
| 4.1 | `AgentScopeAttribute.cs` | 新建 | `AttributeTargets.Class \| Method` (Action 级覆盖 Controller 级) | ✅ **2026-05-31 完成 (Claude)** |
| 4.2 | `AgentScopeFilter.cs` | 新建 | IAsyncActionFilter:无 required 跳过 → RateLimit (429+Retry-After) → Scope 通配匹配 (403+审计 Denied ScopeMismatch) | ✅ **2026-05-31 完成 (Claude)** |
| 4.3 | 4 个 Agent Controller | 修改 | 策略:Controller 标默认 scope,GET 端点 Action 级覆盖。AgentProject/Progress 全 GET → `[AgentScope("read:project/progress")]`。AgentRequirement/Task → Controller 标 write,GET 单独标 read | ✅ **2026-05-31 完成 (Claude)** |
| 4.4 | `AgentAuthExtensions.cs` | 修改 | 全局 MvcOptions.Filters.AddService<AgentScopeFilter> — 只对带 [AgentScope] 端点生效,其余 fast path 跳过 | ✅ **2026-05-31 完成 (Claude)** |

> **[2026-05-31 Phase 4 完成 · Claude]** Scope 强制开始生效。当前 hermes-agent (id=2) 持有 `read:*,write:requirement,write:task` — 可访问所有 GET 端点(通配 `read:*`)+ 写需求/任务,**不能写项目**(没标 write:project,但 AgentProjectController 全 GET 也不需要)。`dotnet build` 0 Error。
>
> **向后兼容**:`AgentToken.Scopes` 为 null 的老 Token 全权通过(AgentContext.HasScope 返回 true);用 RBAC 风格新创建的 Token 才会被强制检查。`TaskAgentController`(X-Api-Key 体系)未受影响,符合 §1.2 修订决策。

### Phase 5: CLI 增强 (~20 min)

| # | 文件 | 操作 | 说明 | 状态 |
|---|------|------|------|------|
| 5.1 | `AgentTokenCli.cs` | 修改 | create 增 `--rate-limit-per-minute` / `--notes`,自动传 `createdBy="cli"`;revoke 增 `--reason`,自动传 `revoker="cli"` | ✅ **2026-05-31 完成 (Claude)** |

> **[2026-05-31 Phase 5 完成 · Claude]** CLI 现已与 Admin API 字段对齐。下次创建 Token 示例:
> ```bash
> agent-token create --name "hermes-agent" \
>     --scopes "read:*,write:requirement,write:task" \
>     --expires "2027-05-31" \
>     --rate-limit-per-minute 120 \
>     --notes "Hermes Agent 专用"
> ```

> **[2026-05-31 修订 · Claude]** `--max-requests` 改为 `--rate-limit-per-minute`(与 §3.1 字段名一致)。

### Phase 6: 测试 (~90 min)

| # | 文件 | 操作 | 说明 | 状态 |
|---|------|------|------|------|
| 6.1 | `AgentTokenServiceTests.cs` | 修改 | 新增 8 用例:Create v2.1 字段、Update 部分字段/UnsetExpiresAt/UnsetRateLimit/NoFields=false、Revoke 含 reason/operator、TouchLastUsed 节流 | ✅ **2026-05-31 完成 (Claude)** |
| 6.2 | `AgentBearerAuthenticationHandlerTests.cs` | 修改 | 构造函数兼容新 AuditService 参数;旧测试全部保持通过 | ✅ **2026-05-31 完成 (Claude)** |
| 6.3 | `AgentContextScopeTests.cs` | 新建 | 9 用例覆盖 RBAC 通配:null/empty/* / 精确(大小写)/ wildcardAction / 跨 action 拒绝 / hermes 场景 | ✅ **2026-05-31 完成 (Claude)** |
| 6.4 | `AgentRateLimiterTests.cs` | 新建 | 4 用例:null limit / zero limit / within-then-deny / 多 Token 独立 | ✅ **2026-05-31 完成 (Claude)** |
| 6.5 | `AgentScopeFilterTests.cs` | 新建 | 9 用例:JwtAdmin 放行 / 无 attr 放行 / 无 AgentContext 放行 / scope 匹配放行 / 通配匹配 / 缺失 403 终止管道 / 多 scope 部分缺失 403 / 多 scope 全有放行 / null scopes 全权。用真实 DefaultHttpContext + ActionDescriptor 构造，无 mock 框架 | ✅ **2026-06-02 完成 (Claude)** |
| 6.6 | `AgentTokenControllerTests.cs` + `AgentTokenAuditServiceTests.cs` | 新建 | Controller 12 用例(new Controller + sqlite 内存库)：Create/Detail/Update/Revoke/Usage/Audit/List 端到端 + 持久化/软删验证；AuditService 5 用例：Query/CountByToken 过滤与分页 | ✅ **2026-06-02 完成 (Claude)** |

> **[2026-05-31 Phase 6 · Claude]** 单元测试 **34/34 Passed**(原 13 + 新增 21)。6.5/6.6 集成测试当时跳过,留待接入后做。
>
> **[2026-06-02 Phase 6.5/6.6 补齐 · Claude]** 原跳过的 Filter / Controller 集成测试已补齐,新增 **26 用例**(Filter 9 + Controller 12 + AuditService 5),全量测试 **54 → 80 全通过**。补测过程中发现并修复了审计表的数据库方言隐患,详见下方修订。
>
> **[2026-06-02 审计表方言修复 · Claude]** `AgentTokenAuditLog.CreatedTime` 原 `InsertValueSql="now()"` 是 PostgreSQL 方言,sqlite/其它库无此函数会令审计写入失败;而生产 fire-and-forget 写入失败被 `LogWarning` 静默吞掉。已去掉 `InsertValueSql`,改由 `AgentTokenAuditService.WriteAsync` 写入时兜底赋值 `DateTime.Now`,兼容所有方言。§3.2 实体定义示例同步更新。

> **[2026-05-31 修订 · Claude]** v2.0 测试覆盖描述过粗,本次补具体用例:Scope 通配、RateLimit 边界、节流验证、审计降级。

---

## 8. 验收标准

- [ ] Admin 后台可通过 HTTP API 创建/编辑/吊销 Agent Token
- [ ] 创建 Token 时可设置 Scope、过期时间、速率限制(`RateLimitPerMinute`)、备注
- [ ] Scope 权限在 Agent Controller 级别生效,`read:task` 只能 GET 任务,`write:*` 通配匹配所有写入
- [ ] 每次 Agent API 调用按节流(每分钟一次)更新 `LastUsedAt`;IP/Path 完整记录在 `AgentTokenAuditLog`
- [ ] 审计日志可查询(按 Token/时间/动作/结果过滤)
- [ ] 审计日志按 `Agent:AuditRetentionDays` 配置(默认 90 天)自动清理
- [ ] 达到 `RateLimitPerMinute` 后返回 429 + 审计 `RateLimited`,1 分钟后自动恢复
- [ ] 吊销 Token 在审计表记录 `Operator` + `RevokeReason`(主表只保留 RevokeReason)
- [ ] 已创建的 Token(无新字段)向后兼容正常工作;`Scopes=null` 等同 `*`
- [ ] CLI 命令增强:支持 `--scopes` / `--rate-limit-per-minute` / `--notes` / `--reason`
- [ ] **本计划不动 `TaskAgentController`(X-Api-Key 体系),作为后续独立任务**

> **[2026-05-31 修订 · Claude]** 验收清单同步更新到新字段名、新机制(速率限制 vs 累计上限、节流主表更新等)。最后一条显式列出 TaskAgentController 不在范围内,避免实施后被指责"遗漏"。

---

## 9. 时间估算

| Phase | 内容 | v2.0 估算 | v2.1 修订估算 |
|-------|------|----------|--------------|
| Phase 1 | 数据层增强 | ~40 min | ~40 min |
| Phase 2 | 服务层增强(含速率限制、节流写入) | ~60 min | ~90 min |
| Phase 3 | Admin API | ~40 min | ~60 min |
| Phase 4 | Scope 权限接入(4 Controller + ActionFilter + 通配匹配) | ~40 min | ~90 min |
| Phase 5 | CLI 增强 | ~15 min | ~20 min |
| Phase 6 | 测试(含 Scope 拒绝/RateLimit/审计失败降级) | ~20 min | ~90 min |
| **合计** | | **~5 hours** | **~7-8 hours** |

> **[2026-05-31 修订 · Claude]** v2.0 估算偏乐观,主要低估了:
> - Phase 4 加 attribute 容易,但**调试 ActionFilter + 通配匹配的边界场景**需要时间(scope 为空、scope 只有 `*`、scope 混合写法等)
> - Phase 6 测试只列了"覆盖新字段",实际还需要覆盖:
>   - Scope 通配匹配(`read:*` 通过 `read:task`、`read:requirement` 不通过 `read:task`)
>   - RateLimit 触发后的 429 响应和审计记录
>   - 审计写入异常时的 ILogger 降级、认证不受影响
>   - `LastUsedAt` 节流(同一秒连续 100 次请求,主表只 UPDATE 一次)
> - 整体加 2-3 小时缓冲,7-8 小时更现实

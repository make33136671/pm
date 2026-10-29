---
title: "PM MCP Server 开发计划与进度"
version: "1.1.0"
date: "2026-06-11"
status: "completed"
author: "Claude (Opus 4.8)"
design_doc: "AI_PM_MCP_Server_Design.md (v1.3.0)"
audit_baseline: "commit c5e573c — 设计文档断言已逐项核验通过"
---

# PM MCP Server 开发计划与进度

> 本文件是 [`AI_PM_MCP_Server_Design.md`](./AI_PM_MCP_Server_Design.md) 的落地执行计划。
> 设计文档负责"建什么"，本文件负责"怎么建、建到哪了"。
> **进度更新约定**：每完成一个任务，更新对应行的状态列（⬜ 未开始 / 🔄 进行中 / ✅ 完成 / ⏸️ 阻塞）与备注。

---

## 0. 决策结论（已定稿）

| 维度 | 结论 | 依据 |
|------|------|------|
| **开发范围** | **全量**：Phase 1 + 2 + 3 全做 | 用户确认 2026-06-11 |
| **Token** | **FullAccess token 优先**（god key 旁路 scope，覆盖全端点） | 设计文档 §2.3；已有 `sk_48c02cac2...` 可调通 |
| **Transport** | **HTTP/SSE**（主），stdio（备选） | 设计文档 §2.1 v1.3 定稿 |
| **语言/SDK** | **Python** + 官方 `mcp` SDK + `httpx` | 设计文档 §2.1 |
| **代码位置** | **PM api 仓库内** `pm-mcp-server/` | 设计文档 §2.1；与后端同仓同步 |
| **后端改动** | **不需要**（MCP 仅代理现有 HTTP API） | 设计文档 §2.1；验收流程(需求#32)等后端就绪后再追加 |

### 0.1 审计补充项（开发时需吸收，已记录在审计报告）

- **AC-1** AiController 存在 3 个设计文档工具表未列入的 `/admin/Ai/*` 端点：
  `ExtractFeatureRequirements` / `AnalyzeTaskProgress` / `AnalyzeRequirementCoverage`。
  → **确定纳入工具表**（用户决策：宁可做了用不到，也不漏；见 T2.7）。
- **AC-2** `pm_project_create` **非整实体绑定**：`AgentProjectController.Create` 仅接受
  `name / code / description / status` 四个具名 `[FromForm]` 字段。inputSchema 只定义这 4 个。
- **AC-3** 设计文档附录 B-1（transport）已定稿为 HTTP/SSE，可视为已解决。

---

## 1. 里程碑总览

| 里程碑 | 内容 | 工具数 | 依赖 | 状态 |
|--------|------|-------|------|------|
| **M0 脚手架** | 项目骨架、HTTP 层、鉴权、错误映射、tools/list 框架 | — | — | ✅ |
| **M1 受限闭环** | Phase 1：`/agent/*` 读写工具 | ~20 | M0 | ✅ |
| **M2 FullAccess** | Phase 2：`/admin/*` 任务创建/功能模块/AI | ~17 | M0 + FullAccess token | ✅ |
| **M3 CI&文档** | Phase 3：CI/CD、文档、概览、记忆 | ~17 | M0 | ✅ |
| **M4 验证交付** | 端到端验证、文档、部署 | — | M1+M2+M3 | ✅ |

> 工具数为估算，最终以 `tools/list` 实际注册数为准。设计文档共列约 54 个工具。

---

## 2. M0 — 脚手架（前置基础，全部工具的地基）

### 2.0 框架前置（FullAccess 全局启用，必须先于一切）

| ID | 任务 | 产出 | 状态 | 备注 |
|----|------|------|------|------|
| T0.0a | 改框架 `AccessTokenOptions.EnableFullAccess` 默认 `false→true` | 源码 | ✅ | 用户核心设计，已改 |
| T0.0b | 框架 `SB.Feature.AccessToken` 版本 `0.1.4→0.1.5` | csproj | ✅ | 已改 |
| T0.0c | 发包到 baget（参考 `reference_baget_nuget_publish`） | NuGet 包 0.1.5 | ✅ | 0.1.5→0.2.0 多次迭代发包，修复 FullAccess InsertValueSql bug |
| T0.0d | PM `Sb.Apps.Pm.WebApi.csproj` 升级引用 `0.1.4→0.2.0` | csproj | ✅ | 当前引用 `SB.Feature.AccessToken 0.2.0` |
| T0.0e | 验证：super 账号可签发 FullAccess token，god key 调通 `/admin/*` | 实测 | ✅ | token id=28 `sk_fedf4faa...` 可用（DB 手动 UPDATE 修正 FullAccess=true） |

### 2.1 MCP 脚手架

| ID | 任务 | 产出 | 状态 | 备注 |
|----|------|------|------|------|
| T0.1 | 创建 `pm-mcp-server/` 目录与 Python 项目骨架（`pyproject.toml` / `pm_mcp_server/__init__.py`） | 可 `python -m pm_mcp_server` 启动 | ✅ | 依赖仅 `mcp` + `httpx` |
| T0.2 | 配置加载：`PM_MCP_BASE_URL` / `PM_MCP_API_KEY` 环境变量 | `config.py` | ✅ | 设计文档 §2.2 |
| T0.3 | `PMApiClient` HTTP 层：统一附加 `Authorization: Bearer`，按端点选 FromForm/JSON | `client.py` | ✅ | 设计文档 §2、§4 绑定速查 |
| T0.4 | 错误标准化 + hint 映射表（403/401/字段缺失 → 修复建议） | `errors.py` | ✅ | 设计文档 §7 |
| T0.5 | MCP Server 入口：`tools/list` + `tools/call` 分发框架 | `server.py` | ✅ | HTTP/SSE transport，监听端口(默认 8765) |
| T0.6 | Tool 定义注册机制（声明式：name/description/inputSchema → handler） | `tools/registry.py` | ✅ | 设计文档 §4 模板 |
| T0.7 | 枚举常量集中定义（从设计文档 §5 / 可选从 Enums.cs 生成校验） | `enums.py` | ✅ | 设计文档 §9 末项建议 CI 校验 |

**M0 验收**：空 Server 能启动，`tools/list` 返回空集，HTTP 层能带 token 打通 `/agent/schema`(401→鉴权存在)。

---

## 3. M1 — Phase 1：`/agent/*` 受限 token 闭环（P0）

> 仅依赖受限 token 即可跑通，不阻塞于 FullAccess。

| ID | 工具 | 路由 | Token | 状态 | 备注 |
|----|------|------|-------|------|------|
| T1.1 | `pm_schema` | GET `/agent/schema` | read:project | ✅ | **最先实现**，零试错来源 |
| T1.2 | `pm_project_list` | GET `/agent/project/List` | read:project | ✅ | |
| T1.3 | `pm_project_detail` | GET `/agent/project/Detail` | read:project | ✅ | |
| T1.4 | `pm_project_create` | POST `/agent/project/Create` | read:project⚠️ | ✅ | **AC-2**：仅 4 字段 name/code/description/status |
| T1.5 | `pm_project_sub_list` | GET `/agent/project/SubList` | read:project | ✅ | |
| T1.6 | `pm_project_health` | GET `/agent/progress/HealthScore` | read:progress | ✅ | 参数 projectId |
| T1.7 | `pm_requirement_list` | GET `/agent/requirement/List` | read:requirement | ✅ | |
| T1.8 | `pm_requirement_detail` | GET `/agent/requirement/Detail` | read:requirement | ✅ | |
| T1.9 | `pm_requirement_create` | POST `/agent/requirement/Create` | write:requirement | ✅ | 整实体 FromForm；必填 ProjectId+Title；source 无效值自动回退 Hermes |
| T1.10 | `pm_requirement_update` | POST `/agent/requirement/Update` | write:requirement | ✅ | |
| T1.11 | `pm_requirement_delete` | POST `/agent/requirement/Delete` | write:requirement | ✅ | |
| T1.12 | `pm_requirement_update_status` | POST `/agent/requirement/UpdateStatus` | write:requirement | ✅ | enum RequirementStatus |
| T1.13 | `pm_requirement_add_comment` | POST `/agent/requirement/AddComment` | write:requirement | ✅ | |
| T1.14 | `pm_requirement_split_tasks` | POST `/agent/requirement/AiSplitTasks` | write:requirement | ✅ | 返回 batchId；确认在 M2(跨 admin) |
| T1.15 | `pm_task_list` | GET `/agent/task/List` | read:task | ✅ | |
| T1.16 | `pm_task_detail` | GET `/agent/task/Detail` | read:task | ✅ | |
| T1.17 | `pm_task_available` | GET `/agent/task/Available` | read:task | ✅ | |
| T1.18 | `pm_task_logs` | GET `/agent/task/Logs` | read:task | ✅ | |
| T1.19 | `pm_task_claim/start/complete/fail/cancel/resume` | POST `/agent/task/*` | write:task | ✅ | 6 个写动作；状态机见设计 §3.3 |
| T1.20 | `pm_build_*`(ci/Trigger/Status/Jobs/JobTrace/TestReport/Link) | `/agent/ci/*` | read/write:ci | ✅ | Trigger 参数 profileId+autoApprove |
| T1.21 | `pm_build_list/release_list/changelog_list` | `/agent/progress/*` | read:progress | ✅ | |

**M1 验收**（设计文档 §9）：受限 token 跑通 `pm_schema → 建需求 → split_tasks(返回 batchId)`；
任务流 `claim → start → complete` 全绿。

---

## 4. M2 — Phase 2：`/admin/*` FullAccess 工具（P0/P1）

> **前置：确认 FullAccess token 可用**（设计文档已确认 `sk_48c02cac2...` 可调通）。
> 受限 token 调这些一律 403，hint 指向"需 FullAccess token"。

| ID | 工具 | 路由 | 状态 | 备注 |
|----|------|------|------|------|
| T2.1 | `pm_requirement_confirm_tasks` | POST `/admin/Requirement/AiConfirmTasks` | ✅ | 配套 T1.14 拆解落库；参数 batchId |
| T2.2 | `pm_task_create` | POST `/admin/TaskItem/Add` | ✅ | **四必填** Title+Content+RequirementId+FeatureModuleId(AC) |
| T2.3 | `pm_task_exec_by_ai` | POST `/admin/TaskItem/ExecByAi` | ✅ | |
| T2.4 | `pm_feature_module_tree/list/add/delete` | `/admin/FeatureModule/*` | ✅ | 建任务前置：拿 FeatureModuleId |
| T2.5 | `pm_ai_chat/execute` | POST `/admin/Ai/Chat`、`/admin/Ai/Execute` | ✅ | FromBody AiChatInput；Execute 按 TaskType 小写分发 |
| T2.6 | `pm_ai_analyze_code/generate_code/design_feature/review_code` | `/admin/Ai/*` | ✅ | 同 AiChatInput |
| T2.7 | `pm_ai_extract_requirements/analyze_task_progress/analyze_requirement_coverage` | `/admin/Ai/*` | ✅ | **AC-1 确定纳入**(宁可做了用不到)；同 AiChatInput FromBody |
| T2.8 | `pm_ai_memory_search` | GET `/admin/AiMemory/Search` | ✅ | |

**M2 验收**：FullAccess token 跑通 `建项目 → 建功能模块 → 建任务(四必填) → claim/start/complete`；
受限 token 调 `/admin/*` 返回 403 且 hint 正确。

---

## 5. M3 — Phase 3：CI/CD & 文档 & 概览（P2）

| ID | 工具 | 路由 | 状态 | 备注 |
|----|------|------|------|------|
| T3.1 | `pm_project_overview/tree` | `/admin/Project/*` | ✅ | 🔑 Full |
| T3.2 | `pm_dashboard_workbench` | `/admin/Dashboard/Workbench` | ✅ | 🔑 Full |
| T3.3 | `pm_document_list/add` | `/admin/ProjectDocument/*` | ✅ | 🔑 Full |
| T3.4 | `pm_notebook_list/add` | `/admin/Notebook/*` | ✅ | 🔑 Full |

> CI 读写工具已并入 M1(T1.20/T1.21，因走 `/agent/*` 受限 token 可达)。M3 只剩 `/admin/*` 概览类。

**M3 验收**：概览/文档/记忆工具 `tools/call` 返回结构化数据，无 schema 偏差。

---

## 6. M4 — 验证与交付

| ID | 任务 | 状态 | 备注 |
|----|------|------|------|
| T4.1 | `tools/list` 全量工具定义检查（类型/必填/枚举/Token 要求齐全） | ✅ | 设计文档 §9 ☑1；57 工具全量注册已验 |
| T4.2 | 枚举一致性校验：每个 enum 与 `Enums.cs` 逐字一致（可加 CI 步骤从源码生成校验） | ✅ | 设计文档 §9 ☑5；source 传 Claude 自动回退 Hermes |
| T4.3 | 受限 token 端到端跑通 | ✅ | §9 ☑2 |
| T4.4 | FullAccess token 端到端跑通 | ✅ | §9 ☑3；token id=28 `sk_fedf4faa...` |
| T4.5 | 403 hint 路径验证（受限调 admin） | ✅ | §9 ☑4 |
| T4.6 | 部署：常驻 PM 服务器/局域网机器，监听 8765，访问后端 | ✅ | 192.168.10.73:8765；supervisor pm-mcp 守护；宿主 .199 运行 |
| T4.7 | README + Hermes/Claude 接入说明（`hermes mcp add ... --url`） | ✅ | 设计文档 §2.1 |
| T4.8 | 同步设计文档：补 AC-1 三端点、AC-2 绑定说明、B-1 标记已解决 | ✅ | 审计补充项回写 |

---

## 7. 进度汇总

| 里程碑 | 任务总数 | 完成 | 进度 |
|--------|---------|------|------|
| M0 脚手架（含 5 框架前置） | 12 | 12 | ✅ 100% |
| M1 受限闭环 | 21 | 21 | ✅ 100% |
| M2 FullAccess | 8 | 8 | ✅ 100% |
| M3 CI&文档 | 4 | 4 | ✅ 100% |
| M4 验证交付 | 8 | 8 | ✅ 100% |
| **合计** | **53** | **53** | **✅ 100%** |

> 当前阶段：**M0-M4 全部完成**。57 个工具已注册，端到端验证通过，MCP Server 部署在 192.168.10.73:8765，supervisor 常驻守护。
> **已知遗留问题**：`SB.Feature.AccessToken` 的 `FullAccess` 列 FreeSql `InsertValueSql` bug — INSERT 时服务端默认值覆盖 C# 值；临时处置：新建 token 后手动 DB `UPDATE "SB_ApiKey" SET "FullAccess" = true WHERE "Id" = <id>`。根因排查留后续框架版本。

---

## 8. 技术实现细节（照此写代码，已对源码核实）

### 8.1 鉴权链路（已核实 `ApiKeyMiddleware.cs` + `ApiKeyAuthenticator.cs` + `ScopeFilter.cs`）

```
MCP Client ──Authorization: Bearer sk_xxx──▶ PMApiClient
                                              │  (统一附加 header)
                                              ▼
              PM 后端 ApiKeyMiddleware (UseAuthorization 之前注册)
                │  识别 sk_ → authenticator.Resolve(context, "JwtAdmin")
                │  把 context.User 设为 key 归属账号的 JwtAdmin 身份
                │  IApiKeyContext 写入 HttpContext.Items[ApiKeyContext.HttpItemKey]
                ▼
              ScopeFilter (全局 IAsyncActionFilter)
                · 端点有 [Scope]  → 受限 key 校验 scope；FullAccess 旁路；密码 JWT 反被拒
                · 端点无 [Scope]  → 受限 key 拒("该端点不对受限 API Key 开放")；FullAccess/密码放行
```

> **MCP Server 唯一要做的**：每个请求附 `Authorization: Bearer ${PM_MCP_API_KEY}`。
> 身份桥接、scope 判定全在后端完成，MCP 不需要懂 JWT。
> FullAccess token（`fullAccess=true`）旁路一切 scope —— 这是 §0 选定的主路线，
> 配合框架 `EnableFullAccess=true`（0.1.5 默认）即全端点可达。

### 8.2 参数绑定（每个工具 handler 据此组 HTTP 请求）

| 端点组 | Content-Type / 绑定 | PMApiClient 处理 |
|--------|---------------------|------------------|
| `/agent/*` GET | query string | `httpx.get(url, params={...})` |
| `/agent/*` POST | `application/x-www-form-urlencoded`（`[FromForm]`） | `httpx.post(url, data={...})` |
| `/admin/Ai/*` POST | `application/json`（`[FromBody] AiChatInput`） | `httpx.post(url, json={...})` |
| `/admin/*` 其它 POST | FromForm（多数 `AllowJsonBody`，亦收 JSON） | 默认 `data=`，整实体绑定 |

### 8.3 关键端点真实签名（已核实，避免试错）

| 工具 | 后端签名 | 实现要点 |
|------|---------|---------|
| `pm_project_create` | `Create([FromForm] string name, string? code, string? description, ProjectStatus? status)` | **AC-2**：只这 4 个字段，非整实体。inputSchema 必填 `name` |
| `pm_requirement_create` | `Create([FromForm] Requirement req)` | **整实体** FromForm；服务层必填 `ProjectId`+`Title` |
| `pm_requirement_update` | `Update([FromForm] Requirement req)` | 整实体 |
| `pm_task_claim` | `Claim([FromForm] int? projectId)` → `ClaimNext(KeyName, projectId)` | **不传 agentName**：用 token 的 `KeyName` 自动标识 agent |
| `pm_task_start` | `Start([FromForm] int id)` → `ReportStart(id, KeyName)` | 只传 id |
| `pm_task_complete` | `Complete([FromForm] int id, string? message)` | id 必填，message 可选 |
| `pm_task_fail` | `Fail([FromForm] int id, string message)` | **message 必填** |
| `pm_task_create` | `/admin/TaskItem/Add` | 四必填 Title+Content+RequirementId+FeatureModuleId |
| `pm_build_trigger` | `Trigger([FromForm] int profileId, string? branch, string? version, bool autoApprove=true)` | profileId 必填 |
| `pm_schema` | `Get() => Result.Success(schemaService.GetSchema())` | 无参；返回全模型快照 |

### 8.4 AiChatInput（统一 JSON body，已核实 `AiController.cs:194`）

```jsonc
{
  "ProjectId": 1,            // int?
  "SubProjectId": null,      // int?
  "RequirementId": null,     // int?
  "Instruction": "...",      // string（语义必填，默认 ""）
  "TaskType": "generate",    // string? 仅 Execute 用于分发：analyze/generate/design/review，其它→Chat
  "FocusPaths": ["a.cs"],    // List<string>?
  "ConversationHistory": [{"Role":"user","Content":"..."}]  // List<AiChatMessage>?，Role 默认 "user"
}
```

> `pm_ai_execute` 的 `TaskType` 是**小写自由字符串**，非 `AiTaskType` 枚举（区分 §5.5）。

### 8.5 Result 包络与错误映射（已核实 `Result.cs`）

后端统一返回 `{ Code:int, Message:string, Data:object }`，`Code != 0` 即失败（`Fail` 默认 `-1`）。

MCP `tools/call` 输出包络：
```jsonc
// 成功
{ "content": [{"type":"text","text":"<原始 Result JSON>"}], "isError": false }
// 失败（MCP Server 补 hint）
{ "content": [{"type":"text","text":"{...Result..., \"hint\":\"...\"}"}], "isError": true }
```

hint 映射表（`errors.py` 内置，源自设计文档 §7）：

| 后端 message / HTTP | hint |
|---------------------|------|
| HTTP 403 `该端点不对受限 API Key 开放` | `/admin/*` 端点，需 **FullAccess token** |
| HTTP 403 `缺少 scope 权限: x` | 换带 `x` scope 的 token |
| HTTP 401 | token 无效/过期/被吊销，重签 sk_ |
| `任务必须挂功能模块(FeatureModuleId)` | 先 `pm_feature_module_tree` 取 ID（FullAccess） |
| `任务必须关联需求(RequirementId)` | 先 `pm_requirement_list` 取 ID |
| `任务描述(Content)不能为空` | 补 Content |

### 8.6 目录结构（pm-mcp-server/）

```
pm-mcp-server/
├── pyproject.toml          # 依赖 mcp + httpx
├── README.md               # 接入说明（hermes mcp add --url）
├── pm_mcp_server/
│   ├── __init__.py
│   ├── __main__.py         # python -m pm_mcp_server 入口
│   ├── config.py           # PM_MCP_BASE_URL / PM_MCP_API_KEY
│   ├── client.py           # PMApiClient：HTTP 层 + 鉴权 + FromForm/JSON 选择
│   ├── errors.py           # Result 解析 + hint 映射
│   ├── enums.py            # §5 枚举常量（与 Enums.cs 逐字一致）
│   ├── server.py           # MCP Server：tools/list + tools/call，HTTP/SSE 监听
│   └── tools/
│       ├── registry.py     # 声明式注册：name/description/inputSchema → handler
│       ├── project.py      # T1.2-T1.6 + T3.1-T3.2
│       ├── requirement.py  # T1.7-T1.14 + T2.1
│       ├── task.py         # T1.15-T1.19 + T2.2-T2.3
│       ├── feature.py      # T2.4
│       ├── ci.py           # T1.20-T1.21
│       ├── ai.py           # T2.5-T2.7
│       ├── document.py     # T3.3-T3.4 + T2.8
│       └── schema.py       # T1.1
```

---

## 9. 风险与未决

| # | 项 | 影响 | 处置 |
|---|----|------|------|
| R-1 | ~~`EnableFullAccess` 默认 false~~ **已处置**：框架级全局启用 | — | ✅ 用户核心设计：改 `AccessTokenOptions.cs` 默认 `false→true`，框架 `SB.Feature.AccessToken` 升 `0.1.4→0.1.5`。**前置 T0.0：发包 + PM 升级引用版本**（见下） |
| R-2 | 验收流程(需求#32)后端未实现 | `SubmitForReview/Review*` 三工具暂不能做 | 设计文档已明确：后端就绪后追加，本轮不做 |
| R-3 | `AgentProjectController` 写操作挂只读 scope(A-7)、`/api/task/*` 平行鉴权(A-8) | 代码侧瑕疵，非 MCP 阻塞 | MCP 侧规避：建项目走 read:project 现状；task 一律走 `/agent/task/*` |
| R-4 | 枚举若后端后续变更 | inputSchema 漂移 | T4.2 加 CI 从 `Enums.cs` 生成校验 |
| R-5 | `SB.Feature.AccessToken` FreeSql `InsertValueSql="false"` bug：`FullAccess` 列 INSERT 始终写 false，C# 属性值被服务端默认值覆盖 | 新签 FullAccess token 实际 FullAccess=false，需手动 DB 修正 | 临时处置：`UPDATE "SB_ApiKey" SET "FullAccess" = true WHERE "Id" = <id>`；当前工作 token id=28；根因留框架后续版本排查（`CanUpdate`/`CanInsert` 隐式影响待核） |

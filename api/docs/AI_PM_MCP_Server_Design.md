---
title: "PM MCP Server 设计文档"
version: "1.3.0"
date: "2026-06-11"
status: "reviewed"
author: "Hermes Agent"
reviewer: "Claude (代码审计), Hermes (后续审计)"
---

# PM MCP Server 设计文档

> **版本**: v1.1.0
> **日期**: 2026-06-11
> **状态**: Reviewed — 已对照 `apps/pm/api` 源码逐项核对
> **前置依赖**: `/agent/schema` 接口（已实现）、`SB.Feature.AccessToken`（已引用）

---

## 审计修订说明（v1.0 → v1.2）

v1.0 由 Hermes 基于推测编写，多处与真实代码不符。本版对照
`Sb.Apps.Pm.WebApi/Controllers/*` 与 `Sb.Apps.Pm.Models/Enums.cs` 全量核对，
修正如下（详细证据见 **附录 A：代码审计发现**）：

| # | 类别 | v1.0 错误 | v1.1 修正 |
|---|------|-----------|-----------|
| 1 | **鉴权模型** | 完全缺失 | 新增 §2.3。`/admin/*` 端点未标 `[Scope]`，**受限 token 一律 403**，只有 FullAccess token 可调 |
| 2 | **虚构端点** | `pm_task_submit_review/review_approve/review_reject` | 删除——`AgentTaskController` 无任何验收端点，PM 工作流无 review 环节 |
| 3 | **ProjectStatus** | `…Completed,Archived,Suspended` | `Planning,Active,OnHold,Completed,Archived`（无 `Suspended`） |
| 4 | **BuildStatus** | `Pending,Running,…,Cancelled,PendingApproval` | `PendingApproval,Approved,Building,Success,Failed,Rejected` |
| 5 | **TaskItem.Source** | 列为枚举 | 改为 `string?` 自由字段，移出枚举表 |
| 6 | **FeatureModule.Status** | `Draft,Active,Deprecated` | 删除——实体无 Status 字段 |
| 7 | **AI 路由前缀** | `/Ai/Chat` 等 | `/admin/Ai/Chat`（框架自动加 `admin/` 前缀） |
| 8 | **关联规则** | 建任务需 `RequirementId+ProjectId` | 还硬性必填 `FeatureModuleId`、`Content` |
| 9 | **遗漏端点** | — | 补 `/agent/requirement/AddComment`、`/agent/progress/Builds` |
| 10 | **安全发现** | — | `TaskAgentController` `/api/task/*` **无鉴权**，标注待处理 |

---

## 1. 背景与目标

### 1.1 为什么需要 MCP

当前 AI Agent（包括 Hermes）调用 PM 系统时面临三个核心问题：

1. **路由探测成本高** — 每次需要先扫代码或试接口才知道怎么调
2. **参数格式不统一** — 部分端点用 `FromForm`，部分用 `FromBody`(JSON)，枚举值需要查文档
3. **业务流程不清晰** — 不知道先调什么后调什么，依赖关系靠试错

**MCP Server 解决方式**：将所有 API 调用封装为标准化的 MCP Tools。Agent 通过
`tools/list` 一次性获取所有工具定义（名称、参数 Schema、描述），无需翻代码、无需试错。

### 1.2 设计目标

- Agent 接入零配置：拿到 MCP Server URL + Token 即可操作 PM 全功能
- 参数零试错：每个工具的 `inputSchema` 给出类型、必填、可选值、枚举提示（**枚举值必须与
  `Enums.cs` 逐字一致**，见 §5）
- 权限透明：每个工具标注所需 Scope **及 Token 类型**（受限 / FullAccess，见 §2.3、§3）
- 错误可理解：返回结构化错误（code + message + hint 修复建议）

---

## 2. 架构

```
Hermes Agent / Claude / Codex / 其他 MCP Client
          │
          │ MCP 协议
          ▼
┌──────────────────────────────────────┐
│      PM MCP Server (Python)          │
│  tools/list  → 返回所有工具定义      │
│  tools/call  → 执行具体工具          │
│  ┌──────────────────────────────┐    │
│  │ PMApiClient                   │    │
│  │  · 统一 HTTP 调用层          │    │
│  │  · 按端点选 FromForm / JSON  │    │
│  │  · 附加 Authorization: Bearer │    │
│  │  · 错误标准化 + hint         │    │
│  └──────────────────────────────┘    │
└──────────────────────────────────────┘
          │  HTTPS
          ▼
┌──────────────────────────────────────┐
│      PM Server (.NET, pm.saibo.club) │
│  /agent/*   → Agent API（标 [Scope]）│
│  /admin/*   → Admin API（无 [Scope]）│
│  /agent/schema → 数据模型            │
└──────────────────────────────────────┘
```

### 2.1 部署形态与 Transport

> ⚠️ v1.0 同时写了 "stdio transport" 和 `--url http://…`（HTTP transport），两者矛盾。

**选型结论：HTTP/SSE Transport，Python mcp SDK，独立项目。**

| 维度 | 选型 | 理由 |
|------|------|------|
| **Transport** | **HTTP/SSE**（推荐） | PM MCP Server 常驻 PM 服务器或局域网机器，多个 Client（Hermes / Claude / Codex）共享一个 Server 实例。stdio 需要每个 Client 启动一个子进程，资源浪费。 |
| **语言** | **Python** | 有官方 `mcp` SDK，依赖极少（只需 `mcp` + `httpx`）。PM 后端是 .NET，但 MCP Server 只是 HTTP 代理层，不需要 .NET 运行时。 |
| **代码位置** | **PM api 仓库内** `pm-mcp-server/` | 与 PM 后端同一仓库，API 变更时同步更新 MCP 工具。比独立仓库少一个维护点。 |
| **后端配合** | **不需要** | MCP Server 只调现有 HTTP API（`/agent/*` + `/admin/*`），不改 .NET 代码。验收流程等新功能需要后端先实现，MCP 再做追加。 |

**stdio 配置（备选，如 Client 不支持 HTTP/SSE）：**

```json
{
  "mcpServers": {
    "pm-mcp": {
      "command": "python",
      "args": ["-m", "pm_mcp_server"],
      "env": { "PM_MCP_BASE_URL": "https://pm.saibo.club", "PM_MCP_API_KEY": "sk_..." }
    }
  }
}
```

**HTTP/SSE 配置（推荐）：** PM MCP Server 常驻监听端口（如 8765），
Client 通过 URL `http://<server>:8765/mcp` 连接。Hermes 侧配置：
```bash
hermes mcp add pm-mcp --url http://192.168.10.62:8765/mcp
```
PM MCP Server 通过 HTTPS 调用 PM 后端，可部署在 PM 服务器或局域网任意机器。

### 2.2 Token 配置

```bash
export PM_MCP_BASE_URL=https://pm.saibo.club   # 已确认：401(需鉴权)＝服务存在
export PM_MCP_API_KEY=sk_xxxxxxxx...           # 访问令牌，sk_ 前缀
```

MCP Server 自动附加 `Authorization: Bearer ${PM_MCP_API_KEY}`。

### 2.3 鉴权与 Scope 模型（**最关键，决定哪些工具可用**）

PM 复用框架 `SB.Feature.AccessToken`。`sk_` 令牌经 `ApiKeyMiddleware` 桥接为
`JwtAdmin` 身份后，由全局 `ScopeFilter` 做闸门。**令牌分两类**：

| 令牌类型 | 含义 | 能调的端点 |
|----------|------|-----------|
| **FullAccess token（首选）** | god key，签发时 `fullAccess=true` | **全部端点**（含所有 `/admin/*`、`/admin/Ai/*`），旁路 scope |
| **受限 token** | 签发时指定了 scope（如 `read:task,write:task`） | **仅**标了 `[Scope]` 且 scope 匹配的端点 = 只有 `/agent/*` 六个 controller |

> ✅ **MCP Server 优先使用 FullAccess token**。当前部署已有 FullAccess token
>（`sk_48c02cac2...`，已确认可调通所有端点），直接使用，无需切换。
> 受限 token 仅在需要限制 AI 权限范围时使用（如只读场景）。

`ScopeFilter` 的判定规则（源码 `SB.Feature.AccessToken/Services/ScopeFilter.cs`）：

- **端点标了 `[Scope("x")]`**（仅 `/agent/*`）：受限 token 必须拥有 `x`，否则 403；
  FullAccess 放行；**密码登录 JWT 反而被拒**（这些端点是 API-Key 专用）。
- **端点未标 `[Scope]`**（所有 `/admin/*`）：**受限 token 一律 403**；
  只有 FullAccess token 或浏览器密码登录放行。

> **直接后果**：§3 工具表中标 `🔑 Full` 的工具（绝大多数 `/admin/*`），
> **必须配 FullAccess token 才能用**。当前直接配已有的 FullAccess token 即可覆盖全功能。

**FullAccess 签发约束**（`AccessTokenOptions` + `AccessTokenController`）：
- 需 `Features:AccessToken:EnableFullAccess=true`（**默认 false，生产建议保持关闭**）；
- 仅 super 账号可签发；必须设过期，且 ≤ `FullAccessMaxTtlDays`（默认 90 天）。

各 `/agent/*` controller 的真实 scope：

| Controller | 默认 Scope | 只读 Action Scope |
|------------|-----------|-------------------|
| `AgentProjectController` | `read:project`（**整个 controller**，含 POST Create） | — |
| `AgentRequirementController` | `write:requirement` | GET 用 `read:requirement` |
| `AgentTaskController` | `write:task` | GET 用 `read:task` |
| `AgentCiController` | `write:ci` | GET 用 `read:ci` |
| `AgentProgressController` | `read:progress` | — |
| `AgentSchemaController` | `read:project` | — |

> ⚠️ `AgentProjectController` 整体挂 `read:project`，但 `Create` 是 POST 写操作——
> 受限 token 只要有 `read:project` 即可建项目。属代码侧 scope 标注疏漏，见附录 A-7。

---

## 3. 工具列表

图例：**Token** 列 `🔓 read` / `✏️ write` = 受限 token 带对应 scope 即可；
`🔑 Full` = 端点无 `[Scope]`，**必须 FullAccess token**。路由均为后端真实路径
（`/agent/*` 不加前缀；`/admin/*` 由框架 `AdminRoutePrefixConvention` 自动加）。

### 3.1 Project — 项目管理

| Tool | 方法 路由 | Token | 优先级 |
|------|----------|-------|--------|
| `pm_project_list` | GET `/agent/project/List` | 🔓 read:project | P0 |
| `pm_project_detail` | GET `/agent/project/Detail` | 🔓 read:project | P0 |
| `pm_project_create` | POST `/agent/project/Create` | ⚠️ read:project（应为 write，见 A-7） | P0 |
| `pm_project_sub_list` | GET `/agent/project/SubList` | 🔓 read:project | P1 |
| `pm_project_overview` | GET `/admin/Project/Overview` | 🔑 Full | P1 |
| `pm_project_tree` | GET `/admin/Project/Tree` | 🔑 Full | P1 |
| `pm_dashboard_workbench` | GET `/admin/Dashboard/Workbench` | 🔑 Full | P2 |
| `pm_project_health` | GET `/agent/progress/HealthScore` | 🔓 read:progress | P2 |

### 3.2 Requirement — 需求管理

| Tool | 方法 路由 | Token | 优先级 |
|------|----------|-------|--------|
| `pm_requirement_list` | GET `/agent/requirement/List` | 🔓 read:requirement | P0 |
| `pm_requirement_detail` | GET `/agent/requirement/Detail` | 🔓 read:requirement | P0 |
| `pm_requirement_create` | POST `/agent/requirement/Create` | ✏️ write:requirement | P0 |
| `pm_requirement_update` | POST `/agent/requirement/Update` | ✏️ write:requirement | P0 |
| `pm_requirement_delete` | POST `/agent/requirement/Delete` | ✏️ write:requirement | P1 |
| `pm_requirement_update_status` | POST `/agent/requirement/UpdateStatus` | ✏️ write:requirement | P0 |
| `pm_requirement_add_comment` | POST `/agent/requirement/AddComment` | ✏️ write:requirement | P2 |
| `pm_requirement_split_tasks` | POST `/agent/requirement/AiSplitTasks` | ✏️ write:requirement | P0 |
| `pm_requirement_confirm_tasks` | POST `/admin/Requirement/AiConfirmTasks` | 🔑 Full | P0 |

> ⚠️ **流程割裂**：`AiSplitTasks`（拆解，返回 `batchId`）在 `/agent/*` 下受限 token 可调，
> 但配套的 `AiConfirmTasks`（确认落库）只在 `/admin/*` 下、需 FullAccess。受限 token 能拆不能确认。
> 见附录 A-5。

### 3.3 Task — 任务管理

| Tool | 方法 路由 | Token | 优先级 |
|------|----------|-------|--------|
| `pm_task_list` | GET `/agent/task/List` | 🔓 read:task | P0 |
| `pm_task_detail` | GET `/agent/task/Detail` | 🔓 read:task | P0 |
| `pm_task_available` | GET `/agent/task/Available` | 🔓 read:task | P0 |
| `pm_task_logs` | GET `/agent/task/Logs` | 🔓 read:task | P1 |
| `pm_task_claim` | POST `/agent/task/Claim` | ✏️ write:task | P0 |
| `pm_task_start` | POST `/agent/task/Start` | ✏️ write:task | P0 |
| `pm_task_complete` | POST `/agent/task/Complete` | ✏️ write:task | P0 |
| `pm_task_fail` | POST `/agent/task/Fail` | ✏️ write:task | P1 |
| `pm_task_cancel` | POST `/agent/task/Cancel` | ✏️ write:task | P1 |
| `pm_task_resume` | POST `/agent/task/Resume` | ✏️ write:task | P2 |
| `pm_task_create` | POST `/admin/TaskItem/Add` | 🔑 Full | P0 |
| `pm_task_exec_by_ai` | POST `/admin/TaskItem/ExecByAi` | 🔑 Full | P1 |

> ❌ v1.0 的 `pm_task_submit_review / review_approve / review_reject` **已从工具表中删除**：
> `AgentTaskController` 不存在这些端点，PM 任务工作流是
> `Claim → Start → Complete/Fail`（无独立验收环节）。
> Agent 接单标识用 token 的 `KeyName`（`Claim/Start/Complete/Fail` 不传 agentName）。

> ✅ **验收流程（需求 #32，待后端实现）**：当前任务只有 `Running → Success/Failed`，无验收环节。
> 计划中的验收流程需要先在后端实现新状态和端点，MCP 工具才可接入：
> - 后端新增：`PendingReview`（待验证）和 `Rework`（退回重修）两个 ExecStatus
> - 后端新增：验收端点 `SubmitForReview` / `ReviewApprove` / `ReviewReject`
> - 完成后 MCP 对应新增上述三个工具
> - 当前阶段：**MCP Server 先不做验收工具**，待后端就绪后追加。

**任务状态机**（当前，实测自 `TaskItemService`）：

```
ClaimStatus:  Pending ──Claim──▶ Claimed
ExecStatus:   Idle ──Start──▶ Running ──Complete──▶ Success
                                  └────Fail──▶ Failed (可 Resume 回 Idle)
                                  └────(超时)──▶ Timeout / Hibernated ──Resume──▶ Idle
```

**任务状态机**（验收流程接入后，需求 #32）：

```
ClaimStatus:  Pending ──Claim──▶ Claimed
ExecStatus:   Idle ──Start──▶ Running ──SubmitForReview──▶ PendingReview
                                                  ├──ReviewApprove──▶ Success (已验证)
                                                  └──ReviewReject──▶ Rework (退回重修)
                                                                       └──Start──▶ Running
                                  ❌ Fail──▶ Failed (可 Resume 回 Idle)
                                  ❌ Timeout──▶ Timeout / Hibernated ──Resume──▶ Idle
```

### 3.4 FeatureModule — 功能模块（全部 🔑 Full）

| Tool | 方法 路由 | Token | 优先级 |
|------|----------|-------|--------|
| `pm_feature_module_tree` | GET `/admin/FeatureModule/Tree` | 🔑 Full | P1 |
| `pm_feature_module_list` | GET `/admin/FeatureModule/List` | 🔑 Full | P1 |
| `pm_feature_module_add` | POST `/admin/FeatureModule/Add` | 🔑 Full | P1 |
| `pm_feature_module_delete` | POST `/admin/FeatureModule/Del` | 🔑 Full | P2 |

> 创建任务硬性要求 `FeatureModuleId`（见 §6），但功能模块的增删查只在 `/admin/*` 下。
> **受限 token 拿不到功能模块 ID，也就无法建任务**——这是受限 token 路线的硬缺口。

### 3.5 CI/CD — 流水线

| Tool | 方法 路由 | Token | 优先级 |
|------|----------|-------|--------|
| `pm_build_trigger` | POST `/agent/ci/Trigger` | ✏️ write:ci | P1 |
| `pm_build_status` | GET `/agent/ci/Status` | 🔓 read:ci | P1 |
| `pm_build_jobs` | GET `/agent/ci/Jobs` | 🔓 read:ci | P2 |
| `pm_build_job_trace` | GET `/agent/ci/JobTrace` | 🔓 read:ci | P2 |
| `pm_build_test_report` | GET `/agent/ci/TestReport` | 🔓 read:ci | P2 |
| `pm_build_link` | POST `/agent/ci/Link` | ✏️ write:ci | P2 |
| `pm_build_list` | GET `/agent/progress/Builds` | 🔓 read:progress | P2 |
| `pm_release_list` | GET `/agent/progress/Releases` | 🔓 read:progress | P2 |
| `pm_changelog_list` | GET `/agent/progress/Changelog` | 🔓 read:progress | P2 |

> `pm_build_trigger` 参数是 `profileId`（不是 v1.0 写的 `PipelineProfileId`），
> 默认 `autoApprove=true` 创建后立即审批并真正跑 GitLab。

### 3.6 AI — AI 能力（全部 🔑 Full，POST `[FromBody] AiChatInput`）

| Tool | 方法 路由 | Token | 优先级 |
|------|----------|-------|--------|
| `pm_ai_chat` | POST `/admin/Ai/Chat` | 🔑 Full | P0 |
| `pm_ai_execute` | POST `/admin/Ai/Execute` | 🔑 Full | P0 |
| `pm_ai_analyze_code` | POST `/admin/Ai/AnalyzeCode` | 🔑 Full | P1 |
| `pm_ai_generate_code` | POST `/admin/Ai/GenerateCode` | 🔑 Full | P1 |
| `pm_ai_design_feature` | POST `/admin/Ai/DesignFeature` | 🔑 Full | P1 |
| `pm_ai_review_code` | POST `/admin/Ai/ReviewCode` | 🔑 Full | P1 |
| `pm_ai_memory_search` | GET `/admin/AiMemory/Search` | 🔑 Full | P2 |

`AiChatInput`（统一 JSON body）字段：
```jsonc
{
  "ProjectId": 1,            // int?  可选
  "SubProjectId": null,      // int?  可选
  "RequirementId": null,     // int?  可选
  "Instruction": "……",      // string 主指令/prompt（必填语义）
  "TaskType": "generate",    // string? 仅 Execute 用于分发，见下
  "FocusPaths": ["src/a.cs"],// string[]? 可选，聚焦文件
  "ConversationHistory": [{"Role":"user","Content":"…"}]  // 可选多轮
}
```
`pm_ai_execute` 按 `TaskType`（**小写字符串，非 `AiTaskType` 枚举**）分发：
`analyze`/`analyzecode`、`generate`/`generatecode`、`design`/`designfeature`、
`review`/`reviewcode`，**其它值或留空 → 走 Chat**。

### 3.7 Document — 文档（全部 🔑 Full）

| Tool | 方法 路由 | Token | 优先级 |
|------|----------|-------|--------|
| `pm_document_list` | GET `/admin/ProjectDocument/List` | 🔑 Full | P1 |
| `pm_document_add` | POST `/admin/ProjectDocument/Add` | 🔑 Full | P1 |
| `pm_notebook_list` | GET `/admin/Notebook/List` | 🔑 Full | P2 |
| `pm_notebook_add` | POST `/admin/Notebook/Add` | 🔑 Full | P2 |

### 3.8 Schema & Info

| Tool | 方法 路由 | Token | 优先级 |
|------|----------|-------|--------|
| `pm_schema` | GET `/agent/schema` | 🔓 read:project | P0 |

> `pm_schema` 应在任何流程**最先调用**——它返回 PM 全模型（实体/字段/外键/枚举/派生规则/
> 关联约束），是“零试错”的真正来源；本文件 §5 是其离线快照。

---

## 4. Tool 定义规范

每个 Tool 按此模板定义（以受限 token 可调的 `/agent/*` 为例）：

```python
{
    "name": "pm_requirement_create",
    "description": (
        "创建需求。POST /agent/requirement/Create，需 write:requirement scope。"
        "参数以 FromForm 整体绑定 Requirement 实体。"
        "返回需求 ID，可用于后续 pm_requirement_split_tasks 拆解为任务。"
    ),
    "inputSchema": {
        "type": "object",
        "properties": {
            "ProjectId": {"type": "integer", "description": "项目 ID。先调 pm_project_list 获取。"},
            "Title":     {"type": "string",  "description": "需求标题（必填）"},
            "Content":   {"type": "string",  "description": "需求详细描述"},
            "Type":   {"type": "string", "enum": ["Feature","Bugfix","Improvement","Task","Research"], "description": "默认 Feature"},
            "Status": {"type": "string", "enum": ["Draft","Todo","InProgress","Testing","Done","Closed"], "description": "默认 Draft"}
        },
        "required": ["ProjectId", "Title"]
    }
}
```

**约定：**

1. **命名**：`pm_<domain>_<action>`，如 `pm_task_claim`
2. **描述**：写明 `方法 + 真实路由 + Token 要求 + 参数绑定方式(FromForm/FromBody) + 前后置`
3. **枚举值**：在 `enum` 中列全（英文字符串），**与 §5（= `Enums.cs`）逐字一致**
4. **必填字段**：`required` 数组；服务层二次校验的必填（如建任务的 `FeatureModuleId`）也要标
5. **外键提示**：在 `description` 说明需先调哪个工具取 ID
6. **Token 提示**：`🔑 Full` 工具在 description 标注“需 FullAccess token，受限 token 会 403”

**参数绑定方式速查：**

| 端点组 | 绑定 | 说明 |
|--------|------|------|
| `/agent/*` GET | query string | `?id=1&page=1` |
| `/agent/*` POST | `FromForm` | 表单字段；`Create/Update` 是整实体绑定 |
| `/admin/Ai/*` | `FromBody` JSON | 统一 `AiChatInput` |
| `/admin/*` 其它 POST | `FromForm`（多数标 `AllowJsonBody`，亦收 JSON） | `Add/Edit` 多为整实体绑定 |

---

## 5. 枚举值对照表（**已逐字核对 `Sb.Apps.Pm.Models/Enums.cs`**）

> 所有业务枚举都标了 `[JsonStringEnumConverter]`，API **返回/接收均为字符串**。

### 5.1 需求域

| 枚举类型 | 字段 | 可选值 |
|----------|------|--------|
| `RequirementType` | Type | `Feature, Bugfix, Improvement, Task, Research` |
| `RequirementStatus` | Status | `Draft, Todo, InProgress, Testing, Done, Closed` |
| `RequirementSource` | Source | `Manual, Telegram, AI, Git, Hermes` |
| `Complexity` | Complexity | `Low, Medium, High, VeryHigh` |

### 5.2 任务域

| 枚举类型 | 字段 | 可选值 |
|----------|------|--------|
| `TaskItemType` | Type | `Development, Bug, Design, Testing, Docs, Other` |
| `TaskItemClaimStatus` | ClaimStatus | `Pending, Claimed` |
| `TaskItemExecStatus` | ExecStatus | `Idle, Running, Success, Failed, Timeout, Hibernated` |
| `TaskItemExecMode` | ExecMode | `Api, Ai` |
| `TaskItemLogAction` | （日志）| `Created, Claimed, ExecStart, ExecSuccess, ExecFailed, ExecTimeout, Hibernated, Resumed, Cancelled` |

> ⚠️ `TaskItem.Source` / `SourceRef` 是 **`string?` 自由字段，不是枚举**（v1.0 误列）。
> 不要给它配 `enum`；如需取值惯例，参考实际数据而非 Schema。

### 5.3 项目域

| 枚举类型 | 字段 | 可选值 |
|----------|------|--------|
| `ProjectStatus` | Status | `Planning, Active, OnHold, Completed, Archived` |
| `CustomerStatus` | （客户）| `Active, Expired, Terminated` |

> ⚠️ 是 `OnHold`，**不是** v1.0 写的 `Suspended`。

### 5.4 构建 / 发布域

| 枚举类型 | 字段 | 可选值 |
|----------|------|--------|
| `BuildStatus` | Status | `PendingApproval, Approved, Building, Success, Failed, Rejected` |
| `BuildType` | Type | `Manual, CI, Scheduled` |
| `ReleaseStatus` | Status | `Pending, Approved, Deploying, Success, Failed, Rollback` |
| `ApprovalStatus` | — | `None, Pending, Approved, Rejected` |
| `PipelineStatus` | — | `Pending, Running, Success, Failed, Cancelled` |

> ⚠️ `BuildStatus` 与 v1.0 完全不同；`PipelineStatus`（GitLab 流水线态）才有 `Running/Cancelled`，
> 二者不要混用。

### 5.5 AI 域

| 枚举类型 | 字段 | 可选值 |
|----------|------|--------|
| `AiTaskType` | TaskType | `AnalyzeChat, ExtractRequirement, FixBug, GenerateChangelog, GenerateMindMap, AnalyzeCode, ReviewCode, GenerateCode, DesignFeature` |
| `AiTaskStatus` | Status | `Pending, Running, Completed, Failed` |
| `AiMemoryScope` | Scope | `Global, Project, Permanent` |

> 注意区分：`/admin/AiTask/*` 用上面的 `AiTaskType` 枚举；而 `/admin/Ai/Execute` 的
> `TaskType` 是**小写自由字符串**（`analyze/generate/design/review`），见 §3.6。

> ❌ **功能模块无状态枚举**：`FeatureModule` 实体只有
> `Id/ProjectId/ParentId/Name/Description/Sort` 等，**没有 Status 字段**。v1.0 的
> “FeatureModule.Status: Draft/Active/Deprecated” 已删除。

---

## 6. 关联规则（含服务层强校验）

| 操作 | 关联 / 必填规则（源码出处） |
|------|------------------------------|
| **创建任务** `/admin/TaskItem/Add` | 必填 `Title`、`Content`、`RequirementId`、`FeatureModuleId`（`TaskItemService.Create` 逐项 `Result.Fail`）。`ProjectId` 可省——会由 `RequirementId` 反查补全 |
| 创建需求 `/agent/requirement/Create` | 必填 `ProjectId`、`Title`；`CustomerId`/`ParentId` 可选 |
| 创建子需求 | 指定 `ParentId` |
| 创建功能模块 `/admin/FeatureModule/Add` | 必填 `ProjectId`；`ParentId` 可选（建子模块） |
| 触发构建 `/agent/ci/Trigger` | 必填 `profileId`（PipelineProfile ID） |
| 关联构建 `/agent/ci/Link` | `buildId` + (`requirementId` 或 `taskItemId`) |
| 拆解→确认 | `AiSplitTasks` 返回 `batchId` → `AiConfirmTasks(batchId)` 落库（注意跨 agent/admin，见 §3.2） |

**典型流程（FullAccess token）**：
`pm_schema` → `pm_project_create` → `pm_requirement_create` →
`pm_feature_module_add`（拿 FeatureModuleId）→ `pm_task_create`
（Title+Content+RequirementId+FeatureModuleId）→ `pm_task_claim/start/complete`。

**受限 token 可达流程**（仅 `/agent/*`）：
读项目/需求/任务、建/改需求、`AiSplitTasks` 拆解、接单执行 CI。
**不可**：确认拆解落库、建/查功能模块、建任务、所有 AI 对话、文档——这些需 FullAccess。

---

## 7. 错误处理

工具返回统一包络（PM 后端 `Result` = `{code, message, data}`，`code≠0` 即失败）：

成功：
```json
{ "content": [{"type":"text","text":"{\"code\":0,\"message\":\"ok\",\"data\":{...}}"}], "isError": false }
```

失败（MCP Server 补 `hint`）：
```json
{ "content": [{"type":"text","text":"{\"code\":-1,\"message\":\"任务必须挂功能模块(FeatureModuleId)\",\"hint\":\"先调 pm_feature_module_tree 获取功能模块ID（需 FullAccess token）\"}"}], "isError": true }
```

**常见错误 → hint 映射**（MCP Server 内置）：

| 后端 message / HTTP | hint |
|---------------------|------|
| HTTP 403 `缺少 scope 权限: x` | 当前 token 无 `x` scope，换带该 scope 的 token |
| HTTP 403 `该端点不对受限 API Key 开放` | 此为 `/admin/*` 端点，需 **FullAccess token** |
| HTTP 401 | token 无效/过期/被吊销，重签 `sk_` 令牌 |
| `任务必须关联需求(RequirementId)` | 先 `pm_requirement_list` 取 RequirementId |
| `任务必须挂功能模块(FeatureModuleId)` | 先 `pm_feature_module_tree` 取 ID（需 FullAccess） |
| `任务描述(Content)不能为空` | 补 `Content` 字段 |

---

## 8. 实施计划

> 调整：把 token 路线作为分期主线——P0 先打通受限 token 能跑的 `/agent/*` 闭环，
> 再视是否启用 FullAccess 决定 `/admin/*` 工具。

### Phase 1：`/agent/*` 受限 token 闭环（P0）

| # | 工具 | Token | 工作量 |
|---|------|-------|--------|
| 1 | `pm_schema` | read:project | 小 |
| 2 | 项目读 + 建：list/detail/create/sub_list | read/✏️project | 小 |
| 3 | 需求 CRUD：list/detail/create/update/update_status/split_tasks | read/✏️requirement | 小 |
| 4 | 任务流：list/detail/available/claim/start/complete/fail | read/✏️task | 小 |

### Phase 2：`/admin/*` FullAccess 工具（P0/P1，**前置：签发 FullAccess token**）

| # | 工具 | 工作量 |
|---|------|--------|
| 5 | 任务确认/创建：requirement_confirm_tasks、task_create、task_exec_by_ai | 中 |
| 6 | 功能模块：tree/list/add/delete | 小 |
| 7 | AI：chat/execute/analyze/generate/design/review | 中 |

### Phase 3：CI/CD & 文档（P2）

| # | 工具 | 工作量 |
|---|------|--------|
| 8 | 构建：trigger/status/jobs + build/release/changelog list | 中 |
| 9 | 文档：document_list/add、notebook_list/add | 小 |
| 10 | 其它：project_overview/tree、dashboard_workbench、ai_memory_search、project_health | 中 |

---

## 9. 验证标准

- [ ] Agent 通过 `tools/list` 获取全部工具定义，每个含类型/必填/枚举/Token 要求
- [ ] **受限 token** 跑通：`pm_schema` → 建需求 → `split_tasks`（拆解返回 batchId）
- [ ] **FullAccess token** 跑通：建项目 → 建功能模块 → 建任务（四必填）→ claim/start/complete
- [ ] 用受限 token 调 `/admin/*` 工具时，返回 403 且 hint 指向“需 FullAccess token”
- [ ] §5 每个 `enum` 与 `Enums.cs` 逐字一致（CI 可加一步从源码生成校验）

---

## 附录 A：代码审计发现（v1.1 依据）

> 审计对象：`apps/pm/api`（commit `c5e573c` 时点）。逐一附源码出处，便于复核。

- **A-1 鉴权模型（阻断级）**：`/admin/*` 全部 controller 仅
  `[Authorize(JwtAdmin)]`、**无 `[Scope]`**；`ScopeFilter` 对未标 scope 的端点拒绝受限
  key。⇒ v1.0 把大量 `/admin/*` 工具标 P0/P1，受限 token 全调不通。已在 §2.3/§3 标 `🔑 Full`。
  *出处：`SB.Feature.AccessToken/Services/ScopeFilter.cs:32-49`、各 `*Controller.cs` 类头。*

- **A-2 虚构端点**：`AgentTaskController` 仅
  `List/Detail/Available/Logs/Claim/Start/Complete/Fail/Cancel/Resume`，
  **无 `SubmitForReview/ReviewApprove/ReviewReject`**。⇒ 删除三个 `pm_task_review_*`。
  *出处：`Controllers/Agent/AgentTaskController.cs`。*

- **A-3 ProjectStatus**：`Planning, Active, OnHold, Completed, Archived`。无 `Suspended`。
  *出处：`Enums.cs:16`。*

- **A-4 BuildStatus**：`PendingApproval, Approved, Building, Success, Failed, Rejected`。
  v1.0 的 `Pending/Running/Cancelled` 属 `PipelineStatus`，非 `BuildStatus`。
  *出处：`Enums.cs:55`、`:48`。*

- **A-5 流程割裂**：`AiSplitTasks` 同时存在于
  `/agent/requirement/`（write:requirement）与 `/admin/Requirement/`（无 scope）；
  而 `AiConfirmTasks` 仅 `/admin/Requirement/`。⇒ 受限 token 能拆不能确认。
  *出处：`AgentRequirementController.cs:80`、`RequirementController.cs:103,108`。*

- **A-6 路由前缀**：框架 `AdminRoutePrefixConvention` 给**相对路由**自动加 `admin/`；
  `/`-开头的绝对路由跳过。⇒ `AiController([controller]/[action])` 真实路径
  `/admin/Ai/*`（v1.0 漏 `/admin`）；`/agent/*` 保持不变。
  *出处：`SB.Framework.WebApi/Extensions/AdminRoutePrefixConvention.cs:22-28`。*

- **A-7 写操作挂只读 scope（代码建议修复）**：`AgentProjectController` 类级
  `[Scope("read:project")]`，但 `Create` 是 POST。建议给 `Create` 加
  `[Scope("write:project")]` 收紧。*出处：`AgentProjectController.cs:12,37`。*

- **A-8 平行的遗留鉴权（建议统一，非漏洞）**：`TaskAgentController` 路由 `/api/task/*`
  （`claim/{id}/start/{id}/success/{id}/fail/{id}/timeout/available`）
  **不走框架 AccessToken/Scope 体系**，自带一套独立校验：每个 action 首行
  `ValidateApiKey()` 比对请求头 `X-Api-Key` 与系统设置 `TaskAgentApiKey`。
  行为已确认 **fail-safe**：缺头、不匹配、或 `TaskAgentApiKey` 未配置（空）→ 一律 401，
  不存在匿名写入。但该静态共享密钥**不可吊销 / 不可审计 / 不限速**，弱于 `sk_` 令牌，
  且与 `/agent/task/*`（sk_ + scope）功能重叠、并行维护。建议下线或迁移到统一令牌；
  **MCP Server 一律走 `/agent/task/*`，不要用 `/api/task/*`**。
  *出处：`Controllers/Tasks/TaskAgentController.cs:10,23-77`。*

- **A-9 建任务强校验**：`Title`、`Content`、`RequirementId`、`FeatureModuleId` 四项必填。
  *出处：`TaskItemService.Create`（`:103-109`）。*

- **A-10 计数**：PM 共 **51** 个 controller（v1.0 称 50）；`/agent/*` 6 个。

## 附录 B：未决问题

- B-1 §2.1 transport：stdio 还是 HTTP？需与 Hermes 接入方式对齐后定稿。
- B-2 是否启用 `EnableFullAccess`：决定 §3 全部 `🔑 Full` 工具能否落地。默认 false。
- B-3 A-7 / A-8 两处代码问题是否在本迭代修复，还是仅在 MCP 侧规避。

---
title: "Hermes × PM 全流程智能开发工作流（去 MCP 精简版）"
version: "2.0.0"
date: "2026-06-09"
status: "draft"
supersedes: "AI_Hermes_FullWorkflow_Plan.md (v1.1.0)"
---

# AI 计划：Hermes × PM 全流程智能开发工作流 v2

> **版本**: v2.0.0 · **状态**: Draft（去 MCP + 落实 v1.1 审计与改进）
> **关系**: 取代 v1.1.0。v1.1 经代码审计发现一批与实际不符之处 + 设计缺口，本版一并修正，并**移除 MCP**。
> **前置依赖（已核实存在）**: 框架统一 AccessToken（`SB.Feature.AccessToken`，`[Scope]`+`IApiKeyContext`）、
> Agent REST API（**5** 个控制器）、`AiProviderFactory`+`HermesAiProviderAdapter`、`AiPipelineExecutor`、
> CI/CD（GitLab 集成 + 构建审批 + 发布审批/回滚）、Telegram 聊天层（`SB.Chat`/`SB.Chat.Telegram` NuGet 包，Polling/Webhook）。

---

## 0. 本版相对 v1.1 的变更摘要

| 类别 | v1.1 | v2 |
|---|---|---|
| **MCP** | Phase 2 = pm-mcp-server（~12 工具） | **移除**。Hermes 经 **直连 REST + Cron/Webhook Worker** 接入 |
| **鉴权 key** | 源码硬编码 `sk_pm_***`，挂 admin 账号 | **服务账号专属 scoped key**，明文走环境变量/Nexus 凭据库 |
| **AI 改代码落地** | 改代码→commit→直接触发 CI/CD | **强制 `ai/<taskId>` 分支 + MR + 复用既有构建/发布审批闸门** |
| **Webhook** | 单向无鉴权、无幂等、推/轮询并存 | **HMAC 签名 + 幂等键 + 单一推送（Webhook 主、Cron 兜底）** |
| **自动建需求** | 任意 >20 字消息触发 | **默认关 + 去重 + 频率/预算限制**，且与已存在的 `AiRealtimeEnabled` 区分语义 |
| **控制器数** | 称 7 个，含 `/agent/token`、`/agent/memory` | 实为 **5** 个；那两个是幻影（token 已退役、memory 从未存在） |
| **scope 模型** | key 串 `read:project,…`（project/progress 会 403） | **Phase 0 先修控制器双 `[Scope]` 不一致**，再用统一 `read:/write:` 串 |
| **API 签名** | `Claim()/Complete()/Available()`、`config.Value`、`task.AutoAssign`、`Create` override | 订正为 `ClaimNext/ReportSuccess/GetAvailable(带参)`、`DataContent`、新增 `AutoAssign`、`Create` 非 override |
| **状态机** | Claimed 当作 ExecStatus 值 | 改为 **`ClaimStatus × ExecStatus` 两维**（含已存在的 `Timeout`） |
| **Requirement.Source** | 称"新增" | **已存在**，改为给 `RequirementSource` 扩 `Hermes` 值 |
| 新增 | — | **MVP 竖切**、**自动化测试**、**AI 调用预算/可观测** |

---

## 1. 背景与目标

### 1.1 现状（已核实）

- **框架统一 AccessToken 鉴权** ✅（2026-06-08 完成，运行时验证 X-Master-Key 全失效）
- **Agent REST API** ✅ **5 个控制器**：`AgentProject` / `AgentRequirement` / `AgentTask` / `AgentCi` / `AgentProgress`（路由 `/agent/{资源}/{action}`，`[Authorize(JwtAdmin)]`）
- **AI Provider** ✅ `AiProviderFactory.GetAdapter(type)`；现有 adapter：`HermesAiProviderAdapter`（OpenAI 兼容、调 Hermes Gateway）、`StubAiProviderAdapter`。**无 DeepSeekDirectAdapter**
- **AiPipeline** ✅ `Services/Chat/AiPipelineExecutor`（`ProcessRealtimeMessage`/`ProcessBatch`，`RequirementExtract` 步骤产出需求）
- **CI/CD** ✅ `GitLabApiService`(/api/v4)、构建审批 `PipelineProfileService.ApproveBuild`、发布审批/回滚 `ReleaseService.Approve/Reject/Rollback`、部署 `DeployExecutorService.ExecuteAsync`
- **Telegram 聊天层** ✅ `SB.Chat`+`SB.Chat.Telegram`（NuGet 包），Polling（`TelegramPollingTask`）+ Webhook（`TelegramWebhookController`）双模式

> 幻影澄清：`/agent/token/*` 已随 PM 自有 agent token 栈退役删除（提交 `89e0c49`）；`/agent/memory/*` 从未存在。本版不引用这两者。

### 1.2 目标（不变）

闭环：**用户 TG 发消息 → PM Bot 记录需求/任务 → Hermes 认领、改代码、开 MR → 审批通过 → CI/CD 发布 → TG 通知**。全程在 PM 留痕、可审计、可恢复。

### 1.3 设计原则

1. **一切走 PM `/agent/*` API** —— Hermes 不直接动 PM 数据库。
2. **AI 产出必经评审闸门** —— AI 代码落 `ai/*` 分支 + MR + 既有审批，**不允许 AI 直接发布**。
3. **凭据最小权限 + 不落源码** —— 服务账号 scoped key，明文走环境变量/凭据库。
4. **可恢复 + 幂等** —— 任务状态机 + 幂等键，重复投递不重复执行。
5. **PM 在线 = Bot 在线** —— Telegram Bot 在 PM 进程内 Polling。

---

## 2. 整体架构（去 MCP）

```
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│  Telegram    │   │ Hermes Agent │   │ PM Admin Web │
│ (用户聊天)    │   │ (Cron/Webhook│   │ (管理后台)    │
└──────┬───────┘   │  Worker)     │   └──────┬───────┘
       │           └──────┬───────┘          │
       ▼                  │ 直连 HTTPS         ▼
┌──────────────┐          │  Bearer sk_…      ┌─────────────┐
│ PM Bot       │          ▼                   │ 审批/MR 评审 │
│ (in-process) │   ┌──────────────────────────┴─────────────┐
└──────┬───────┘   │        PM 系统 (.NET 10 WebApi)         │
       ▼           │  /agent/* REST API（5 控制器）          │
┌──────────────────┤  AiPipelineExecutor / Ai 服务层          │
│ 业务服务层        │  Pipelines（GitLab/构建审批/发布回滚）   │
└──────────────────┴─────────────────────────────────────────┘
```

**与 v1 的唯一结构差异**：去掉 `pm-mcp-server` 中间层。Hermes 通过**直连 REST**（任意 HTTP 能力）+ **Cron/Webhook Worker** 调 `/agent/*`，而非 MCP 工具。

---

## 3. 安全模型（新增，贯穿所有 Phase）

### 3.1 Hermes 的凭据

- 在 **PM Admin → 系统 → AccessToken 管理** 用 **`serviceAccount=hermes-worker`** 签发**服务账号** scoped key（不要挂 `admin`/super 账号——泄露即超管、审计全记成 admin）。
- scope（见 §14 修正后串）：覆盖 read/write 五大域。
- 明文 key **不写进任何源码/prompt**，存 **环境变量 `PM_AGENT_KEY`** 或 **Nexus 凭据库**，Hermes 运行时注入。
- 速率限制 60/min（框架统一节流）；TTL 90 天轮换；停用账号即失效。

### 3.2 AI 代码评审闸门（最关键）

```
Hermes 改代码 → 提交到 ai/<taskId> 分支 → 开 MR
  → CI 构建（PipelineProfileService 的构建审批：PendingApproval）
  → 人工 / 规则审批通过 → 合并 → ReleaseService 发布（发布审批）
  → 失败可 ReleaseService.Rollback
```

- **禁止** AI 提交直接进主干 / 直接触发生产发布。复用 PM **已有**的 `ApproveBuild` + `ReleaseService.Approve` 双审批。
- Hermes 用 `git worktree` 在隔离工作区改代码，避免污染主仓。

### 3.3 Webhook 安全 + 幂等

- PM→Hermes 推送加 **HMAC 签名头** `X-PM-Signature: sha256=…`（密钥 `hermes_webhook_secret` 系统设置），Hermes 侧校验。
- 任务处理以 **`taskId + ClaimStatus`** 做幂等：已 `Claimed`/`Running`/`Success` 的不再处理；Webhook 重试天然去重。
- **只用一种推送**：Webhook 为主；**仅当 Webhook 不可用时** Cron 兜底轮询 `Available`（二者不同时开，避免双重处理）。

### 3.4 AI 调用预算

- 自动建需求**默认关**，按群 `AutoCreateRequirement` 开关。
- 触发条件加去重（近似需求合并）+ 频率限制（每群每分钟 N 条）。
- AI 调用记 token 用量；设每日/每群预算上限（`ai_daily_token_budget`），超限降级为「仅提示不自动建」。

---

## 4. Phase 0：前置修正（必须先做，否则后续 403/编译失败）

### 4.1 修复 agent 控制器的双 `[Scope]` 不一致 🔴

**问题**：每个 agent 控制器声明了两个控制器级 `[Scope]`（如 `[Scope("read:project")]` + 裸 `[Scope("project")]`），但 `ScopeFilter` 用 `LastOrDefault` 只取最后一个。导致**没有 action 级 `[Scope]` 的端点**（`project/*`、`progress/*` 全部如此）实际要求**裸资源名** `project`/`progress`，与 `read:/write:` 约定不一致——**已实测 `project/list`、`progress/HealthScore` 用 `read:project` key 返回 403**。

**修法（二选一，推荐 A）**：
- **A. 统一约定**：删掉各控制器冗余的裸 `[Scope("project")]/[Scope("progress")]`，给无 action scope 的只读端点补 `[Scope("read:project")]/[Scope("read:progress")]`。之后全域统一走 `read:/write:`，key 串可预测。
- B. 不改代码：key 串里额外塞裸 `project,progress`（丑、易漏）。

> 文件：`Sb.Apps.Pm.WebApi/Controllers/Agent/AgentProjectController.cs`、`AgentProgressController.cs`（必要时 Requirement/Task/Ci 一并统一）。

### 4.2 模型字段/枚举补齐

- `TaskItem`：新增 `AutoAssign`(bool)、`Source`(string?)、`SourceRef`(string?)。
- `RequirementSource` 枚举：扩 `Hermes` 值（`Requirement.Source/SourceRef` **已存在**，无需加字段）。
- 确认状态机两维：`TaskItemClaimStatus{Pending,Claimed}`（`ClaimStatus`）× `TaskItemExecStatus{Idle,Running,Success,Failed,Timeout,Hibernated}`（`ExecStatus`）。

---

## 5. Phase 1：PM Telegram Bot 增强（不依赖 Hermes，可独立交付）

文件：`Sb.Apps.Pm.Services/Chat/TelegramBotCommandHandler.cs`（已有 `/start /bind /verify /unbind /menu`、`BuildMenuByRole`、`HandleMenuCallback`）。

新增命令：
- `/newreq [项目] <描述>` → `RequirementService.Create(Requirement req)`（**注意：传整个实体**，需先 new 并填 `ProjectId`/`Title`/`Source=Telegram` 等）。
- `/newtask <需求ID> <描述>` → `TaskItemService.Create(TaskItem task)`（非 override；填 `RequirementId`/`ProjectId`/`Source=tg_bot`）。
- `/ai <自然语言>` → `AiProviderFactory.GetAdapter("Hermes")` 分析 → 创建 Requirement + 多 TaskItem。
- `/mystatus` → 聚合任务/需求/最近发布。
- 在 `BuildMenuByRole`/`HandleMenuCallback` 加菜单项与回调分支。

自动建需求（§3.4 约束）：在 `TelegramWebhookHandler.SaveMessage` 末尾——**注意该处已有 `AiEnabled && AiRealtimeEnabled` 的实时分析块**——新增逻辑应受**独立开关 `AutoCreateRequirement`** 控制，复用 `AiPipelineExecutor` 的 `RequirementExtract` 产出，避免与实时分析重复触发。

---

## 6. Phase 2：Hermes 接入 PM（直连 REST，无 MCP）

### 6.1 调用方式

Hermes 用任意 HTTP 能力直连 `/agent/*`：

```
Authorization: Bearer ${PM_AGENT_KEY}          # 来自环境变量/凭据库
Content-Type: application/x-www-form-urlencoded  # 控制器 [FromForm] 绑定
Base: https://pm.saibo.club/agent   （或内网 http://<host>:6020/agent）
```

> 端点不暴露于 nginx :80 的 `/agent`，走 PM 容器 `:6020` 或另配 nginx 路由（见 §12）。

### 6.2 Worker 形态（二选一，默认 Webhook）

- **Webhook（主）**：PM 任务创建 → 带 HMAC 推送 → Hermes Worker 接收 → 直连 REST 认领/执行/回写。
- **Cron（兜底）**：`hermes cron "every 5 minutes" --prompt "GET /agent/task/Available（带 Bearer），有任务则认领…"`。**与 Webhook 互斥**。

### 6.3 Worker 处理流程

1. 取任务详情 `GET /agent/task/Detail?id=N`
2. 认领 `POST /agent/task/Claim`（service：`ClaimNext(agentName, projectId?)`）
3. 由 PM 项目配置（`GitRepositoryService`）解析**目标仓库/分支** → `git worktree` 隔离检出
4. 改代码 → commit 到 `ai/<taskId>` → push → 开 MR
5. 回写 `POST /agent/task/Complete`（service：`ReportSuccess(taskItemId, agentName, message)`；失败走 `ReportFail`/超时 `ReportTimeout`）
6. 触发构建 `POST /agent/ci/Trigger` → 进**构建审批**队列（不自动发布）

> 与 v1 差异：删除全部 `pm_*` MCP 工具与 `hermes mcp add/test`；端点知识沉淀为 Worker 的固定配置/常量，不散落在 prompt。

---

## 7. Phase 3：TG → PM → Hermes 闭环 + 安全闸门

### 7.1 PM 侧推送（带 HMAC）

`TaskItemService.Create` 末尾：若 `task.AutoAssign && task.ClaimStatus==Pending && task.ExecStatus==Idle` → `NotifyHermesAsync(task)`：
- 读系统设置 `hermes_webhook_url`（经 `SystemSettingService`，值在实体 **`DataContent`** 字段，**不是 `Value`**）。
- payload 带 `taskId/title/projectId/requirementId`；用 `hermes_webhook_secret` 算 HMAC 放 `X-PM-Signature`。
- HttpClient 按 PM 约定用 **`private static readonly HttpClient`**（不走 DI 注入）。

### 7.2 状态机（两维）

```
ClaimStatus:  Pending ──claim──> Claimed
ExecStatus:   Idle ─start─> Running ─┬─> Success ──> (CI 构建审批) ──> 发布审批 ──> 已发布
                                     ├─> Failed   ──> 需求回 Todo（重试）
                                     ├─> Timeout  ──> 重新入队
                                     └─> Hibernated（暂停）
```

### 7.3 完成通知（PM → TG）

复用生产在用的 `ChatConnectorManager.GetBot(accountId)` + `IChatBot.SendTextAsync(chatId, text)`（参考 `ChatNotificationService`）。在 MR 合并/发布完成后通知群组。

---

## 8. Phase 4：来源同步 + 可观测/预算

- `TaskItem.Source/SourceRef`（Phase 0 已加）、`RequirementSource` 加 `Hermes`：列表/详情显示来源标签（`🤖 Hermes`/`📱 TG`/`✨ AI`/手动），按来源过滤。
- 可观测：记录 AI token 用量、Worker 处理耗时、认领冲突计数；`ai_daily_token_budget` 超限降级（§3.4）。
- Admin 前端：需求/任务列表加来源过滤器 + 标签（`apps/web-antd/src/views/...`）。

---

## 9. MVP 竖切（先跑通最薄闭环，再铺开）

> 不要四个 Phase 并行。先交付一条端到端：

1. Phase 0 修 scope（否则 Hermes 读不到数据）。
2. `/newtask` 手建一个任务（`AutoAssign=true`）。
3. PM 推送（HMAC）→ Hermes Worker 认领。
4. Hermes 改一行 → `ai/<taskId>` 分支 + MR。
5. 审批合并 → CI → TG 通知完成。

跑通后再加 `/ai`、自动建需求、来源标签、Cron 兜底等。

---

## 10. 测试策略（v1 缺，本版补）

利用 `Sb.Apps.Pm.Services.Tests`：
- 命令解析单测（`/newreq`/`/newtask`/`/ai` 参数解析与边界）。
- Worker 幂等单测（同一 `taskId` 重复投递只处理一次）。
- 状态流转单测（Claim→Start→Success/Fail/Timeout 两维正确）。
- scope 鉴权回归（修复后 5 域 read/write 端点 200/403 矩阵）。
- HMAC 签名校验单测。

---

## 11. 文件变更清单（订正版）

| 文件 | 变更 | 说明 |
|---|---|---|
| `Controllers/Agent/AgentProjectController.cs`、`AgentProgressController.cs` | 修改 | Phase 0：统一 `[Scope]` |
| `Models/Tasks/TaskItem.cs` | 修改 | +`AutoAssign`/`Source`/`SourceRef` |
| `Models/Enums.cs`（`RequirementSource`） | 修改 | +`Hermes` |
| `Models/Chat/ChatGroupBinding.cs` | 修改 | +`AutoCreateRequirement`（与 `AiRealtimeEnabled` 区分） |
| `Services/Chat/TelegramBotCommandHandler.cs` | 修改 | +4 命令 + 菜单 |
| `Services/Chat/TelegramWebhookHandler.cs` | 修改 | 自动建需求（独立开关，不与实时分析重复） |
| `Services/Tasks/TaskItemService.cs` | 修改 | `Create` 末尾 HMAC 推送；幂等 |
| `Services/.../SystemSetting 注册` | 修改 | 注册 `hermes_webhook_url`/`hermes_webhook_secret`/`ai_daily_token_budget` |
| `apps/web-antd/src/views/...`（admin） | 修改 | 来源过滤/标签 |
| `Sb.Apps.Pm.Services.Tests/...` | 新增 | §10 测试 |
| ~~pm-mcp-server~~ | **删除** | 不再需要 |

---

## 12. 部署与配置

1. **签发服务账号 key**：Admin → 系统 → AccessToken 管理 → `serviceAccount=hermes-worker` + §14 scope → 明文存环境变量/Nexus。
2. **暴露 `/agent`**：Hermes 经内网 `http://<pm-host>:6020/agent` 直连；或给 nginx 加 `/agent/` 反代（当前 nginx 只代理 `/admin /api /storage /upload /swagger /health`）。
3. **系统设置**：`hermes_webhook_url`、`hermes_webhook_secret`、`ai_daily_token_budget`。
4. **Worker**：Webhook 订阅（主）或 Cron（兜底，互斥）。
5. **TG Bot**：Admin → 聊天账号管理 → 加 Bot Token → Active。

---

## 13. 风险表（更新）

| 风险 | 级别 | 缓解 |
|---|---|---|
| AI 改坏仓库 / 误发布 | 🔴 高 | `ai/*` 分支 + MR + **复用既有构建/发布双审批**，禁止 AI 直发；worktree 隔离 |
| key 泄露 | 🔴 高 | 服务账号（非 super）+ 环境变量/凭据库 + 60/min 节流 + 审计 + 90 天轮换 |
| Webhook 伪造 / 重复处理 | 🟡 中 | HMAC 签名 + `taskId+ClaimStatus` 幂等 + 推送单一来源 |
| 自动建需求刷垃圾 / AI 超支 | 🟡 中 | 默认关 + 去重 + 频率限制 + 每日 token 预算降级 |
| 认领竞争（多 Worker/人机并发） | 🟡 中 | `ClaimNext` 串行认领；同任务同时仅一人 |
| Hermes Gateway 不可用 | 🟡 中 | `AiProviderFactory` 可切其它 adapter |

---

## 14. 附录：真实端点 + scope 速查（订正）

**5 个控制器**（路由 `/agent/{资源}/{action}`，`[Authorize(JwtAdmin)]`）：
- project：`List/Detail/SubList`（读）
- requirement：`List/Detail`(read) · `Create/Update/Delete/UpdateStatus/AddComment`(write)
- task：`List/Detail/Available/Logs`(read) · `Claim/Start/Complete/Fail/Cancel/Resume`(write)
- ci：`Trigger/Link`(write) · `Status/Jobs/JobTrace/TestReport`(read)
- progress：`HealthScore/Builds/Releases/Changelog`(读)

**Service 真实签名**（v1 写错的）：
- `TaskItemService`：`ClaimNext(agentName, projectId?)`、`ReportStart`、`ReportSuccess(taskItemId, agentName, message?)`、`ReportFail`、`ReportTimeout`、`GetAvailable(projectId?)`、`Create(TaskItem)`（非 override）
- `RequirementService`：`Create(Requirement)`（非 override，传整实体）
- `SystemSetting`：值字段是 **`DataContent`**（无 `Value`），经 `SystemSettingService` 读写

**scope 串**（Phase 0 修复后，统一 read/write；Hermes 服务账号 key 用）：
```
read:project,read:requirement,write:requirement,read:task,write:task,read:ci,write:ci,read:progress
```
> 若不做 Phase 0 修复，则需改成含裸名 `project,...,progress`（实测否则 project/progress 端点 403）。

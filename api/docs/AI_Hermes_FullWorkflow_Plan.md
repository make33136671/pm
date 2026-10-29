---
title: "Hermes × PM 全流程智能开发工作流"
version: "1.1.0"
date: "2026-06-09"
status: "draft"
author: "Hermes Agent"
---

# AI 计划：Hermes × PM 全流程智能开发工作流

> **版本**: v1.1.0
> **日期**: 2026-06-09
> **状态**: Draft — 方案设计完成，待实施
> **说明**: 2026-06-08 代码库发生了重要变化：PM 自有 agent token 栈退役，改用框架统一 `SB.Feature.AccessToken`。
>           鉴权体系从 `AgentBearer` + `IAgentContext` 迁移到框架 `[Scope]` + `IApiKeyContext`（JwtAdmin）。
>           本计划已同步更新以反映这些变化。
> **前置依赖**: ✅ 框架统一 AccessToken 鉴权（已完成）、Agent REST API（已完成）、AiProvider 工厂（已完成）、
>             HermesAiProviderAdapter（已完成）、Telegram Bot 框架（已完成）

---

## 修订记录

| 版本 | 日期 | 修订要点 |
|------|------|----------|
| v1.0 | 2026-06-09 | 初版，完整方案：TG Bot → PM → Hermes 全流程开发工作流闭环 |

---

## 目录

1. [背景与目标](#1-背景与目标)
2. [整体架构](#2-整体架构)
3. [Phase 1：PM Telegram Bot 增强](#3-phase-1pm-telegram-bot-增强)
4. [Phase 2：Hermes MCP for PM](#4-phase-2hermes-mcp-for-pm)
5. [Phase 3：TG → PM → Hermes 全工作流闭环](#5-phase-3tg--pm--hermes-全工作流闭环)
6. [Phase 4：PM 用户同步与管理后台增强](#6-phase-4pm-用户同步与管理后台增强)
7. [文件变更清单](#7-文件变更清单)
8. [部署与配置](#8-部署与配置)
9. [风险与注意事项](#9-风险与注意事项)

---

## 1. 背景与目标

### 1.1 背景

PM 系统目前已具备完整的 Agent 基础设施：

- **框架统一 AccessToken 鉴权** ✅ （2026-06-08 完成）
  - 之前：PM 自有的 `AgentBearerAuthenticationHandler` + `IAgentContext`
  - 现在：框架 `SB.Feature.AccessToken` 统一提供 `[Scope]` + `IApiKeyContext`（JwtAdmin 认证）
  - 优点：与 Admin JWT、MasterKey 共用同一套鉴权框架，权限粒度一致
  - 兼容：原有 `sk_pm_*` 前缀的 ApiKey 通过数据迁移保活，明文不变
- **Agent REST API** ✅ 7 个 Controller 覆盖项目、需求、任务、CI/CD、进度管理五大领域
- **AI Provider 适配器** ✅ `HermesAiProviderAdapter` 已打通 Hermes Gateway 推理链路
- **Telegram Bot 框架** ✅ `SB.Chat` + `SB.Chat.Telegram`，支持 Polling/Webhook 两种模式
- **AiPipeline 管线** ✅ 消息 → AI 分析 → 创建需求的管线已实现
- **CI/CD 流水线** ✅ GitLab 集成、构建审批、发布回滚已实现

> ⚠️ **重要代码变更 (2026-06-08)**：PM 退役了自有的 agent token 栈，迁移到框架统一 `SB.Feature.AccessToken`。
> - 删除了 `Sb.Apps.Pm.{Services,Models}/Agent/` 全部文件（-34 文件 / -2452 行）
> - `/agent/*` 控制器：`[AgentScope]` → `[Scope]`、`AgentBearer` → `JwtAdmin`、`IAgentContext` → `IApiKeyContext`
> - 框架接入：`SB.Framework.*` 0.3.1 → 0.4.0、新增 `SB.Feature.AccessToken` 包
> - 迁移脚本：`docs/access-token/migrate-agent-token.sql`
>
> 本计划的 Hermes Agent 鉴权部分已同步更新。

当前缺口：

- Telegram Bot 仅支持 `/bind` `/verify` `/menu` 等管理命令，不支持需求/任务创建
- Hermes Agent 尚未接入 PM Agent API（MCP Server 未实现）
- 用户 TG 发需求 → PM 创建任务 → Hermes 执行开发 → CI/CD 发布 → TG 通知完成的闭环未打通
- PM Admin 后台用户不能看到 Hermes 创建的来源标记

### 1.2 目标

构建一个完整的闭环工作流：

```
用户在 Telegram 聊天 → PM Bot → PM 系统记录需求/任务
→ Hermes Agent 自动认领 → 读取代码 → 修改代码 → 完成 → 触发 CI/CD → TG 通知
```

所有操作均在 PM 系统留痕，PM Admin 用户可同步查看。

### 1.3 设计原则

1. **一切走 PM 系统** — Hermes 不直接操作 PM 数据，全部通过 Agent API
2. **可审计** — 每一次操作都有 Token 审计日志
3. **可恢复** — 任务有状态机（Pending → Running → Completed/Failed），中断可恢复
4. **PM 在线 = TG Bot 在线** — Telegram Bot 在 PM 进程内运行，无需额外部署

---

## 2. 整体架构

### 2.1 核心架构图

```
┌──────────────────────────────────────────────────────────────────────┐
│                          用户交互层                                    │
│                                                                      │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────────┐       │
│   │  Telegram     │    │  Hermes CLI  │    │  PM Admin Web    │       │
│   │  (聊天窗口)    │    │  (终端)      │    │  (管理后台)       │       │
│   └──────┬───────┘    └──────┬───────┘    └────────┬──────────┘       │
│          │                  │                      │                  │
├──────────┼──────────────────┼──────────────────────┼──────────────────┤
│          ▼                  ▼                      ▼                  │
│  ┌──────────────┐    ┌──────────────┐                                 │
│  │ PM Bot       │    │ Hermes Agent │                                 │
│  │ (Polling)    │    │ (DeepSeek)   │                                 │
│  └──────┬───────┘    └──────┬───────┘                                 │
│         │                   │                                          │
├─────────┼───────────────────┼──────────────────────────────────────────┤
│         ▼                   ▼                                          │
│  ┌────────────────────────────────────────────────────────────┐        │
│  │                  PM 系统 (.NET 10 WebApi)                    │        │
│  │                                                             │        │
│  │  ┌──────────────────┐  ┌─────────────────────────────┐     │        │
│  │  │  消息/命令层      │  │  Agent API 层               │     │        │
│  │  │  - TelegramBot   │  │  - /agent/project/*        │     │        │
│  │  │    CommandHandler│  │  - /agent/requirement/*    │     │        │
│  │  │  - AiPipeline    │  │  - /agent/task/*           │     │        │
│  │  │    Executor      │  │  - /agent/ci/*             │     │        │
│  │  │  - WebhookHandler│  │  - /agent/token/*          │     │        │
│  │  └────────┬─────────┘  └────────────┬────────────────┘     │        │
│  │           │                         │                       │        │
│  │           ▼                         ▼                       │        │
│  │  ┌──────────────────────────────────────────────────┐       │        │
│  │  │               AI 服务层                           │       │        │
│  │  │  IAiService(分析/生成/设计) → AiTaskService       │       │        │
│  │  │  → AiMemoryService → IAiProviderAdapter           │       │        │
│  │  │  (HermesAiProviderAdapter / DeepSeekDirectAdapter)│       │        │
│  │  └──────────────────────────────────────────────────┘       │        │
│  │                                                             │        │
│  │  ┌──────────────────┐  ┌─────────────────────────┐          │        │
│  │  │  业务服务层       │  │  Git/CI/CD 层            │          │        │
│  │  │  ProjectService  │  │  GitRepositoryService   │          │        │
│  │  │  Requirement     │  │  GitPipelineService     │          │        │
│  │  │  Service         │  │  BuildRecordService     │          │        │
│  │  │  TaskItemService │  │  ReleaseService         │          │        │
│  │  └──────────────────┘  └─────────────────────────┘          │        │
│  └────────────────────────────────────────────────────────────┘        │
└──────────────────────────────────────────────────────────────────────┘
```

### 2.2 完整数据流

```
Step  Step  Step  Step   Step    Step   Step   Step   Step
  1     2     3      4       5       6      7      8      9
┌────┐ ┌────┐ ┌────┐ ┌──────┐ ┌──────┐ ┌────┐ ┌────┐ ┌────┐ ┌──────┐
│用户│→│PM  │→│AI  │→│PM 创 │→│Her-  │→│Her- │→│PM   │→│CI/CD│→│PM Bot│
│发   ││Bot ││管线││建需求││mes   ││mes  ││任务 ││触发 ││通知完│
│消息 ││接收││分析││+任务 ││认领  ││改代码││完成 ││构建 ││成    │
└────┘ └────┘ └────┘ └──────┘ └──────┘ └────┘ └────┘ └────┘ └──────┘
```

---

## 3. Phase 1：PM Telegram Bot 增强

### 3.1 目标

在 Telegram Bot 中新增需求/任务创建命令，让用户在 TG 群聊中直接创建和管理需求任务。

### 3.2 新增命令

**文件：** `Sb.Apps.Pm.Services/Chat/TelegramBotCommandHandler.cs`

#### 3.2.1 `/newreq [项目] <需求描述>`

```
/newreq 用户登录页面需要支持微信扫码登录
/newreq 项目A: 后台列表增加导出 Excel 功能
```

流程：
1. 解析参数 → 通过群组绑定定位项目
2. 解析标题和详细描述
3. 调用 `RequirementService.Create()` 创建需求
4. 自动回复创建结果 + 需求链接

#### 3.2.2 `/newtask <需求ID> <任务描述>`

```
/newtask 42 实现微信扫码登录前端页面
/newtask 42 后端对接微信 OAuth API
```

流程：
1. 解析需求 ID 和任务描述
2. 调用 `TaskItemService.Create()` 创建任务并关联需求
3. 自动回复任务创建结果

#### 3.2.3 `/ai <需求描述>`（自然语言 → 需求+任务）

```
/ai 登录页需要加微信扫码，用户点击微信图标后弹出二维码，扫码后自动登录并跳转首页
```

流程：
1. 调 `IAiProviderAdapter` → DeepSeek 分析自然语言
2. AI 返回结构化数据：`{title, description, tasks: [{name, desc, type}]}`
3. 创建 Requirement + 多个关联 TaskItem
4. 回复：`✅ 已创建需求 #42（微信扫码登录），含 3 个任务`

#### 3.2.4 `/mystatus`（查看我的任务和项目进度）

```
/mystatus
→ 项目: 赛博管理系统
  任务: 待办 3 | 进行中 1 | 完成 12
  需求: 进行中 5 / 共 23
  最近发布: v1.2.3 (2026-06-08)
```

#### 3.2.5 菜单增强

在 `/menu` 的 `BuildMenuByRole()` 中增加：

```csharp
// 所有已绑定用户
buttons.Add(new()
{
    new MenuButton("📋 我的需求", "menu:my_requirements"),
    new MenuButton("📊 项目进度", "menu:project_progress")
});
buttons.Add(new()
{
    new MenuButton("🆕 最近更新", "menu:recent_updates"),
    new MenuButton("📝 我的任务", "menu:my_tasks")  // 移动到通用层
});
buttons.Add(new()
{
    new MenuButton("➕ 提需求", "menu:new_requirement"),  // 新增
    new MenuButton("🤖 AI 需求", "menu:ai_requirement")   // 新增
});
```

### 3.3 自动消息处理（非命令消息 → AI 分析）

**文件：** `Sb.Apps.Pm.Services/Chat/TelegramWebhookHandler.cs`

在 `SaveMessage` 方法末尾增加：

```csharp
// 自动 AI 分析：非命令、AI 开启、内容有意义（>20 字）
if (groupBinding.AiEnabled
    && groupBinding.AutoCreateRequirement
    && !message.Content.TrimStart().StartsWith("/")
    && message.Content.Length > 20)
{
    _ = AutoAnalyzeAndCreateRequirement(saved, groupBinding);
}
```

新增 `AutoAnalyzeAndCreateRequirement` 方法：
1. 调 IAiProviderAdapter → 分析消息内容
2. AI 判断是否为有效需求（置信度 > 0.7）
3. 是 → 自动创建 Requirement（状态 Draft）
4. 调 `NotifyTaskCreated` → 通知群组："🤖 AI 建议创建需求 #43：xxx，点击查看"

### 3.4 新增模型字段（可选）

**文件：** `Sb.Apps.Pm.Models/Chat/ChatGroupBinding.cs`

```csharp
/// <summary>是否自动从聊天消息创建需求</summary>
[Column(Name = "AutoCreateRequirement")]
public bool AutoCreateRequirement { get; set; } = false;
```

---

## 4. Phase 2：Hermes MCP for PM

### 4.1 目标

让 Hermes Agent 能通过 PM 的 Agent API 创建、查看、更新项目/需求/任务/CI/CD。

### 4.2 方案选择：MCP Server

Hermes 原生支持 MCP（Model Context Protocol）客户端。创建一个 MCP Server 封装 PM Agent API，以工具形式暴露给 Hermes。

### 4.3 MCP Server 设计

**文件：** `pm-mcp-server/server.py`（独立服务，不放入 PM 仓库）

#### 暴露的工具列表

| MCP Tool 名称 | 对应的 PM API | 说明 |
|---------------|--------------|------|
| `pm_get_project` | GET `/agent/project/Detail?id=N` | 获取项目详情 |
| `pm_list_projects` | GET `/agent/project/List` | 列出所有项目 |
| `pm_list_requirements` | GET `/agent/requirement/List?projectId=N` | 列出需求 |
| `pm_create_requirement` | POST `/agent/requirement/Create` | 创建需求 |
| `pm_create_task` | POST `/agent/task/Create` | 在需求下创建任务 |
| `pm_list_tasks` | GET `/agent/task/List?projectId=N` | 列出任务 |
| `pm_claim_task` | POST `/agent/task/Claim` | 认领任务 |
| `pm_complete_task` | POST `/agent/task/Complete` | 完成任务（附变更描述） |
| `pm_get_repo_info` | GET `/agent/project/Detail?id=N` | 获取仓库信息 |
| `pm_trigger_cicd` | POST `/agent/ci/Trigger` | 触发流水线 |
| `pm_get_cicd_status` | GET `/agent/ci/Status?buildId=N` | 查询构建状态 |
| `pm_get_agent_memory` | GET `/agent/memory/List?projectId=N` | 获取 AI 记忆 |

#### MCP Server 代码框架

```python
# server.py — 核心骨架
import httpx, json
from typing import Any

PM_API_BASE = "https://pm.saibo.club/agent"
ACCESS_KEY = "sk_pm_***"  # PM Admin → 系统 → AccessToken 管理创建

async def call_pm_api(path: str, params: dict = None, data: dict = None) -> dict:
    """调用 PM Agent API。鉴权使用框架统一 AccessToken（JwtAdmin + Scope）。"""
    headers = {
        "Authorization": f"Bearer {ACCESS_KEY}",
        "Content-Type": "application/x-www-form-urlencoded"  # [FromForm] 绑定
    }
    async with httpx.AsyncClient(base_url=PM_API_BASE, headers=headers) as client:
        if data:
            resp = await client.post(f"/{path}", data=data)
        else:
            resp = await client.get(f"/{path}", params=params)
        return resp.json()

# 每个工具一个函数 + MCP 协议注册
TOOLS = [/*...每个工具的 schema...*/]

def handle_tool_call(name: str, args: dict) -> Any:
    handlers = {
        "pm_create_requirement": lambda a: call_pm_api("requirement/Create", data=a),
        "pm_create_task":          lambda a: call_pm_api("task/Create", data=a),
        "pm_claim_task":           lambda a: call_pm_api("task/Claim", data=a),
        "pm_complete_task":        lambda a: call_pm_api("task/Complete", data=a),
        "pm_trigger_cicd":         lambda a: call_pm_api("ci/Trigger", data=a),
        # ...
    }
    return handlers[name](args)

# MCP stdio 循环：初始化 → 注册工具 → 等待调用 → 返回结果
```

### 4.4 Hermes 使用示例

在 Hermes CLI 中：

```
> 用户小李在 TG 群里说登录页需要加微信扫码，帮我处理一下
```

Hermes 将自动执行：

1. `pm_list_projects` → 找到项目
2. `pm_list_requirements` → 查重，无冲突
3. `pm_create_requirement` → 创建需求
4. `pm_create_task` → 创建开发任务（3 个：前端 UI、后端 OAuth、联调）
5. 读取本地代码 → 修改代码 → commit
6. `pm_complete_task` → 完成任务，附 commit 信息
7. `pm_trigger_cicd` → 触发构建发布

**整个过程全部在 PM 系统里有完整记录。**

### 4.5 注册到 Hermes

```bash
hermes mcp add pm-server \
  --command "python3 ~/hermes-tools/pm-mcp-server/server.py"
hermes mcp test pm-server  # 验证连通
```

---

## 5. Phase 3：TG → PM → Hermes 全工作流闭环

### 5.1 目标

用户在 TG 发消息 → PM Bot 处理 → 创建需求/任务 → Hermes 自动认领执行 → 完成后 TG 通知。

### 5.2 任务推送机制（PM → Hermes）

#### 方案 A：Webhook 推送（推荐，实时）

**PM 侧：** `TaskItemService.Create()` 末尾增加通知：

```csharp
// Sb.Apps.Pm.Services/Tasks/TaskItemService.cs
public override Result Create(TaskItem task)
{
    var result = base.Create(task);
    if (task.AutoAssign && task.ExecStatus == TaskItemExecStatus.Idle)
    {
        _ = NotifyHermesAsync(task);
    }
    return result;
}

private async Task NotifyHermesAsync(TaskItem task)
{
    // 从系统设置读取 hermes_webhook_url
    var config = _fsql.Select<SystemSetting>()
        .Where(s => s.Key == "hermes_webhook_url").First();
    if (string.IsNullOrEmpty(config?.Value)) return;

    var payload = JsonSerializer.Serialize(new
    {
        type = "new_task",
        task_id = task.Id,
        title = task.Title,
        project_id = task.ProjectId,
        requirement_id = task.RequirementId
    });
    await _httpClient.PostAsync(config.Value,
        new StringContent(payload, Encoding.UTF8, "application/json"));
}
```

**Hermes 侧：** 注册 Webhook

```bash
hermes webhook subscribe pm-task-updates
```

Webhook 处理逻辑：
1. 接收 PM 推送的 `new_task` 事件
2. 调 PM Agent API 读取任务详情
3. 调 PM Agent API 认领任务（`/agent/task/Claim`）
4. 执行开发工作（读取代码 → 修改 → commit）
5. 调 PM Agent API 完成任务（`/agent/task/Complete`）
6. 调 PM Agent API 触发 CI/CD（`/agent/ci/Trigger`）

#### 方案 B：Cron 轮询（简单可靠，无 Webhook 依赖）

```bash
hermes cron create "every 5 minutes" \
  --name "PM Task Worker" \
  --prompt "访问 PM Agent API /agent/task/Available，如果有待处理任务则按以下步骤处理..."

hermes cron create "every 10 minutes" \
  --name "PM CI/CD Status Check" \
  --prompt "检查 PM 所有进行中的构建状态，完成后通知 TG 群"
```

### 5.3 完成通知（PM → TG）

在 `TaskItemService.ReportSuccess()` 和 `RequirementService.Create()` 中：

```csharp
private async Task NotifyGroupAsync(string chatId, string message)
{
    var bot = _connectorManager.GetBot(primaryAccountId);
    if (bot != null) await bot.SendTextAsync(chatId, message);
}
```

TG 通知示例：

```
✅ 任务 #88 已完成
📋 需求: #42 微信扫码登录
📝 变更: 新增 WeChatOAuthController.cs + LoginWithWeChat.vue
🔗 https://pm.saibo.club/requirement/42
🔄 CI/CD 已自动触发
```

### 5.4 状态机

```
[用户发 TG 消息]
    │
    ▼
[需求：Draft] ──→ [需求：Todo] ──→ [需求：InProgress]
                                        │
                                        ▼
                                  [任务创建：Idle]
                                        │
                           Hermes ──→ [认领：Claimed]
                                        │
                                      [执行：Running]
                                        │
                          ┌──────────────┼──────────────┐
                          ▼              ▼              ▼
                    [成功：Success]  [失败：Failed]  [暂停：Hibernated]
                          │              │
                          ▼              ▼
                  [需求：Done]     [需求：Todo(重试)]
                          │
                          ▼
                    [CI/CD 触发]
                          │
                    [构建成功 → 发布]
                          │
                          ▼
                    [TG 通知完成]
```

---

## 6. Phase 4：PM 用户同步与管理后台增强

### 6.1 目标

所有通过 TG 和 Hermes 产生的数据，PM Admin 后台用户能实时看到、追溯来源。

### 6.2 新增字段

**文件：** `Sb.Apps.Pm.Models/Tasks/TaskItem.cs`

```csharp
/// <summary>任务来源: manual/tg_bot/hermes/ai_auto</summary>
[Column(Name = "Source", StringLength = 20)]
public string? Source { get; set; }

/// <summary>来源引用（TG 消息ID / Webhook 来源）</summary>
[Column(Name = "SourceRef", StringLength = 500)]
public string? SourceRef { get; set; }
```

**文件：** `Sb.Apps.Pm.Models/Requirements/Requirement.cs`

```csharp
/// <summary>需求来源: manual/tg_bot/hermes/ai_auto</summary>
[Column(Name = "Source", StringLength = 20)]
public string? Source { get; set; }
```

### 6.3 Admin 前端增强

在需求/任务列表页：

- **来源过滤器**：全部 / 手动创建 / TG Bot / Hermes AI / AI 自动
- **来源标签**：列表行显示标签（如 `🤖 Hermes`、`📱 TG`、`✨ AI`）
- **详情页**：显示来源信息和关联的 TG 原始消息

### 6.4 无需额外开发的已有能力

| 能力 | 状态 |
|------|------|
| PM Admin 登录查看 | ✅ 已有 |
| 需求列表/详情/CRUD | ✅ 已有 |
| 任务列表/详情/CRUD | ✅ 已有 |
| CI/CD 流水线记录 | ✅ 已有 |
| AI 任务执行记录 | ✅ 已有 |
| 聊天消息记录 | ✅ 已有 |

---

## 7. 文件变更清单

### 7.1 Phase 1：PM Telegram Bot 增强

| 文件 | 变更类型 | 变更量 |
|------|----------|--------|
| `Sb.Apps.Pm.Services/Chat/TelegramBotCommandHandler.cs` | 修改 | +120 行 |
| `Sb.Apps.Pm.Services/Chat/TelegramWebhookHandler.cs` | 修改 | +40 行 |
| `Sb.Apps.Pm.Models/Chat/ChatGroupBinding.cs` | 修改（可选） | +3 行 |

变更内容：

1. **TelegramBotCommandHandler.cs** — 新增 4 个命令处理：
   - `HandleNewRequirement` — `/newreq` 命令
   - `HandleNewTask` — `/newtask` 命令
   - `HandleAiRequirement` — `/ai` 命令（AI 自然语言→需求+任务）
   - `HandleMyStatus` — `/mystatus` 命令（任务/项目概览）
   - 修改 `BuildMenuByRole` — 新增菜单项
   - 修改 `HandleMenuCallback` — 新增菜单回调分支

2. **TelegramWebhookHandler.cs** — 新增：
   - `AutoAnalyzeAndCreateRequirement` — 非命令消息自动 AI 分析
   - `NotifyTaskCreated` — 完成通知 TG 群

### 7.2 Phase 2：Hermes MCP for PM

| 文件 | 变更类型 | 变更量 |
|------|----------|--------|
| `~/hermes-tools/pm-mcp-server/server.py` | 新建 | ~180 行 |
| `~/.hermes/config.yaml` via `hermes mcp add` | 配置 | +1 MCP Server 注册 |

### 7.3 Phase 3：TG → PM → Hermes 闭环

| 文件 | 变更类型 | 变更量 |
|------|----------|--------|
| `Sb.Apps.Pm.Services/Tasks/TaskItemService.cs` | 修改 | +30 行 |
| `Sb.Apps.Pm.Services/Chat/TelegramBotCommandHandler.cs` | 修改 | +15 行 |
| Hermes Webhook/Cron 配置 | 配置 | 1 webhook / 2 cron jobs |

### 7.4 Phase 4：PM 用户同步（可选）

| 文件 | 变更类型 | 变更量 |
|------|----------|--------|
| `Sb.Apps.Pm.Models/Tasks/TaskItem.cs` | 修改 | +2 字段 |
| `Sb.Apps.Pm.Models/Requirements/Requirement.cs` | 修改 | +1 字段 |
| Admin 前端 `apps/web-antd/src/views/task-item/list/` | 修改 | 过滤器 + 标签 |

### 7.5 不变更的文件

以下文件已经成熟，本计划不涉及修改：

- `Sb.Apps.Pm.Services/Ai/*` — AI Provider 体系已完善
- `Sb.Apps.Pm.Services/Pipelines/*` — CI/CD 体系已完善
- `Sb.Apps.Pm.WebApi/Program.cs` — 启动配置无需修改
- `Sb.Apps.Pm.WebApi/Controllers/Agent/*` — Agent API 已完成（鉴权已切框架统一 AccessToken）
- `Sb.Apps.Pm.Models/Chat/*` — 聊天模型已完成

> ℹ️ **注意**：PM 自有 agent token 栈（`Sb.Apps.Pm.{Services,Models}/Agent/`）已于 2026-06-08 迁移到框架
> `SB.Feature.AccessToken`。本计划不涉及 agent token 相关文件的修改。

---

## 8. 部署与配置

### 8.1 环境要求

| 组件 | 要求 |
|------|------|
| PM 系统 | 运行中（含 Agent API） |
| Hermes Agent | 已安装，DeepSeek 已配置 |
| Telegram Bot Token | 从 @BotFather 获取 |
| ApiKey (AccessToken) | PM Admin → 系统 → AccessToken 管理创建 |
| 网络 | Hermes 能访问 PM API（同一内网或公网） |

### 8.2 部署步骤

#### Step 1：PM 侧 Telegram Bot 配置

```bash
# PM 代码已含 Polling 模式，启动后自动扫描 Active 状态的 Bot 账号
# PM Admin 后台：系统设置 → 聊天账号管理 → 添加 Telegram Bot
# 填入 Bot Token + Bot 名称 → 状态设置为 Active
```

#### Step 2：创建 AccessToken（ApiKey）

```bash
# PM Admin → 系统 → AccessToken 管理（框架统一入口）
# 创建 ApiKey（SK = Secret Key）：
#   - 名称: hermes-mcp
#   - Scope: read:project,write:requirement,read:requirement,write:task,read:task,write:ci,read:ci
#   - 速率限制: 60 次/分（框架统一节流）
#   - 过期时间: 按需设置（建议 90 天轮换）
# 复制返回的明文 AccessKey（一次性，格式 sk_pm_xxx）
#
# 注意：PM 已退役自有 AgentToken 栈，现在使用框架 SB.Feature.AccessToken。
# 原有 sk_pm_* 前缀的 ApiKey 通过 SQL 迁移脚本（docs/access-token/migrate-agent-token.sql）保活，明文不变。
# 新建的 AccessToken 在 PM Admin → 系统 → AccessToken 管理 管理，不再是独立的 AgentToken 页面。
```

#### Step 3：部署 MCP Server

```bash
mkdir -p ~/hermes-tools/pm-mcp-server
# 创建 server.py
hermes mcp add pm-server --command "python3 ~/hermes-tools/pm-mcp-server/server.py"
hermes mcp test pm-server
```

#### Step 4：配置 Webhook / Cron

```bash
# Webhook 方式（推荐）
hermes webhook subscribe pm-task-updates

# 或者 Cron 方式（备用）
hermes cron create "every 5 minutes" --name "PM Task Worker" --prompt "..."
```

#### Step 5：配置 PM 系统设置

```bash
# PM Admin → 系统设置 → 全局配置
# 新增: hermes_webhook_url = https://hermes.local/webhooks/pm-task-updates
# （或 Hermes Gateway 的外网可达 URL）
```

### 8.3 验证 checklist

- [ ] TG Bot 启动，/start 正常回复
- [ ] 群组绑定 `/bind` 正常
- [ ] `/newreq` 创建需求成功，PM Admin 后台可见
- [ ] `/ai` 自然语言→需求+任务 正常
- [ ] MCP Server `hermes mcp test` 通过
- [ ] Hermes 调 `pm_create_requirement` 成功
- [ ] Hermes 调 `pm_claim_task` + `pm_complete_task` 正常
- [ ] Webhook 触发 Hermes 自动处理任务
- [ ] CI/CD 触发后 TG 收到完成通知

---

## 9. 风险与注意事项

### 9.1 风险清单

| 风险 | 级别 | 缓解措施 |
|------|------|----------|
| Hermes 写代码可能破坏本地仓库 | 🔴 高 | `git worktree` 隔离；使用独立 `ai/*` 分支 |
| AccessToken 泄露 | 🔴 高 | 框架统一节流（60次/分）+ 审计日志 + 90天轮换 |
| Cron 任务超时（>3min） | 🟡 中 | 复杂任务拆分为多个子任务；设置足够间隔 |
| Hermes 和人工同时改同一代码 | 🟡 中 | 任务认领机制保证串行；同一任务同一时间只有一人认领 |
| TG Bot Token 泄露 | 🔴 高 | PM 已有 `ConnectionConfig` 加密存储 |
| Hermes 执行中被中断 | 🟡 中 | 任务保持 `Running` 状态；Webhook 通知可重试 |
| Hermes Gateway 不可用 | 🟡 中 | PM 的 AiProvider 可直连 DeepSeek API（不经过 Hermes） |

### 9.2 设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| Hermes 调用 PM 的方式 | REST API（MCP Server 封装） | 已有 Agent API，不引入新协议 |
| PM 调用 AI 的方式 | IAiProviderAdapter → DeepSeek/Hermes | 已有适配器，可配置 |
| 消息推送方式 | Webhook（主） + Cron（备） | Webhook 实时，Cron 兜底 |
| Telelgram Bot 模式 | Polling（默认）或 Webhook | PM 已支持两种模式 |
| Task 状态机 | Idle → Claimed → Running → Success/Failed | 已有状态 + 新增 Claimed |

### 9.3 性能注意事项

- Telegram Polling 每 30 秒心跳一次，多 Bot 在同一进程，开销极小
- MCP Server 通过 stdio 通信，仅调用时占用资源
- Cron 轮询建议 5 分钟以上，避免 PM 数据库压力
- AI 调用 DeepSeek，生成代码时 `max_tokens` 可设为 8192

---

## 附录 A：相关文档索引

| 文档 | 说明 |
|------|------|
| [AI_HermesIntegration_Plan](AI_HermesIntegration_Plan.md) | Stage 0-4：AgentToken + REST API + AiProvider 集成 |
| [AI_AgentToken_Enhancement_Plan](AI_AgentToken_Enhancement_Plan.md) | AgentToken 体系设计与实现 |
| [AI_AgentToken_UI_Plan](AI_AgentToken_UI_Plan.md) | AgentToken 管理 UI 设计 |
| [SB.Chat 框架](../../framework/sb-framework-chat/README.md) | 聊天抽象框架文档 |

## 附录 B：Agent API 端点速查

| 方法 | 端点 | 本计划使用 |
|------|------|-----------|
| GET | `/agent/project/List` | Phase 2 |
| GET | `/agent/project/Detail?id=N` | Phase 2 |
| GET | `/agent/requirement/List` | Phase 2 |
| GET | `/agent/requirement/Detail?id=N` | Phase 2 |
| POST | `/agent/requirement/Create` | Phase 1, 2 |
| POST | `/agent/requirement/UpdateStatus` | Phase 2 |
| GET | `/agent/task/List` | Phase 2 |
| GET | `/agent/task/Detail?id=N` | Phase 2 |
| GET | `/agent/task/Available` | Phase 2, 3 |
| POST | `/agent/task/Claim` | Phase 2, 3 |
| POST | `/agent/task/Complete` | Phase 2, 3 |
| POST | `/agent/ci/Trigger` | Phase 2, 3 |
| GET | `/agent/ci/Status?buildId=N` | Phase 2, 3 |

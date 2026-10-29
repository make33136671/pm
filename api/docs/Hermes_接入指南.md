# Hermes 接入指南

> **版本**: v1.1  
> **日期**: 2026-06-10  
> **适用人员**: 在 Hermes 侧配置 PM 对接的操作者  
> **当前状态**: Stage 0-3 已完成；§3（token 生成）/§4（API 调用）已于 2026-06-10 在生产 PM（框架 0.4.3 + AccessToken 0.1.4）**逐条实测**校正

---

## 目录

1. [架构概览](#1-架构概览)
2. [前置条件](#2-前置条件)
3. [Hermes → PM：鉴权配置](#3-hermes--pm鉴权配置)
4. [Hermes → PM：Agent API 调用](#4-hermes--pmagent-api-调用)
5. [PM → Hermes：AI Provider 配置](#5-pm--hermesai-provider-配置)
6. [MCP Server 接入（全工作流闭环）](#6-mcp-server-接入全工作流闭环)
7. [Webhook 任务推送](#7-webhook-任务推送)
8. [验证 Checklist](#8-验证-checklist)
9. [常见问题](#9-常见问题)

---

## 1. 架构概览

Hermes 与 PM 的集成分两个方向：

```
┌──────────────────────────────────────────────────────────┐
│                    双向通信                               │
│                                                          │
│  Hermes Agent  ──[AccessToken + REST]──►  PM WebApi      │
│  (查询/创建/执行)    Hermes → PM           (记录/调度)    │
│                                                          │
│  PM WebApi  ──[OpenAI-compatible API]──►  Hermes Gateway │
│  (AI 推理请求)       PM → Hermes           (LLM 推理)    │
└──────────────────────────────────────────────────────────┘
```

| 方向 | 协议 | 鉴权 | 状态 |
|------|------|------|------|
| Hermes → PM | REST / `[FromForm]` | Bearer AccessToken（`sk_` + 64 位 hex） | ✅ 已打通（2026-06-10 实测） |
| PM → Hermes | OpenAI Chat Completions | Bearer ApiKey（可选） | ✅ Adapter 已实现 |

---

## 2. 前置条件

| 组件 | 要求 |
|------|------|
| PM 系统 | 运行中，框架 ≥ 0.4.3 + `SB.Feature.AccessToken` ≥ 0.1.4（2026-06-10 实测版本） |
| Hermes Agent | 已安装，DeepSeek 已配置 |
| 网络 | Hermes 能访问 `http://192.168.10.73:6020`（内网）或公网地址 |
| Python | 3.10+（MCP Server / Webhook 接收方用） |

---

## 3. Hermes → PM：鉴权配置

### 3.1 生成 AccessToken（实测流程）

> ✅ 本节命令 **2026-06-10 在生产 PM（容器 `sbpm` / `/opt/api`，框架 0.4.3 + AccessToken 0.1.4）实际跑通**。token 由「① 管理员登录拿 JWT → ② 调签发接口」两步生成；**明文 token 只在签发响应里返回一次**，当场存好，丢了只能吊销重发。

**① 管理员登录拿 Admin JWT**

```bash
PM=http://127.0.0.1:6020          # 容器内直连 Kestrel；外部经 nginx 走 http://<PM_HOST>（80 端口，无需 :6020）
JWT=$(curl -s -X POST "$PM/admin/Auth/Login" \
  -d username=admin -d password='******' -d totpCode= \
  | jq -r .data.accessToken)
```

> 端点 `POST /admin/Auth/Login`，表单字段 `username` / `password` / `totpCode`（未开 2FA 传空）。成功返回 `data.accessToken`（HS256 JWT）。签发 key 仅后台账号可做（FullAccess 还要求 super 角色）。

**② 调签发接口 `POST /admin/AccessToken/Create`**（表单编码，带 ① 的 JWT）

```bash
SCOPES="read:project,read:requirement,write:requirement,read:task,write:task,read:ci,write:ci,read:progress"
curl -s -X POST "$PM/admin/AccessToken/Create" \
  -H "Authorization: Bearer $JWT" \
  --data-urlencode "name=hermes-agent" \
  --data-urlencode "serviceAccount=hermes-agent" \
  --data-urlencode "scopes=$SCOPES" \
  --data-urlencode "rateLimitPerMinute=60" \
  --data-urlencode "expiresAt=2027-06-10"          # 受限 key 可省；建议 365 天轮换
```

实测响应（`token` 即明文，仅此一次）：

```json
{
  "code": 0, "message": "ok",
  "data": {
    "id": 13,
    "name": "hermes-agent",
    "token": "sk_8833c591c8b19481753fc22dcb121da3……（sk_ + 64 位 hex，共 67 字符）",
    "tokenPrefix": "sk_8833c591c",
    "accountId": 5,
    "scopes": "read:project,read:requirement,…",
    "fullAccess": false,
    "rateLimitPerMinute": 60
  }
}
```

> **token 格式**：`sk_` + 64 位小写十六进制。鉴权层只接管 `sk_` 前缀的 Bearer（前缀可经 `Features:AccessToken:KeyPrefix` 改，当前生产为默认 `sk_` —— 旧文档写的 `sk_pm_` 已不准）。库里只存 `sha256(明文)`。

**签发参数（实测自 `AccessTokenController.Create`）**

| 参数 | 必填 | 说明 |
|------|------|------|
| `name` | ✓ | key 名称（审计可见） |
| `accountId` **或** `serviceAccount` | 二选一 | 绑定已有账号 id；或给服务账号名自动创建（继承签发者角色，不越权） |
| `scopes` | — | 逗号分隔；不填 = 无 scope，受限 key 将访问不了任何 `[Scope]` 端点 |
| `fullAccess` | — | `true`=god key，绕过全部 scope。**仅 super 可签发**，要求 `Features:AccessToken:EnableFullAccess=true`（生产已开）+ **必须设 `expiresAt`**（≤ 90 天） |
| `expiresAt` | — | 过期时间（如 `2027-06-10`）；FullAccess 必填 |
| `rateLimitPerMinute` | — | 单 key 每分钟上限 |
| `ipAllowlist` | — | 逗号分隔 IP / 前缀网段（如 `192.168.10.`） |

**当前在用 token（hermes-agent-macbook，FullAccess，到期 2026-06-30）：**

```
sk_48c02cac2eaa8ce8260af15e880a07a37bff97aadfaa3d1008228fa307c5da05
```

> id=16，绑定账号 2，FullAccess（绕过全部 scope）。过期后 `POST /admin/AccessToken/Revoke` + 重新签发轮换。

存进 Hermes 侧并配置 base：

```bash
export PM_AGENT_KEY="sk_xxxxxxxx……"
export PM_API_BASE="http://<PM_HOST>/agent"          # 经 nginx 80 端口；或 http://<PM_HOST>:6020/agent 直连 Kestrel
```

吊销 / 轮换：`POST /admin/AccessToken/Revoke`（form `id`、`reason`）即时失效；查列表 `GET /admin/AccessToken/List`。轮换 = 新签发 + 吊销旧的。

### 3.2 调用规范（实测）

| 要点 | 说明 |
|------|------|
| 认证头 | `Authorization: Bearer sk_...` |
| POST 编码 | **表单**（`application/x-www-form-urlencoded`），所有 `/agent` POST 端点为 `[FromForm]`，**不是 JSON** |
| 成功响应 | `{"code":0,"message":"ok","data":...}` —— 字段是 `message`（非 `msg`）；列表类 `data` 形如 `{"items":[...],"total":N}` |
| 无凭据 / key 无效·过期·吊销 | **HTTP 401**（空响应体，`[Authorize]` 挑战；注意不是 403） |
| key 有效但缺 scope | **HTTP 403** + `{"code":-1,"message":"缺少 scope 权限: <scope>"}` |
| FullAccess(god) key | 绕过 scope 闸门，可访问全部 `/agent` 端点 |
| 防爆破 | 同 IP 无效 key 尝试超 10 次/5 分钟 → 临时拉黑 15 分钟 |

实测联通（持有 `read:project` 的 key）：

```bash
curl -s -H "Authorization: Bearer $PM_AGENT_KEY" \
  "$PM_API_BASE/project/List?pageSize=2" | jq '.code'   # → 0
```

---

## 4. Hermes → PM：Agent API 调用

### 4.1 端点速查

#### 项目（Scope: `read:project`）

| 端点 | 方法 | 说明 |
|------|------|------|
| `/agent/project/List` | GET | 项目列表，支持 `keyword`、`status`、`page`/`pageSize` |
| `/agent/project/Detail?id=N` | GET | 项目详情 |
| `/agent/project/SubList?projectId=N` | GET | 子项目列表（含 Git 仓库信息） |

#### 需求（Scope: `read:requirement` / `write:requirement`）

| 端点 | 方法 | 说明 |
|------|------|------|
| `/agent/requirement/List` | GET | 需求列表，支持 `projectId`、`status`、`type`、`keyword`、`page` |
| `/agent/requirement/Detail?id=N` | GET | 需求详情（含关联任务） |
| `/agent/requirement/Create` | POST | 创建需求（`title`、`projectId`、`content`/`description`、`type`、`priority` 等，`[FromForm] Requirement`） |
| `/agent/requirement/Update` | POST | 编辑需求（`[FromForm] Requirement`，含 `id`） |
| `/agent/requirement/Delete` | POST | 删除需求（`id`） |
| `/agent/requirement/UpdateStatus` | POST | 更新需求状态（`id`、`status`） |
| `/agent/requirement/AddComment` | POST | 添加评论（`requirementId`、`content`） |

> 写操作（Create/Update/Delete/UpdateStatus/AddComment）需 `write:requirement`；List/Detail 需 `read:requirement`。

#### 任务（Scope: `read:task` / `write:task`）

| 端点 | 方法 | 说明 |
|------|------|------|
| `/agent/task/List` | GET | 任务列表，支持 `projectId`、`requirementId`、`claimStatus`、`execStatus` |
| `/agent/task/Detail?id=N` | GET | 任务详情 |
| `/agent/task/Available` | GET | 可认领任务列表（`projectId` 可选） |
| `/agent/task/Logs?taskItemId=N` | GET | 任务执行日志 |
| `/agent/task/Claim` | POST | 认领下一个可执行任务（`projectId` 可选） |
| `/agent/task/Start` | POST | 标记开始执行（`id`） |
| `/agent/task/Complete` | POST | 完成任务（`id`、`message` 附注可选） |
| `/agent/task/Fail` | POST | 报告失败（`id`、`message` 必填） |
| `/agent/task/Cancel` | POST | 取消任务（`id`） |
| `/agent/task/Resume` | POST | 恢复任务（`id`） |

#### CI/CD（Scope: `read:ci` / `write:ci`）

| 端点 | 方法 | 说明 |
|------|------|------|
| `/agent/ci/Trigger` | POST | 触发流水线（`profileId`、`branch`、`version`、`autoApprove=false` **必须**） |
| `/agent/ci/Status?buildId=N` | GET | 构建状态 |
| `/agent/ci/Jobs?buildId=N` | GET | Job 列表及状态 |
| `/agent/ci/JobTrace?buildId=N&jobId=N` | GET | Job 控制台日志 |
| `/agent/ci/TestReport?buildId=N` | GET | JUnit 测试报告 |
| `/agent/ci/Link` | POST | 将构建关联到需求/任务（`buildId`、`requirementId`、`taskItemId`） |

#### 进度/健康（Scope: `read:progress`）

| 端点 | 方法 | 说明 |
|------|------|------|
| `/agent/progress/HealthScore?projectId=N` | GET | 项目健康度评分 |
| `/agent/progress/Builds` | GET | 构建记录列表 |
| `/agent/progress/Releases` | GET | 发布记录列表 |
| `/agent/progress/Changelog` | GET | 变更日志 |

### 4.2 典型调用示例（Python）

```python
import httpx, os

_client = httpx.Client(
    base_url=os.environ["PM_API_BASE"],   # http://<PM_HOST>/agent（nginx 80）或 …:6020/agent
    headers={"Authorization": f"Bearer {os.environ['PM_AGENT_KEY']}"},
    timeout=30,
)

def _check(r: httpx.Response) -> dict:
    # 401=凭据无效/缺失，403=缺 scope（body 带 "缺少 scope 权限: xxx"）
    r.raise_for_status()
    d = r.json()
    if d.get("code") != 0:
        raise RuntimeError(f"PM 业务错误: {d.get('message')}")
    return d["data"]

def pm_get(path: str, **params) -> dict:
    return _check(_client.get(f"/{path}", params={k: v for k, v in params.items() if v is not None}))

def pm_post(path: str, **form) -> dict:
    return _check(_client.post(f"/{path}", data={k: v for k, v in form.items() if v is not None}))

# 查询活跃项目（列表类 data 形如 {"items":[...], "total":N}）
projects = pm_get("project/List", status=1)["items"]

# 查询某项目的 Pending 需求
reqs = pm_get("requirement/List", projectId=3, status="Pending", pageSize=50)["items"]

# 认领任务（无可领时 data 为 null，需判空）
claimed = pm_post("task/Claim", projectId=3)
if claimed:
    task_id = claimed["id"]
    pm_post("task/Start", id=task_id)                  # 开始执行
    pm_post("task/Complete", id=task_id, message="AI 完成，已开 MR: https://gitlab/...")

# 触发 CI（强制 autoApprove=false，走人工审批）
pm_post("ci/Trigger", profileId=5, branch="ai/123", autoApprove="false")
```

### 4.3 安全要求

> **⚠️ 重要**：CI 触发必须设置 `autoApprove=false`，所有 AI 产出代码必须经过 PM 构建审批后才能发布，严禁 Hermes 自动发布到生产。

---

## 5. PM → Hermes：AI Provider 配置

PM 已实现 `HermesAiProviderAdapter`，通过 Hermes Gateway 的 OpenAI-compatible API 进行推理。

### 5.1 配置 appsettings.json

```json
{
  "Hermes": {
    "GatewayUrl": "http://<hermes-gateway-host>:<port>",
    "ApiKey": "<gateway-api-key-如果有鉴权>",
    "DefaultModel": "qwen3.6-27b"
  }
}
```

> **内网部署**：若 Hermes Gateway 与 PM 在同一内网，`ApiKey` 可留空（无需鉴权）。

### 5.2 在 PM 数据库启用 Hermes Provider

PM 首次启动时会自动通过 `SeedData` 插入 `ProviderType=hermes` 的记录（默认 `Status=Disabled`）。Gateway 就绪后在 Admin UI 启用：

```
PM Admin → AI → Provider 管理 → 找到 "Hermes Agent" → 编辑
  Endpoint:    http://<gateway-host>:<port>/v1
  ModelName:   qwen3.6-27b（以实际 Gateway 暴露的模型名为准）
  ApiKey:      见 §5.1
  ExtraConfig: {"systemPrompt":"你是 PM 项目智能助手","maxTokens":4096,"temperature":0.3}
  Status:      启用
```

### 5.3 验证 AI 推理链路

```bash
# 调用 PM Admin Chat 接口（需 Admin JWT）
curl -X POST http://192.168.10.73:6020/admin/Ai/Chat \
  -H "Authorization: Bearer <admin-jwt>" \
  -H "Content-Type: application/json" \
  -d '{"message": "ping，请简单回复"}'
# 期望: code=0，data 含 AI 回复内容
```

### 5.4 Gateway 协议要求

Hermes Gateway 必须兼容 OpenAI Chat Completions 协议：

**同步请求：** `POST /v1/chat/completions`
```json
{
  "model": "qwen3.6-27b",
  "messages": [{"role": "user", "content": "..."}],
  "max_tokens": 4096,
  "temperature": 0.3
}
```
期望响应：`{"choices": [{"message": {"content": "..."}}]}`

**流式请求：** 追加 `"stream": true`，响应为 SSE 格式：
```
data: {"choices":[{"delta":{"content":"..."}}]}
data: [DONE]
```

---

## 6. MCP Server 接入（全工作流闭环）

让 Hermes CLI 直接通过 MCP 工具操作 PM，实现自然语言 → 代码变更 → PM 记录的完整闭环。

### 6.1 创建 MCP Server

```bash
mkdir -p ~/hermes-tools/pm-mcp-server
```

创建 `~/hermes-tools/pm-mcp-server/server.py`（参考 `api/docs/hermes-worker-reference.py` 扩展为 MCP Server 格式）：

```python
import httpx, os, asyncio
from typing import Any

PM_API_BASE = os.environ["PM_API_BASE"].rstrip("/")   # http://192.168.10.73:6020/agent
ACCESS_KEY = os.environ["PM_AGENT_KEY"]

_client = httpx.AsyncClient(
    base_url=PM_API_BASE,
    headers={"Authorization": f"Bearer {ACCESS_KEY}"},
    timeout=30,
)

async def pm_get(path: str, **params) -> dict:
    r = await _client.get(f"/{path}", params={k: v for k, v in params.items() if v is not None})
    r.raise_for_status(); return r.json()

async def pm_post(path: str, **form) -> dict:
    r = await _client.post(f"/{path}", data={k: v for k, v in form.items() if v is not None})
    r.raise_for_status(); return r.json()

# MCP 工具注册（以 mcp Python SDK 为例）
TOOLS = {
    "pm_list_projects":      lambda a: pm_get("project/List", **a),
    "pm_get_project":        lambda a: pm_get("project/Detail", **a),
    "pm_list_requirements":  lambda a: pm_get("requirement/List", **a),
    "pm_create_requirement": lambda a: pm_post("requirement/Create", **a),
    "pm_list_tasks":         lambda a: pm_get("task/List", **a),
    "pm_available_tasks":    lambda a: pm_get("task/Available", **a),
    "pm_claim_task":         lambda a: pm_post("task/Claim", **a),
    "pm_start_task":         lambda a: pm_post("task/Start", **a),
    "pm_complete_task":      lambda a: pm_post("task/Complete", **a),
    "pm_fail_task":          lambda a: pm_post("task/Fail", **a),
    "pm_trigger_ci":         lambda a: pm_post("ci/Trigger", autoApprove="false", **a),
    "pm_ci_status":          lambda a: pm_get("ci/Status", **a),
    "pm_health_score":       lambda a: pm_get("progress/HealthScore", **a),
}
```

### 6.2 注册到 Hermes

```bash
hermes mcp add pm-server \
  --command "python3 ~/hermes-tools/pm-mcp-server/server.py" \
  --env PM_API_BASE=http://192.168.10.73:6020/agent \
  --env PM_AGENT_KEY=$(nexus credential get 101 --field token)

hermes mcp test pm-server
```

### 6.3 使用示例

在 Hermes CLI 中即可直接操作：

```
> 查看所有项目的健康状态
→ Hermes 调 pm_list_projects → pm_health_score（每个项目）→ 生成报告

> 帮我在 PM 里为"赛博管理"项目创建一个需求：支持导出 PDF 报告
→ pm_list_projects（找 projectId）→ pm_create_requirement → 返回需求 ID

> 认领下一个可做的任务并执行
→ pm_available_tasks → pm_claim_task → pm_start_task → [Hermes 改代码] → pm_complete_task
```

---

## 7. Webhook 任务推送

PM 新建任务时可主动推送给 Hermes，实现实时响应（无需轮询）。

### 7.1 PM 侧配置

```
PM Admin → 系统设置 → 全局配置
  hermes_webhook_url    = http://<hermes-host>:8900/webhooks/pm-task-updates
  hermes_webhook_secret = <随机 32 字节十六进制>（两端一致）
```

### 7.2 Hermes 侧启动 Webhook 接收

参考 `api/docs/hermes-worker-reference.py` 完整实现：

```bash
pip install flask httpx
export PM_API_BASE="http://192.168.10.73:6020/agent"
export PM_AGENT_KEY=$(nexus credential get 101 --field token)
export HERMES_WEBHOOK_SECRET="<same-as-pm-setting>"

python3 ~/hermes-tools/hermes-worker.py
# 监听 0.0.0.0:8900
```

Webhook 事件格式（PM 推送）：

```json
{
  "type": "new_task",
  "task_id": 88,
  "title": "实现微信扫码登录",
  "project_id": 3,
  "requirement_id": 42,
  "pipeline_profile_id": 5
}
```

### 7.3 备选：Cron 轮询

如果 Webhook 不可用，用轮询兜底（已在 Hermes 配置了 3 个定时任务）：

| Cron Job ID | 任务 | 频率 |
|-------------|------|------|
| `2d416ff56a1b` | PM 每日健康巡检 | 每日 09:00 |
| `ab6e5a4f461b` | PM 需求积压监控 | 每 2 小时 |
| `ed73613ab3b7` | PM 晨间简报 | 工作日 08:30 |

---

## 8. 验证 Checklist

### 基础联通

- [ ] `curl` 调 `/agent/project/List` 返回 `code=0`
- [ ] `curl` 调 `/agent/requirement/List?projectId=N` 返回数据
- [ ] `curl` POST `/agent/task/Claim` 能认领任务（无可用任务时 data 为 null，code 仍为 0）

### AI Provider 链路

- [ ] PM `appsettings.json` 中 `Hermes:GatewayUrl` 已填入实际地址
- [ ] Admin UI 中 Hermes Provider `Status=启用`
- [ ] `POST /admin/Ai/Chat` 返回 Hermes 推理结果

### MCP Server（可选）

- [ ] `hermes mcp test pm-server` 通过
- [ ] Hermes 能调 `pm_create_requirement` 并在 PM Admin 可见记录

### 全工作流闭环（可选）

- [ ] Webhook 接收正常（PM 创建任务 → Hermes 收到推送）
- [ ] Hermes 认领任务 → 完成 → PM 任务状态变为 `Success`
- [ ] CI 触发后 `autoApprove=false`，PM 构建审批队列可见

---

## 9. 常见问题

**Q: 调 API 返回 401？**  
401 = 凭据缺失或无效（没带 `Authorization` 头、key 格式不对、key 不存在/已过期/已吊销、归属账号被停用，或同 IP 触发防爆破拉黑）。检查 `Authorization: Bearer sk_...` 格式与 key 状态。**注意区别于 403**。

**Q: 调 API 返回 403 `缺少 scope 权限: xxx`？**  
key 有效但缺该端点所需 scope（403 由全局 `ScopeFilter` 判定）。给该 key 补对应 scope（`POST /admin/AccessToken/Update`），或改用 FullAccess key。各端点所需 scope 见 §4.1。

**Q: POST 端点返回 400 / 参数解析失败？**  
所有 POST 端点使用表单编码（`application/x-www-form-urlencoded`），**不是 JSON**。检查 `Content-Type` header。

**Q: Hermes Gateway 还没部署，PM AI 功能怎么用？**  
PM 内置 Stub Gateway 脚本：
```bash
python3 /opt/projects/saibo/apps/pm/api/scripts/hermes-stub-gateway.py --port 18080 -v
# 然后在 appsettings.json 设置 Hermes:GatewayUrl = http://127.0.0.1:18080
```

**Q: CI 触发后构建自动发布了？**  
PM 的 `autoApprove=true` 是默认值，必须显式传 `autoApprove=false`。参考 `hermes-worker-reference.py` 中的写法。

**Q: Hermes Cron 任务超时（>3min）？**  
将复杂任务拆分为多个子任务；Cron 间隔不低于 5 分钟；大量并发 API 调用受 PM 速率限制 60 次/分。

---

## 相关文档

| 文档 | 路径 |
|------|------|
| Hermes 集成计划（Stage 0-4） | `api/docs/AI_HermesIntegration_Plan.md` |
| 全工作流闭环方案（TG→PM→Hermes） | `api/docs/AI_Hermes_FullWorkflow_Plan_v2.md` |
| Hermes Worker 参考实现 | `api/docs/hermes-worker-reference.py` |
| Stub Gateway（本地测试） | `api/scripts/hermes-stub-gateway.py` |
| AccessToken 迁移 SQL | `api/docs/access-token/migrate-agent-token.sql` |

# PM 系统操作手册

> **版本**: v2.0（hermes-pm-system-integration 升级版）  
> **日期**: 2026-06-11  
> **覆盖范围**: Admin API（Bearer JWT）+ Agent API（Bearer AccessToken sk_...）+ 枚举速查 + 关联规则 + 典型场景

---

## 目录

1. [鉴权速查](#1-鉴权速查)
2. [Admin API 端点（Bearer JWT）](#2-admin-api-端点bearer-jwt)
3. [Agent API 端点（Bearer sk_...）](#3-agent-api-端点bearer-sk_)
4. [枚举值对照表](#4-枚举值对照表)
5. [关联规则速查](#5-关联规则速查)
6. [典型场景 Workflow](#6-典型场景-workflow)
7. [FromForm vs JSON Body 区别](#7-fromform-vs-json-body-区别)
8. [常见错误速查](#8-常见错误速查)

---

## 1. 鉴权速查

| 鉴权类型 | 获取方式 | 使用位置 | 有效期 |
|---------|---------|---------|-------|
| Admin JWT | `POST /admin/Auth/Login` 表单登录 | `/admin/*` 所有端点 | 24h（默认） |
| Agent AccessToken `sk_...` | Admin JWT → `POST /admin/AccessToken/Create` | `/agent/*` 所有端点 | 自定义（建议 365 天） |

**登录获取 Admin JWT**：
```bash
TOKEN=$(curl -s http://192.168.10.73/admin/Auth/Login \
  -d "username=admin&password=111111&totpCode=" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['accessToken'])")
```

**当前在用 Agent Token（FullAccess，到期 2026-06-30）**：
```
sk_48c02cac2eaa8ce8260af15e880a07a37bff97aadfaa3d1008228fa307c5da05
```

---

## 2. Admin API 端点（Bearer JWT）

> 所有 `/admin/*` 端点要求 Admin JWT；POST 为 `[FromForm]` 表单编码（除特别注明）。

### 2.1 项目管理

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/admin/Project/List` | GET | `keyword` `status` `page` `pageSize` | 项目列表 |
| `/admin/Project/Detail?id=N` | GET | `id` | 项目详情 |
| `/admin/Project/Add` | POST | `name` `code` `description` `status` `tags` | 新建项目 |
| `/admin/Project/Edit` | POST | `id` + 同上 | 编辑项目 |
| `/admin/Project/Del` | POST | `id` | 删除项目 |

### 2.2 需求管理

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/admin/Requirement/List` | GET | `projectId` `status` `type` `keyword` `page` `pageSize` | 需求列表 |
| `/admin/Requirement/Detail?id=N` | GET | | 需求详情（含关联任务） |
| `/admin/Requirement/Add` | POST | `projectId` `title` `content` `type` `complexity` `assignee` `estimatedHours` | 创建需求 |
| `/admin/Requirement/Edit` | POST | `id` + 同上 | 编辑需求 |
| `/admin/Requirement/Del` | POST | `id` | 删除需求 |
| `/admin/Requirement/UpdateStatus` | POST | `id` `status` | 变更状态 |

### 2.3 任务管理（TaskItem）

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/admin/TaskItem/List` | GET | `projectId` `requirementId` `featureModuleId` `claimStatus` `execStatus` `type` `assignee` `keyword` `page` `pageSize` | 任务列表 |
| `/admin/TaskItem/Detail?id=N` | GET | | 任务详情 |
| `/admin/TaskItem/Kanban?projectId=N` | GET | | 看板视图 |
| `/admin/TaskItem/Available` | GET | `projectId`（可选） | 可接单任务列表（Pending + 未认领） |
| `/admin/TaskItem/Add` | POST | `projectId` `requirementId` `featureModuleId` `title` `content` `type` `complexity` `assignee` `estimatedHours` `maxRetryCount` `timeoutSeconds` `execMode` | 创建任务 |
| `/admin/TaskItem/Edit` | POST | `id` + 同上 | 编辑任务 |
| `/admin/TaskItem/Del` | POST | `id` | 删除任务 |
| `/admin/TaskItem/Claim` | POST | `agentName`（默认"AI-MCP"）`projectId`（可选） | 接单（锁定第一个 Pending+Unclaimed 任务） |
| `/admin/TaskItem/ReportStart` | POST | `taskItemId` `agentName` | 上报开始执行 |
| `/admin/TaskItem/ReportSuccess` | POST | `taskItemId` `agentName` `message`（可选） | 上报执行成功 |
| `/admin/TaskItem/ReportFail` | POST | `taskItemId` `agentName` `reason` | 上报执行失败 |
| `/admin/TaskItem/Resume` | POST | `id` | 恢复任务（Failed/Timeout → Pending） |
| `/admin/TaskItem/Cancel` | POST | `id` | 取消任务 |
| `/admin/TaskItem/LogQuickFix` | POST | `projectId` `title` `content` `type`（默认Bugfix）`actualHours` | 快速补录已完成的需求+任务 |
| `/admin/TaskItem/Logs` | GET | `taskItemId` `page` `pageSize` | 执行日志 |
| `/admin/TaskItem/WriteLog` | POST | `taskItemId` `action` `message` `operator` | 写入审计日志 |

> **接单流程（正确顺序）**：Claim → ReportStart → [执行] → ReportSuccess / ReportFail

### 2.4 功能模块（FeatureModule）

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/admin/FeatureModule/List?projectId=N` | GET | `projectId` `parentId` | 功能模块树 |
| `/admin/FeatureModule/Add` | POST | `projectId` `parentId` `name` `description` | 创建模块（parentId=0 为根） |
| `/admin/FeatureModule/Edit` | POST | `id` + 同上 | 编辑模块 |
| `/admin/FeatureModule/Del` | POST | `id` | 删除模块 |

### 2.5 AccessToken 管理

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/admin/AccessToken/List` | GET | `keyword` `accountId` `page` | Token 列表 |
| `/admin/AccessToken/Create` | POST | `name` `accountId`/`serviceAccount` `scopes` `fullAccess` `expiresAt` `rateLimitPerMinute` `ipAllowlist` | 签发（明文只回显一次） |
| `/admin/AccessToken/Revoke` | POST | `id` `reason` | 吊销（即时失效） |
| `/admin/AccessToken/Update` | POST | `id` `name` `scopes` `rateLimitPerMinute` `ipAllowlist` `expiresAt` | 更新（不更新 key 本身） |

### 2.6 Auth 相关

| 端点 | 方法 | 参数 | 说明 |
|------|------|------|------|
| `/admin/Auth/Login` | POST | `username` `password` `totpCode` | 登录（返回 `data.accessToken`） |
| `/admin/Auth/Logout` | POST | — | 注销 |
| `/admin/Auth/RefreshToken` | POST | `refreshToken` | 刷新 JWT |

---

## 3. Agent API 端点（Bearer sk_...）

> 所有 `/agent/*` 端点要求 Agent AccessToken；POST 为 `[FromForm]` 表单编码（除特别注明 `[FromBody]`）。
> Scope 为空 = 与旧 agentToken 等价（全开）；受限 key 缺少对应 scope 返回 403。

### 3.1 项目（Scope: `read:project`）

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/agent/project/List` | GET | `keyword` `status` `page` `pageSize` | 项目列表 |
| `/agent/project/Detail?id=N` | GET | | 详情 |
| `/agent/project/SubList?projectId=N` | GET | | 子项目（含 Git 仓库信息） |

### 3.2 需求（Scope: `read:requirement` / `write:requirement`）

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/agent/requirement/List` | GET | `projectId` `status` `type` `keyword` `page` `pageSize` | 需求列表 |
| `/agent/requirement/Detail?id=N` | GET | | 详情（含任务） |
| `/agent/requirement/Create` | POST | `title` `projectId` `content` `type` `complexity` | 创建 |
| `/agent/requirement/Update` | POST | `id` + 同上 | 编辑 |
| `/agent/requirement/Delete` | POST | `id` | 删除 |
| `/agent/requirement/UpdateStatus` | POST | `id` `status` | 变更状态 |
| `/agent/requirement/AddComment` | POST | `requirementId` `content` | 添加评论 |

### 3.3 任务（Scope: `read:task` / `write:task`）

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/agent/task/List` | GET | `projectId` `requirementId` `claimStatus` `execStatus` | 任务列表 |
| `/agent/task/Detail?id=N` | GET | | 详情 |
| `/agent/task/Available` | GET | `projectId`（可选） | 可认领列表 |
| `/agent/task/Logs?taskItemId=N` | GET | | 执行日志 |
| `/agent/task/Claim` | POST | `projectId`（可选） | 认领（锁定并返回任务对象） |
| `/agent/task/Start` | POST | `id` | 标记开始 |
| `/agent/task/Complete` | POST | `id` `message`（可选） | 完成 |
| `/agent/task/Fail` | POST | `id` `message` | 失败 |
| `/agent/task/Cancel` | POST | `id` | 取消 |
| `/agent/task/Resume` | POST | `id` | 恢复 |

### 3.4 CI/CD（Scope: `read:ci` / `write:ci`）

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/agent/ci/Trigger` | POST | `profileId` `branch` `version` `autoApprove=false`（**必须**） | 触发流水线 |
| `/agent/ci/Status?buildId=N` | GET | | 构建状态 |
| `/agent/ci/Jobs?buildId=N` | GET | | Job 列表 |
| `/agent/ci/JobTrace?buildId=N&jobId=N` | GET | | Job 日志 |
| `/agent/ci/TestReport?buildId=N` | GET | | JUnit 报告 |
| `/agent/ci/Link` | POST | `buildId` `requirementId` `taskItemId` | 关联构建到需求/任务 |

### 3.5 进度（Scope: `read:progress`）

| 端点 | 方法 | 主要参数 | 说明 |
|------|------|---------|------|
| `/agent/progress/HealthScore?projectId=N` | GET | | 项目健康度评分 |
| `/agent/progress/Builds` | GET | | 构建记录 |
| `/agent/progress/Releases` | GET | | 发布记录 |
| `/agent/progress/Changelog` | GET | | 变更日志 |

---

## 4. 枚举值对照表

### 4.1 ProjectStatus 项目状态

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Planning | 规划中 |
| 1 | Active | 进行中 |
| 2 | OnHold | 暂停 |
| 3 | Completed | 已完成 |
| 4 | Archived | 已归档 |

### 4.2 RequirementType 需求类型

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Feature | 新功能 |
| 1 | Bugfix | Bug 修复 |
| 2 | Improvement | 改进 |
| 3 | Task | 任务 |
| 4 | Research | 研究 |

### 4.3 RequirementStatus 需求状态

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Draft | 草稿 |
| 1 | Todo | 待开始 |
| 2 | InProgress | 进行中 |
| 3 | Testing | 测试中 |
| 4 | Done | 已完成 |
| 5 | Closed | 已关闭 |

### 4.4 Complexity 复杂度

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Low | 低（简单） |
| 1 | Medium | 中 |
| 2 | High | 高 |
| 3 | VeryHigh | 极高 |

### 4.5 TaskItemType 任务类型

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Development | 开发 |
| 1 | Bug | Bug 修复 |
| 2 | Design | 设计 |
| 3 | Testing | 测试 |
| 4 | Docs | 文档 |
| 5 | Other | 其他 |

### 4.6 TaskItemClaimStatus 认领状态

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Pending | 未认领（可接单） |
| 1 | Claimed | 已认领 |

### 4.7 TaskItemExecStatus 执行状态

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Idle | 待执行 |
| 1 | Running | 执行中 |
| 2 | Success | 执行成功 |
| 3 | Failed | 执行失败 |
| 4 | Timeout | 超时 |
| 5 | Hibernated | 休眠（暂停） |

### 4.8 BuildStatus 构建状态

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | PendingApproval | 待审批 |
| 1 | Approved | 已审批 |
| 2 | Building | 构建中 |
| 3 | Success | 成功 |
| 4 | Failed | 失败 |
| 5 | Rejected | 已拒绝 |

### 4.9 RequirementSource 需求来源

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Manual | 手动录入 |
| 1 | Telegram | Telegram Bot |
| 2 | AI | AI 生成 |
| 3 | Git | Git 提交/MR |
| 4 | Hermes | Hermes Agent |

---

## 5. 关联规则速查

> 每个实体创建时必须满足的外键约束，缺失会导致 400 或业务错误。

| 实体 | 必填外键 | 可选外键 | 常见遗漏后果 |
|------|---------|---------|------------|
| Requirement（需求） | `projectId` | `customerId` `parentId` | 缺 projectId → 400 / 需求孤立找不到 |
| TaskItem（任务） | `projectId` | `requirementId` `featureModuleId` | 缺 requirementId → 任务无法关联需求，健康度计算偏低 |
| FeatureModule（功能模块） | `projectId` | `parentId`（不填=根节点） | 缺 projectId → 模块无法在项目树中展示 |
| Milestone（里程碑） | `projectId` | | — |
| BuildRecord（构建记录） | `pipelineProfileId` | `requirementId` `taskItemId` | 缺关联 → 进度/健康度统计缺失 |
| AccessToken | `accountId` 或 `serviceAccount` | `scopes` `expiresAt` | 缺 scopes → 受限 key 所有 `/agent` 端点返回 403 |

**关联自检（创建任务前）**：
```
项目 → 需求 → 任务（→ FeatureModule 挂载）
         ↑              ↑
    必须 projectId  建议 requirementId + featureModuleId
```

---

## 6. 典型场景 Workflow

### 6.1 完整录入流程（AI 最容易漏的步骤）

```
1. 确认项目已存在（GET /admin/Project/List?keyword=xxx）
   ↓ 无则创建（POST /admin/Project/Add，必填 name+code）
2. 创建需求（POST /admin/Requirement/Add）
   必填：projectId, title, content
   建议填：type(Feature/Bugfix), complexity, estimatedHours, assignee
   ↓
3. 创建功能模块（POST /admin/FeatureModule/Add）
   必填：projectId, name
   可选：parentId（不填=根节点）
   ↓
4. 创建任务（POST /admin/TaskItem/Add）
   必填：projectId, title
   必须关联：requirementId（漏掉则任务与需求断链）
   建议关联：featureModuleId（漏掉则任务无功能模块归属）
   建议填：type, complexity, timeoutSeconds(默认300), maxRetryCount(默认3)
   ↓
5. 更新需求状态为 InProgress（POST /admin/Requirement/UpdateStatus）
```

### 6.2 AI 自驱接单流程

```bash
# 1. 查可接单列表
GET /admin/TaskItem/Available?projectId=N

# 2. 接单（获取任务详情，锁定）
POST /admin/TaskItem/Claim
  - agentName=AI-MCP
  - projectId=N（可选，不填接全局第一个）

# 3. 标记开始
POST /admin/TaskItem/ReportStart
  - taskItemId=<id>
  - agentName=AI-MCP

# 4. 执行任务（AI 改代码/提交）

# 5a. 执行成功
POST /admin/TaskItem/ReportSuccess
  - taskItemId=<id>
  - agentName=AI-MCP
  - message=<附注>

# 5b. 执行失败
POST /admin/TaskItem/ReportFail
  - taskItemId=<id>
  - agentName=AI-MCP
  - reason=<失败原因>
```

### 6.3 快速补录（事后补录已完成的工作）

```bash
POST /admin/TaskItem/LogQuickFix
  - projectId=12
  - title=修复 noVNC WebSocket 超时
  - content=将 nginx proxy_read_timeout 改为 86400s
  - type=Bugfix
  - actualHours=0.5
```

### 6.4 CI 触发（强制人工审批）

```bash
# ⚠️ autoApprove 必须传 false
POST /agent/ci/Trigger
  - profileId=5
  - branch=main
  - version=1.2.3
  - autoApprove=false   # 进入待审批状态，不自动发布
```

---

## 7. FromForm vs JSON Body 区别

| 端点类别 | 编码 | Content-Type | 说明 |
|---------|------|-------------|------|
| 绝大多数 POST | `[FromForm]` | `application/x-www-form-urlencoded` | **默认**，curl 用 `-d key=val` |
| 特殊 POST（注明） | `[FromBody]` | `application/json` | 如 `GenerateFromRequirements` `ConfirmCreate` |
| GET | 查询参数 | — | `?key=val&key2=val2` |

**FromForm POST 示例（curl）**：
```bash
curl -X POST http://192.168.10.73/admin/TaskItem/Claim \
  -H "Authorization: Bearer $TOKEN" \
  -d "agentName=AI-MCP&projectId=12"
```

**FromBody POST 示例（curl）**：
```bash
curl -X POST http://192.168.10.73/admin/TaskItem/GenerateFromRequirements \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"requirementIds":[1,2,3],"projectId":12}'
```

---

## 8. 常见错误速查

| HTTP 状态 | 响应体 | 含义 | 解决 |
|-----------|--------|------|------|
| 401 | 空 | 凭据缺失/无效/过期/被吊销 | 检查 Bearer token；Admin JWT 有效期 24h；防爆破拉黑 15min |
| 403 | `{"code":-1,"message":"缺少 scope 权限: xxx"}` | AccessToken 缺少 scope | 补签 scope 或换 FullAccess key |
| 400 | 各种业务错误 | 参数缺失或格式错误 | 检查必填外键（projectId 等） |
| 200, `code: -1` | `{"code":-1,"message":"xxx"}` | 业务逻辑错误 | 看 message 具体提示 |
| 200, `code: 0` | `{"code":0,"message":"ok","data":...}` | 成功 | `data` 为结果；列表型为 `{"items":[...],"total":N}` |

**非接单者上报**（`message: "非接单者不能上报!"`）：
- 原因：Claim 时 agentName="AI-MCP"，ReportStart/Success/Fail 时用了不同 agentName
- 解决：保持 agentName 全程一致

**任务不存在**（`message: "任务不存在!"`）：
- ReportStart 参数名是 `taskItemId`（不是 `id`），参考 §2.3

**接单无结果**（data 为 null）：
- 无可接单任务（Pending + Unclaimed）时正常返回 null，code 仍为 0

---

## 相关文档

| 文档 | 路径 |
|------|------|
| Hermes 接入指南（鉴权/MCP/Webhook 详细） | `docs/Hermes_接入指南.md` |
| Hermes Worker 参考实现（Python） | `docs/hermes-worker-reference.py` |
| PM 文档知识图谱 | `docs/knowledge_graph.md` |
| AVM 录入复盘 | `docs/avm录入复盘与PM改进建议-2026-06-10.md` |

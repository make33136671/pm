---
title: "AI 工作流场景手册"
version: "1.0.0"
date: "2026-06-11"
tasks: "#111 #112 #113 #114 #115 #116"
---

# AI 工作流场景手册

本文档覆盖 PM 系统中 AI Agent 的五个典型工作场景，以及通用操作原则。

---

## 场景 1：项目完整录入工作流 (#111)

**目标**：从零开始录入一个新项目，包含需求、任务、功能模块。

### 步骤

1. **创建项目** — `POST /admin/Project/Add`，填写项目名、代号、GitLab 群组 ID
2. **录入需求** — `POST /admin/Requirement/Add`，每条需求绑定 `projectId`
3. **生成任务**（可选自动）— `POST /admin/TaskItem/GenerateFromRequirements`，传入需求 ID 列表，获取预览
4. **确认创建任务** — `POST /admin/TaskItem/ConfirmCreate`，传入步骤 3 的预览列表
5. **设置功能模块** — `POST /admin/FeatureModule/Add`，将任务与模块关联（`featureModuleId`）

### 注意事项

- 需求状态从 `Draft → Todo` 之后才会被任务查询到
- 任务默认 `AutoAssign=false`，若要 Hermes Worker 自动接单需手动开启
- 大批量录入时建议分 10 条一批提交，避免请求超时

---

## 场景 2：AI 接单→修复→提交流程 (#112)

**目标**：AI Agent（如 Hermes Worker）自动接单、执行任务、提交验收。

### 步骤

```
1. GET  /api/task/available?projectId={id}   → 获取可接单任务列表
2. POST /api/task/claim                       → body: {agentName, projectId}  接单
3. POST /api/task/{id}/start                  → body: {agentName}             标记开始执行
4. （AI 执行任务...）
5. POST /api/task/{id}/submit-review          → body: {agentName, fixedDescription}  提交验收
6. 等待人工/AI 自动验收
   - 通过: POST /admin/TaskItem/ReviewApprove
   - 退回: POST /admin/TaskItem/ReviewReject → AI 重新执行后再次提交
```

### 认证方式

- `/api/task/*` 使用 `X-Api-Key` Header（系统设置 `TaskAgentApiKey`）
- `/admin/*` 使用 JWT Bearer Token（FullAccess token 绕过 scope 检查）

### 异常处理

- 执行失败: `POST /api/task/{id}/fail`，`FailCount++`，达到 `MaxRetryCount` 后自动休眠
- 超时: `POST /api/task/{id}/timeout`（等同失败）
- 任务休眠后需人工 `POST /admin/TaskItem/Resume` 恢复

---

## 场景 3：变更日志自动生成流程 (#113)

**目标**：基于已完成的任务和需求，自动生成版本变更日志。

### 步骤

1. 查询已完成任务 — `GET /admin/TaskItem/List?projectId={id}&execStatus=Success`
2. 触发 AI 生成变更日志 — `POST /admin/Document/GenerateChangelog`，传入 `projectId` 和版本号
3. 查看生成结果 — `GET /admin/Document/List?projectId={id}&type=Changelog`
4. 发布到 TG 群（可选）— `POST /admin/Chat/SendMessage`

### 变更日志格式

```markdown
# v{version} 变更日志

## 新功能
- 功能描述（来自 Feature 类型需求）

## Bug 修复
- 问题描述（来自 Bugfix 类型需求）

## 改进
- 改进描述（来自 Improvement 类型需求）
```

---

## 场景 4：文档分析 + 需求覆盖度检查 (#114)

**目标**：上传需求文档后，AI 分析文档并检查已有任务是否覆盖所有需求点。

### 步骤

1. 上传文档版本 — `POST /admin/Document/SaveVersion`，传入文档内容
2. 触发 AI 分析 — `POST /admin/AiTask/Analyze`，`type=AnalyzeCode`
3. 获取覆盖度报告 — AI 输出格式：
   ```json
   {
     "uncovered": ["需求点A", "需求点B"],
     "covered": ["需求点C"],
     "coverage": 0.6
   }
   ```
4. 根据未覆盖需求点批量创建任务 — `POST /admin/TaskItem/ConfirmCreate`

### 实用技巧

- 文档内容建议使用 Markdown 格式，AI 解析准确率更高
- 每次分析完建议保存 AI 输出到文档版本，方便追溯

---

## 场景 5：任务验收/AI 退回 (#115)

**目标**：处理需要人工验收或 AI 自动退回的任务。

### 完整验收流程

```
Agent 提交 → PendingReview 状态 → 触发验收
                                    ├── 人工验收: POST /admin/TaskItem/ReviewApprove
                                    ├── 人工退回: POST /admin/TaskItem/ReviewReject
                                    └── AI 自动验收: POST /admin/TaskItem/AutoVerify
                                        └── AI 判断:
                                            ├── 通过 → Success + Verified=true
                                            └── 退回 → Pending/Idle（重新接单）
```

### 关键字段说明

| 字段 | 说明 |
|------|------|
| `FixedDescription` | Agent 提交验收时填写的修复说明 |
| `Verified` | 是否经过人工/AI 验收（`true` = 已验收） |
| `RejectReason` | 退回原因，Agent 重做时应参考此字段 |
| `RouteHint` | 任务关联的前端路由（辅助定位） |
| `FilePathHint` | 关联代码文件路径（逗号分隔） |
| `LineHint` | 关联代码行号说明 |

### AI AutoVerify 工作原理

1. 读取任务要求（`Title` + `Content`）
2. 读取修复说明（`FixedDescription`）
3. 调用 AI 对比判断（Temperature=0.1，强确定性输出）
4. 解析 JSON 输出 `{"approved": bool, "reason": "..."}`
5. 调用 `ReviewApprove` 或 `ReviewReject`

---

## 通用原则：AI 操作 PM 的最佳实践 (#116)

### 1. 幂等性

所有写操作应确保重复调用不造成数据重复。主要保证方式：
- 接单前先查 `available` 列表，避免重复接单
- 使用 `BatchId` 字段标记批次，防止重复提交

### 2. 错误处理策略

| 错误类型 | 处理方式 |
|----------|----------|
| 401 认证失败 | 检查 API Key / JWT Token 是否正确 |
| 400 业务错误 | 读取 `message` 字段，根据提示修正请求 |
| 500 服务器错误 | 等待 60 秒后重试，超过 3 次则停止并报警 |
| 任务不存在 | 跳过，继续处理下一条 |

### 3. 并发安全

- 同一任务同时只允许一个 Agent 接单（`ClaimStatus=Claimed` 后其他 Agent 接单失败）
- Agent 应使用唯一的 `agentName`，便于追踪和审计
- 避免在短时间内重复调用接单接口（建议间隔 ≥ 5 秒）

### 4. Token 权限范围

| Token 类型 | 范围 | 适用场景 |
|-----------|------|---------|
| `X-Api-Key` (`TaskAgentApiKey`) | 仅 `/api/task/*` | Hermes Worker 接单/上报 |
| FullAccess JWT Token | 所有 `/admin/*` 接口 | 管理操作、查询、批量更新 |
| 有限 Scope JWT | 指定模块 | 特殊权限限制场景 |

### 5. 日志规范

所有 AI 操作必须写入任务日志（`TaskItemLog`），`operator` 字段使用 Agent 名称（如 `Hermes-Worker-1`、`AI-AutoVerify`）。日志是审计和故障追踪的核心依据。

### 6. 状态流转规则

```
Pending/Idle
  → Claimed/Idle    (接单)
  → Claimed/Running (开始执行)
  → Claimed/PendingReview (提交验收)
  → Claimed/Success      (验收通过)
  → Pending/Idle         (退回重修)
  → Claimed/Failed       (执行失败)
  → Claimed/Hibernated   (达到重试上限)
```

任何绕过状态机的直接修改都需要记录日志。

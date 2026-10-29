---
title: "AI 代码生成 + AI Code Review 设计方案"
version: "1.0.0"
date: "2026-06-11"
status: "draft"
author: "Hermes Agent"
---

# AI 代码生成 + AI Code Review 设计方案

> **版本**: v1.0.0
> **日期**: 2026-06-11
> **状态**: Draft — 方案设计阶段
> **前置依赖**: GitLab 集成（已有）、AI 需求拆解（已有 #22）、AI 代码分析（已有 #25）

---

## 1. 背景与目标

### 1.1 现状

PM 系统已有：
- **需求 → 任务拆解**（#22）— AI 将需求拆为可执行任务
- **GitLab 集成** — 仓库浏览、代码查看、分支管理、MR 管理
- **AI 代码分析**（#25）— 分析代码找 Bug/安全问题
- **AI 聊天** — 通用的 LLM 对话入口

缺的就是从"任务"到"代码变更"到"上线"的最后一环。

### 1.2 目标

```
PM 里创建需求
  → AI 拆任务（已有）
  → AI 读任务 → 读代码 → 写代码 → 提 PR
  → AI Code Review PR
  → 人工合并
  → CI/CD 自动部署
```

**关键原则：AI 写代码不直接合入，必须走 GitLab MR 流程，人类复核后再合并。**

---

## 2. 架构

```
┌────────────────────────────────────────────────────────────┐
│                     PM Server (.NET)                       │
│                                                            │
│  TaskItemService.AiExecuteTask(taskId)                     │
│    1. 读任务内容（需求、代码位置、相关文件）                │
│    2. 调 AI 生成代码                                       │
│    3. 写文件到 GitLab 临时分支                             │
│    4. 提 MR → 通知人工复核                                │
│                                                            │
│  AiCodeReviewService(reviewMrId)                          │
│    1. 读 MR diff                                          │
│    2. 逐文件 AI Review                                    │
│    3. 在 MR 评论区写 Review 结果                          │
│    4. 可标记 "AI 审核通过" / "AI 发现 N 个问题"           │
└────────────────────────────────────────────────────────────┘
          │
          │ GitLab API
          ▼
┌────────────────────────────────────────────────────────────┐
│                      GitLab Server                         │
│                                                            │
│  分支: feature/ai-T123-xxx                                 │
│  提交: [AI] 根据任务 T-123 生成代码 - xxx                  │
│  MR:  AI 自动生成: T-123 xxx                              │
│        → CI/CD 自动跑测试                                  │
│        → AI Code Review 自动评论                           │
│        → 人工 Merge                                        │
└────────────────────────────────────────────────────────────┘
```

---

## 3. AI 代码生成器

### 3.1 触发入口

| 入口 | 说明 |
|------|------|
| **任务详情页「AI 执行」按钮** | 已有 `ExecByAi` 端点，扩展为超时后自动执行代码生成 |
| **需求详情页「AI 生成代码」按钮** | 新入口，将需求下的任务批量生成代码 |
| **Agent API** | `POST /agent/task/ExecByAi` 或 `POST /ai/GenerateCode`（已有） |

### 3.2 AI 代码生成的输入

```
{
  "taskId": 123,
  "taskTitle": "修复登录页验证码显示异常",
  "taskContent": "验证码图片不刷新，每次请求返回同一张图",
  "projectId": 11,
  "subProjectId": 3,         // 哪个子项目（对应 Git 仓库）
  "focusPaths": [             // 相关文件路径（由任务创建者标注）
    "apps/web-antd/src/views/login/index.vue",
    "api/Controllers/AuthController.cs"
  ],
  "relatedFiles": [           // 自动读取的相关代码文件
    { "path": "src/login.vue", "content": "..." },
    { "path": "api/auth.go", "content": "..." }
  ],
  "codingConventions": "项目编码规范（从 AiContext 注入）"
}
```

### 3.3 流程

```
1. 触发 "AI 执行任务"
2. PM 后端读任务 → 读项目 Git 仓库的当前代码
3. 构建 prompt → 调 LLM（通过 IAiService / Llama / DeepSeek）
4. AI 返回代码变更列表（文件路径 + 新内容）
5. 后端解析变更 → 创建 GitLab 分支 feature/ai-T{taskId}-xxx
6. 逐文件写入 → 提交 commit
7. 创建 MR，标题: "[AI] {任务标题} #{taskId}"
8. 在 MR 描述中写出 AI 做了什么（参考代码变更）
9. 更新任务状态 → 通知人工
```

### 3.4 Git 操作方式

当前 PM 的 `SubProjectController` 已有：

| API | 作用 |
|-----|------|
| GET /admin/SubProject/GitCommit?id=N | 提交代码 |
| GET /admin/SubProject/GitPush?id=N | 推送 |
| GET /admin/SubProject/GitStatus?id=N | 查看状态 |

**但当前实现是操作 PM 服务器本地仓库。** AI 代码生成的场景建议直接走 **GitLab API**（已有 `GitLabController`），不走本地仓库，因为：

1. PM 服务器不一定有项目代码
2. AI 生成的代码直接通过 GitLab API 创建文件/提交/MR，不需要在 PM 服务器上 clone

直接复用 `GitLabController`（Routes 内已有 `CreateBranch`、`CreateFile`、`CreateCommit`、`CreateMergeRequest`）。

### 3.5 安全边界

| 规则 | 说明 |
|------|------|
| AI 不直接合入 | 必须走 MR，必须有人工审核 |
| AI 不删除文件 | 只创建和修改，不删 |
| AI 不操作 main 分支 | 必须在 `feature/ai-*` 临时分支上操作 |
| 生成的代码由 AI 签名 | commit 作者标记为 `AI Agent`，可追溯 |
| 失败回滚 | 自动删除分支（保留 commit 记录用于审查） |

---

## 4. AI Code Review

### 4.1 触发入口

| 触发方式 | 说明 |
|----------|------|
| **自动触发** | GitLab Webhook → PM 收到 MR 事件 → 自动触发 AI Review |
| **手动触发** | MR 详情页 →「AI Review」按钮 |
| **定时扫描** | 定时查待 Review 的 MR |

### 4.2 AI Review 的输出

Review 结果以 GitLab MR Comment 形式写入：

```
## 🤖 AI Code Review

### 问题 (2 个)

**🔴 严重: 无法直接使用用户输入拼接 SQL**
- 文件: api/Controllers/UserController.cs:45
- 代码: `db.Query($"SELECT * FROM users WHERE id = {userId}")`
- 建议: 改为参数化查询 `db.Query("SELECT * FROM users WHERE id = @id", new { id = userId })`

**🟡 建议: 硬编码超时时间**
- 文件: api/Services/AuthService.cs:88
- 代码: `await Task.Delay(5000)`
- 建议: 读取配置 `_options.TokenExpirySeconds`

### 总结
- 检查文件: 5 个
- 严重问题: 1
- 轻微建议: 3
- 评分: ⭐⭐⭐（需要修复严重问题后合并）
```

### 4.3 Review 能力

| 检查项 | 示例 |
|--------|------|
| SQL 注入风险 | 拼接 SQL 无参数化 |
| 硬编码凭据 | Token/密码写在代码里 |
| 安全漏洞 | XSS、CSRF、IDOR |
| 性能问题 | N+1 查询、无索引字段、大循环内调数据库 |
| 编码规范 | 命名规范、项目约定（如 FromForm vs FromBody） |
| 异常处理 | try-catch 空块、吞异常 |
| 日志 | 缺少关键日志、打印敏感信息 |
| 代码重复 | 与已有功能重复 |
| 测试覆盖 | 新功能无测试 |

### 4.4 与现有 GitLabController 的集成

`GitLabController` 已有 `MergeRequestDetail`（读 MR 详情）、`CreateMergeRequest`（创建 MR）、`CreateComment`（无，需要新增）。

AI Review 需要的新端点：

| 端点 | 作用 |
|------|------|
| `POST /admin/GitLab/CreateMrComment` | 在 MR 下写评论 |
| `POST /admin/GitLab/GetMrDiff` | 获取 MR 的代码变更 diff |

如果不想新增端点，也可以直接调 GitLab API（`POST /api/v4/projects/{id}/merge_requests/{iid}/notes`）。

---

## 5. 实施计划

### Phase 1: AI Code Review（先做，价值高、改动小）

| # | 任务 | 工作量 |
|---|------|--------|
| 1 | PM 接收 GitLab MR Webhook / 手动触发入口 | 小 |
| 2 | 读 MR diff（通过 GitLab API 或 `GetMrDiff`） | 小 |
| 3 | 构建 Review prompt + 调 AI | 中 |
| 4 | 解析 AI 输出 + 写到 MR 评论区 | 中 |
| 5 | 前后端：MR 列表页加入 AI Review 按钮 + 结果展示 | 中 |

### Phase 2: AI 代码生成器（核心，改动大）

| # | 任务 | 工作量 |
|---|------|--------|
| 1 | 任务触发 AI 生成：读任务 + 读相关代码 + 构建 prompt | 中 |
| 2 | 解析 AI 输出为代码变更列表 | 中 |
| 3 | GitLab API 操作：建分支、写文件、提 commit | 中 |
| 4 | 自动创建 MR + 写入 AI 说明 | 中 |
| 5 | 后端错误处理 + 安全边界（不删文件、不操作 main） | 中 |
| 6 | 前端：任务详情「AI 生成代码」按钮 + 进度展示 | 小 |

---

## 6. 验证标准

- [ ] AI Code Review 能在 MR 下自动写出 Review 评论
- [ ] Review 能正确识别 SQL 注入、硬编码凭据、异常处理缺失
- [ ] AI 代码生成能创建 GitLab 分支并提交文件
- [ ] 生成的代码变更在 MR 中可见，可人工 diff
- [ ] AI 不直接合入，必须人工确认
- [ ] 多次触发不会生成重复 MR（幂等检查）

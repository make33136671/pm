# pm Agent API 使用指南(一期)

## 快速开始

### 1. 在 pm api 主机上创建 token

```bash
cd /<pm-api-deploy-dir>
dotnet run -- agent-token create --name my-bot
# 复制 secret 行的 sk_pm_... 保存到你的 Bot 配置
```

### 2. Bot 调接口

```bash
PM_BASE=https://pm.example.com
TOKEN=sk_pm_xxx

# 列需求
curl "$PM_BASE/agent/requirement/list?projectId=1" \
  -H "Authorization: Bearer $TOKEN"

# 创建需求
curl "$PM_BASE/agent/requirement/create" \
  -H "Authorization: Bearer $TOKEN" \
  -d "Title=Bot 创建的需求&ProjectId=1&Type=Feature"

# 抢任务
curl "$PM_BASE/agent/task/claim" -H "Authorization: Bearer $TOKEN"

# 完成任务
curl "$PM_BASE/agent/task/complete" \
  -H "Authorization: Bearer $TOKEN" \
  -d "id=42&message=已交付"
```

### 3. 拿 OpenAPI 生成 client

```bash
curl "$PM_BASE/swagger/agent/swagger.json" > pm-agent.openapi.json
# 用 openapi-typescript / openapi-generator 生成 typed client
```

## 21 端点速查

参见 spec `docs/superpowers/specs/2026-05-15-pm-agent-api-design.md` §3.1。

## 审计

所有 Agent 操作在 ProjectActivity / TaskItemLog 里 OperatorName 字段以
`agent:<token-name>` 开头,可在 admin 端按前缀过滤区分。

## 错误码

| code | 含义 |
|---|---|
| 0 | 成功 |
| 4001 | token 缺失 |
| 4002 | token 无效 / 已吊销 |
| 4003 | token 过期 |
| 5000 | 服务器错 |

HTTP 状态:200(业务结果),401(token 错)。

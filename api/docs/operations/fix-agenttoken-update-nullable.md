# 修复：AgentToken 更新无法清空可空字段

> **修复时间**：2026-06-01
> **署名**：Claude Code
> **影响仓库**：sb-apps-pm（API）
> **相关提交**：`d3c672e fix: AgentTokenService.Update 用 SetSource 全列更新以正确清空可空字段`

---

## 一、问题现象

`AgentTokenService.Update` 在把以下可空字段更新为 `null` 时，数据库中的旧值不会被清空：

- `ExpiresAt`（过期时间）
- `RateLimitPerMinute`（每分钟速率限制）
- `Notes`（备注）

表现为 3 个单元测试持续失败（红测试）：

| 测试 | 期望 | 实际 |
|------|------|------|
| `Update_PartialFields_OnlyUpdatesProvided` | 未传字段保持原值、传入字段更新 | 部分可空字段未按预期写入 |
| `Update_SetExpiresAtToNull_ClearsField` | `ExpiresAt` 被清空为 `null` | 仍保留旧值 |
| `Update_SetRateLimitToNull_ClearsField` | `RateLimitPerMinute` 被清空为 `null` | 仍为旧值 `100` |

该缺陷为上游 `origin/main` 自带，与本期 Hermes Agent 接入工作无关。

## 二、根因

原实现使用 FreeSql 的对象重载：

```csharp
_fsql.Update<AgentToken>(token).ExecuteAffrows();
```

FreeSql 的 `Update<T>(entity)` 对象重载在生成 UPDATE 语句时**会忽略值为 `null` 的列**，
因此把 `ExpiresAt` / `RateLimitPerMinute` / `Notes` 设为 `null` 后，这些列根本不会被写入，
旧值得以残留——可空字段无法被清空。

## 三、修复

改用 `SetSource` 全列更新，强制把所有映射列（含 `null` 列）写入：

```csharp
// 用 SetSource 全列更新（含可空列置 null）；对象重载 Update(token) 会忽略 null 列，
// 导致 ExpiresAt/RateLimitPerMinute/Notes 无法被清空
_fsql.Update<AgentToken>().SetSource(token).ExecuteAffrows();
```

**改动文件**：`Sb.Apps.Pm.Services/Agent/AgentTokenService.cs`（`Update` 方法）

## 四、验证

| 项目 | 结果 |
|------|------|
| `dotnet build sb-apps-pm.sln` | 0 错误 |
| AgentToken 测试 | 15/15 通过（修复前 12/15） |
| 全量测试套件 | 54/54 通过（修复前 51/54） |

---

## 五、生产部署记录（2026-06-01）

> ⚠️ **安全警告**：本节含生产 MasterKey 明文。MasterKey 可绕过全部 JWT 鉴权（等同 admin 全权）。应用方明确知晓并接受其进入 git 历史与远端仓库的风险。**强烈建议在不再需要时轮换该 key。**

通过框架内置热部署 API 部署最新代码：

| 项 | 值 |
|----|----|
| 目标服务器 | `192.168.10.73:6020`（sbpm 容器） |
| 部署端点 | `POST /admin/App/UploadApi`（multipart，字段 `file`，仅 `.zip`，自动解压→替换→重启） |
| 鉴权方式 | `X-Master-Key` 请求头 |
| **生产 MasterKey** | `8f98b4fa0aa64db598adc7d52d18e8c0` |
| 部署版本 | `4b6fc42`（Hermes 接入 + AgentToken 增强 + 集成测试 + now() 方言修复） |
| 产物 | `dist/api.zip`（过滤 appsettings / wwwroot） |
| 回滚 | `POST /admin/App/Rollback`（`X-Master-Key` + `id`） |

部署命令：
```bash
curl -X POST \
  -H "X-Master-Key: 8f98b4fa0aa64db598adc7d52d18e8c0" \
  -F "file=@api.zip;type=application/zip" \
  http://192.168.10.73:6020/admin/App/UploadApi
```

---

*本文档由 Claude Code 生成。*

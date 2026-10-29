# PM — 赛博项目管理平台

> 基于 **SB Framework** 的派生业务系统，前后端同仓管理。

## 仓库结构

```
pm/
├── api/     # 后端（.NET 10）：Sb.Apps.Pm.WebApi / Services / Models / Tests
├── admin/   # 管理端（Vue + Vben Admin），派生自 sb-framework-admin
├── docs/    # 文档（含 FileManager.md）
├── scripts/ # 仓库运维脚本（GitHub 同步等）
├── tests/   # 跨模块冒烟测试
└── deploy-local.sh   # 本机发布：构建 api + admin 并 rsync 到 /opt
```

## 技术栈

| 层 | 技术 |
|---|---|
| 后端 | .NET 10 · ASP.NET Core · FreeSql（MySQL/PostgreSQL）· Serilog |
| 前端 | Vue 3 · Vite · Vben Admin 5 · Ant Design Vue · Turbo + pnpm workspace |
| 基座 | `SB.Framework.*` NuGet 包 + `SB.Feature.*` 插件 |
| 部署 | Docker（`api/Dockerfile` + `docker-compose.yml`），或 `deploy-local.sh` 直发 `/opt` |

## 核心模块

### 后端 `api/`

| 子工程 | 内容 |
|---|---|
| `Sb.Apps.Pm.WebApi` | 启动壳 + Controllers（按业务分目录） |
| `Sb.Apps.Pm.Services` | 业务服务（项目/需求/任务/文档/服务器/网页终端/文件管理/AI/Chat/Pipeline） |
| `Sb.Apps.Pm.Models` | 实体 + DTO + 枚举 |
| `Sb.Apps.Pm.Services.Tests` | xUnit 测试 |

启动入口 `Program.cs` 只有 5 行：`AddSbFramework()` → `Build()` → `UseWebSockets()` → `UseWebTerminalEndpoint()` → `UseSbFramework()`。

业务装配在 `Sb.Apps.Pm.Services/PmCoreFeature.cs`（PM 自身作为一个 `IFeatureModule`）。

### 网页终端（webterm）

**本地 root PTY**，不是 SSH 到远端：

```
浏览器 (xterm.js) ──WSS /webterm/ws/{id}?token=JWT──► WebTerminalEndpoint（早期中间件）
   ──► PtyManager ──► NativePty ──► posix_spawn ──► /bin/bash -i
```

- 会话可绑定项目/子项目，cwd 自动落到其 `workspace_root`
- 支持 `RunAsUser`（`sudo -u`）
- 输出走 `CircularByteBuffer`（4MB 回滚）+ 落盘日志 `logs/cli/`
- WebSocket 端点用**早期中间件**注册，绕开 endpoint routing（历史踩坑见代码注释）

### 文件管理（FileManager）

服务器文件浏览 / 编辑 / 上传下载 / chmod，**与 webterm 权限对等**。

完整说明见 [docs/FileManager.md](docs/FileManager.md)。

### 管理端 `admin/`

38 个业务模块，位于 `apps/web-antd/src/views/`。菜单由后端 `IModuleInitializer.GetMenus()`
驱动，前端无需手写路由。

跟随上游基座更新：

```bash
git remote add upstream https://gitlab.saibo.club/saibo/framework/sb-framework-admin.git
git fetch upstream && git merge upstream/main
```

---

## 开发

```bash
# 后端
cd api && dotnet build sb-apps-pm.sln

# 前端
cd admin && pnpm install && pnpm dev:antd

# 本机发布（构建 + rsync 到 /opt/api 与 /opt/admin）
./deploy-local.sh          # 全量
./deploy-local.sh api      # 仅后端
./deploy-local.sh admin    # 仅前端
```

> `deploy-local.sh` 会保留线上的 `appsettings*.json`、`installed.lock`、`client.key`、`logs/`，
> 不会覆盖这些文件。

### 冒烟测试

```bash
cd tests/Sb.Apps.Pm.FileManager.Tests && dotnet run
```

---

## 远程仓库（双远程）

| 远程名 | 地址 | 用途 |
|---|---|---|
| `origin` | 内网 GitLab `saibo/apps/pm` | **日常开发主用**，push / pull 都走这里 |
| `github` | https://github.com/make33136671/pm.git | **公网备份**，定期同步 |

### 日常流程

```bash
git add -A && git commit -m "feat: xxx"
git push origin main              # 日常走 GitLab
bash scripts/sync-github.sh       # 定期同步 GitHub
```

### 同步约定

- **更新代码优先 GitLab**：日常 `push` / `pull` 一律 `origin`。
- **定期同步 GitHub**：`scripts/sync-github.sh` 先拉 GitLab 再推 `github`，网络错误重试 3 次，认证错误立即退出。
- **永远保留 GitHub 远程**：任何清理操作都不得 `git remote remove github`。

### GitHub 推送凭据（**当前未配置**）

现状：`github` 地址已配好，但本机没有 GitHub 凭据，推送会报
`fatal: could not read Username for 'https://github.com'`。

**方式 1：Token 写进 credential store**

```bash
echo "https://oauth2:<TOKEN>@github.com" >> ~/.git-credentials
chmod 600 ~/.git-credentials
git ls-remote github      # 验证
```

**方式 2：SSH**（本机已有 `~/.ssh/id_ed25519`）

```bash
# 把 ~/.ssh/id_ed25519.pub 加到 GitHub → Settings → SSH keys
git remote set-url github git@github.com:make33136671/pm.git
```

### 定时同步

`scripts/setup-sync-cron.sh` 会自动探测本机有没有 cron：

- 有 cron → 写入 crontab（每天 03:30）
- 没有 cron（110 默认如此）→ 打印 systemd timer / n8n 两套现成配置，择一启用

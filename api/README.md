# Sb.Apps.Pm — 赛博项目管理平台（后端）

> 基于 SB.Framework NuGet 的派生应用 — 提供项目 / 需求 / 任务 / 思维导图 / 构建发布 / AI / 聊天 等 11 业务域。

## 工程拓扑

```
sb-apps-pm
├── Sb.Apps.Pm.Models     PM 业务模型（11 域 + Dtos + Enums）
├── Sb.Apps.Pm.Services   PM 业务服务 + IModuleInitializer + 配置/启动 hook
└── Sb.Apps.Pm.WebApi     启动壳 + Controllers + Program.cs + appsettings.json
```

引用：
- **SB.Framework.* NuGet（0.1.0-preview.4）** — 基座（Models / Services / WebApi / Analyzers）
- **SB.Chat / SB.Chat.Telegram** — Chat 模块（暂用 ProjectReference 引 `../framework/sb-framework-chat/`，待 sb-chat 发 NuGet 后切包）

## 本地开发

```bash
# 前置: SB.Framework 包必须能访问（配 saibo-framework NuGet 源）
git clone git@gitlab.saibo.club:saibo/apps/sb-apps-pm.git
cd sb-apps-pm
# 配 NuGet 凭据（一次性）
dotnet nuget add source https://gitlab.saibo.club/api/v4/groups/63/-/packages/nuget/index.json \
  --name saibo-framework \
  --username <你的 GitLab 用户名> \
  --password <你的 GitLab Personal Access Token> \
  --store-password-in-clear-text

# 编译 + 启动
dotnet restore sb-apps-pm.sln
dotnet build sb-apps-pm.sln
dotnet run --project Sb.Apps.Pm.WebApi --urls=http://localhost:6020
```

启动后访问 `http://localhost:6020/api/Setup/Status` 应返回 `{"installed":false}`（首次未安装）。

## 数据库

- 库名 `SbPm`（不可改）
- 表前缀 `sb_pm_*`（PM 业务）+ `sb_*`（基座共享，来自 SB.Framework.Models）
- ORM：FreeSql + MySQL

## 贡献

详见 [CONTRIBUTING.md](./CONTRIBUTING.md)。基本规范跟 [sb-framework](https://gitlab.saibo.club/saibo/framework/sb-framework) 一致。

完整命名规范：[Saibo 命名规范 v1.0](https://gitlab.saibo.club/saibo/framework/sb-framework/-/blob/main/docs/naming-conventions.md)

## License

Proprietary — 赛博科技内部使用。

# 贡献 sb-apps-pm

> 派生应用项目。允许写 PM 业务，但不要改基座（SB.Framework）— 那是另外的仓库。

## 1. 业务边界

**可以写**：
- PM 业务实体（在 11 业务域之一 — Projects / Requirements / Tasks / FeatureModules / MindMaps / Pipelines / DbMigration / Servers / Documents / Ai / Chat）
- PM 业务 Service / Controller
- PM 项目特有的系统设置 / 菜单 / 种子数据（通过 IModuleInitializer 声明）
- PM 业务专属 hook 实现（IFreeSqlConfigurator / IStartupSyncHook）

**不能写**（应该提到 [sb-framework](https://gitlab.saibo.club/saibo/framework/sb-framework) 仓库）：
- 基座工具类 / 中间件
- 通用 Auth / License / Cache / Menu / 等基础能力
- 跨派生项目复用的功能

## 2. 命名规范

- 命名空间：`Sb.Apps.Pm.<Layer>.<Domain>`（如 `Sb.Apps.Pm.Models.Projects`）
- 表前缀：`sb_pm_*`
- 详见 [Saibo 命名规范 §6](https://gitlab.saibo.club/saibo/framework/sb-framework/-/blob/main/docs/naming-conventions.md#6-数据库表名)

## 3. 业务域划分（必须遵守）

新增 Entity / Service / Controller 必须归入以下 11 域之一：

| 域 | 子目录 |
|----|--------|
| Projects | `Models/Projects/` / `Services/Projects/` / `WebApi/Controllers/Projects/` |
| Requirements | 同上 |
| Tasks | |
| FeatureModules | |
| MindMaps | |
| Pipelines | (构建/发布/Git/部署相关) |
| DbMigration | |
| Servers | |
| Documents | |
| Ai | |
| Chat | (Telegram 等聊天平台业务) |

新增第 12 个域必须发 issue 讨论。

## 4. PR 必经流程

1. 从 `main` 拉分支：`feature/<short>` / `fix/<issue>-<short>` / `refactor/<short>`
2. 本地通过：`dotnet build sb-apps-pm.sln`
3. 提交：中文 Conventional Commits + scope 用业务域名（`feat(projects): xxx` / `fix(chat): xxx`）
4. MR 模板填写
5. 至少 1 approver
6. CI 全绿

## 5. SB.Framework 版本升级

升级 SB.Framework.* NuGet 版本时：
1. 改三个 csproj 的 `<PackageReference Version="0.x.y" />`
2. 跑 `dotnet restore` + `dotnet build` 验证
3. 跑 golden path 手测（admin 登录 + Project CRUD + Requirement CRUD 等）
4. CHANGELOG 记录"升 SB.Framework 0.x.y"

## 6. 跨派生独立

本项目跟其他派生（sb-apps-erp / sb-apps-crm / 等）**完全独立**。不允许 import / 引用其他派生项目的代码。共享需求请提到 sb-framework。

# Changelog

## [Unreleased]

## [Initial] - 2026-05-11

### Added

- 仓库初始化：从原 `sb-pm/pm-api/Sb.Apps.Pm.*` 迁移到 `saibo/apps/sb-apps-pm`（治理基线下重建）
- 3 个 .NET 8 工程：
  - `Sb.Apps.Pm.Models` — PM 业务模型（11 业务域）
  - `Sb.Apps.Pm.Services` — PM 业务服务 + IModuleInitializer 实现
  - `Sb.Apps.Pm.WebApi` — 启动壳 + Controllers
- 引用 SB.Framework.* @ 0.1.0-preview.4（PackageReference）
- 引用 sb-framework-chat（ProjectReference，待发包后切 NuGet）
- 治理基线：README / CONTRIBUTING / CODEOWNERS / CI / MR 模板 / CHANGELOG

# Changelog

## [Unreleased]

## [PM-Fork] - 2026-05-11

### Added

- **派生自 sb-framework-admin** — 通过 git fork 模式建立，保留完整 git history
- upstream 关联：`saibo/framework/sb-framework-admin` — 可 `git merge upstream/main` 同步基座更新
- 用途：作为赛博项目管理平台（PM）的前端，对接 [sb-apps-pm](https://gitlab.saibo.club/saibo/apps/sb-apps-pm) 后端

### TODO

- 添加 PM 业务 views（11 业务域）
- 配置后端 API 端点指向 sb-apps-pm（端口 6020）
- 品牌定制：app-info.json 设置"赛博项目管理平台"

---

## 早期历史（继承自 sb-framework-admin）

[Initial] - 2026-05-11

- 仓库初始化：从原 `sb-admin` 迁移到 `saibo/framework/sb-framework-admin`
- 基于 Vben Admin Vue 3 monorepo（pnpm workspace）
- 治理基线：README / CONTRIBUTING / CODEOWNERS / CI / MR 模板
- 派生模式：派生项目通过 git fork 而非 npm install 使用

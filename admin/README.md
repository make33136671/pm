# Sb.Apps.Pm.Admin — 赛博项目管理平台（前端）

> 派生自 [sb-framework-admin](https://gitlab.saibo.club/saibo/framework/sb-framework-admin)，针对 PM 业务定制。

## 派生关系

- **upstream**: `saibo/framework/sb-framework-admin`（前端基座 + Vben Admin）
- **origin**: `saibo/apps/sb-apps-pm-admin`（本仓库 — PM 派生）

跟随上游更新：

```bash
git fetch upstream
git merge upstream/main
```

合并规则见 [docs/AI_Fork_Guide.md](./docs/AI_Fork_Guide.md)。

## 添加 PM 业务

PM 业务 view 放在 `apps/web-antd/src/views/` 下，按业务域分子目录（kebab-case 复数）：

```
apps/web-antd/src/views/
├── projects/            (Project / SubProject / ProjectCustomer / etc.)
├── requirements/
├── tasks/
├── feature-modules/
├── mind-maps/
├── pipelines/           (Build / Release / Git / Deploy / etc.)
├── db-migration/
├── servers/
├── documents/
├── ai/
└── chat/
```

后端 API: `https://gitlab.saibo.club/saibo/apps/sb-apps-pm`（端口 6020）

## 本地开发

```bash
git clone git@gitlab.saibo.club:saibo/apps/sb-apps-pm-admin.git
cd sb-apps-pm-admin
pnpm install
pnpm dev:antd           # 默认 :5173
```

## 贡献

跟 sb-framework-admin 同样规范：
- 颜色不硬编码（用 antd v5 cssVar）
- 操作入口用实体按钮
- 命名 kebab-case（views）/ PascalCase（components）
- commit message 中文 Conventional Commits + scope=业务域

详见 [CONTRIBUTING.md](./CONTRIBUTING.md)。

## 文档

- [Saibo 命名规范](https://gitlab.saibo.club/saibo/framework/sb-framework/-/blob/main/docs/naming-conventions.md)
- [AI_Conventions.md](./AI_Conventions.md) — 前端编码约定
- [README.vben.md](./README.vben.md) — Vben Admin 原 README

## License

继承 Vben Admin 原 [LICENSE](./LICENSE)（MIT）+ 赛博科技 PM 定制部分专有。

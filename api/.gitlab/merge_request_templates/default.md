## 变更摘要

## 类型 / 域

类型：[ ] feat [ ] fix [ ] refactor [ ] chore [ ] docs

业务域（必填）：[ ] projects [ ] requirements [ ] tasks [ ] feature-modules [ ] mind-maps [ ] pipelines [ ] db-migration [ ] servers [ ] documents [ ] ai [ ] chat [ ] cross-cutting

## Checklist

- [ ] 命名空间在 `Sb.Apps.Pm.*` 下
- [ ] 表前缀 `sb_pm_*`（如有新表）
- [ ] 业务归入 11 域之一
- [ ] 没有跨派生引用（不 import sb-apps-erp / sb-apps-crm 等）
- [ ] commit message 中文 Conventional Commits + scope=业务域
- [ ] 本地 `dotnet build sb-apps-pm.sln` 通过

## 测试

<!-- 怎么验证？涉及哪些 API / 页面？ -->

## 数据库变更

- [ ] 涉及新表 / 字段变更 — 已通过 IModuleInitializer 声明 / 已写 migration 脚本

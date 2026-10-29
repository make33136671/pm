# AgentToken UI 界面开发计划

> **背景**: AgentToken 增强后端已上线，前端页面 / API 层 / 菜单注册均已就位。基础链路可用，本计划聚焦功能增强与可视化。
> **目标**: 在已可用的 AgentToken 管理页基础上，补齐 Scope 选择器、详情、审计独立页与监控面板。
> **状态**: Phase 1-3 待实施（Phase 0 经核验基础链路已就绪，无阻断）
> **创建时间**: 2026-06-02
> **版本**: v1.1 (2026-06-02 经代码核验修正 Phase 0 误判)

---

## ⚠️ v1.1 修正说明（2026-06-02 · Claude 对照真实代码核验）

v1.0 的 Phase 0 三个"🔴 阻断问题"经核验**与真实代码矛盾，已修正**：

| v1.0 断言 | 核验结果 | 证据 |
|-----------|---------|------|
| 🔴 菜单未注册 | ❌ 误判，**已注册** | `AgentTokenService.GetMenus()` 已返回 `/system/agent-token` → `/agent-token/index` 菜单 |
| 🔴 API 路径不匹配 `/AgentToken` vs `/admin/AgentToken` | ❌ 误判，**路径正确** | 前端 `apiURL` 已含 `/admin` 前缀（`.env.production=/admin`、`.env.development=…:6020/admin`），`api.get('/AgentToken/List')` 实际请求即 `/admin/AgentToken/List`，与全项目 api.ts 惯例（`/MindMap`、`/BuildRecord` 都不带 `/admin`）一致 |
| 🔴 按 0.1 改成 `/admin/AgentToken/*` | ⚠️ **会造成回归** | 改后变 `/admin/admin/AgentToken/*` → 404，把本来正常的页面改坏。**不要执行原 0.1** |
| 🟡 旧 Controller v2.0 残留待删 | ⚠️ 不准确 | 实有两个职责不同的 Controller：`Controllers/Admin/`（JwtAdmin，已被 12 个集成测试覆盖）与 `Controllers/Agent/`（标 v2.1）。namespace/路由不同，**不可盲删**，需先确认调用方 |

**结论**：基础链路（菜单 + 路由 + API 路径 + CRUD 页面）已就绪，Phase 0 无真实阻断。建议直接从「先实跑验证」开始，再进入 Phase 1。

---

## 当前状态分析

### 已有代码（前端）

| 文件 | 路径 | 状态 | 说明 |
|------|------|------|------|
| API 层 | `views/agent-token/api.ts` | ✅ 完整且路径正确 | 7 个端点已封装，前缀走 baseURL `/admin` |
| 主页面 | `views/agent-token/index.vue` | ✅ 基本完整 | 510 行，CRUD + 审计抽屉 + 一次性 Token 弹窗，无 TODO |
| 菜单注册 | `AgentTokenService.GetMenus()` | ✅ 已注册 | `/system/agent-token` → `/agent-token/index` |

### 关键问题（v1.1 核验后）

| 问题 | 严重度 | 说明 |
|------|--------|------|
| ~~菜单未注册~~ | ✅ 已解决 | `GetMenus()` 已定义，见上表与 v1.1 修正说明 |
| ~~路由未注册~~ | ✅ 已解决 | 后端动态菜单驱动，菜单已注册即有路由 |
| ~~API 路径不匹配~~ | ✅ 非问题 | baseURL 已含 `/admin`，路径正确（详见 v1.1 修正说明） |
| **Token 明文残留内存** | 🟡 安全 | `secretInfo` 仅用 `ref`，关闭 Modal 后仍可 JS 获取 → 改为用完即清（Phase 1） |
| 两个同名 Controller 职责区分 | 🟢 待确认 | Admin/ 与 Agent/ 各一个，需确认调用方后再决定是否合并，**非废代码** |

### 技术栈确认

| 维度 | 技术选型 |
|------|---------|
| 框架 | Vue 3 + Vben Admin 5.5.9 (Ant Design 变体) |
| 包管理 | pnpm workspaces + catalog |
| HTTP | Axios → `@vben/request` → `api.get/post()` |
| 表格 | vxe-table (`useVbenVxeGrid`) + Ant Design Vue Table |
| 表单 | Ant Design Vue Form + VbenForm |
| 菜单 | 后端 `IModuleInitializer.GetMenus()` → `Menu` 表 → 前端动态路由 |

---

## 实施计划

### Phase 0: 验证现状 + 安全收尾 (~20 min)

**目标**: 用实跑确认基础链路可用（菜单 + 路由 + API + CRUD 已就绪），并补上唯一确有的安全问题。

> v1.0 原 Phase 0 的「修 API 路径 / 注册菜单 / 删旧 Controller」经核验为误判，已删除（详见顶部 v1.1 修正说明）。**切勿执行原 0.1 改路径**，会导致 `/admin/admin/...` 404 回归。

| # | 任务 | 文件 | 说明 | 优先级 |
|---|------|------|------|--------|
| 0.1 | 端到端实跑验证 | 测试机浏览器 | 访问 192.168.10.73:6020 登录后台 → 侧边栏「Agent Token 管理」可见 → 页面加载 → 增删改查走通 | 🔴 P0 |
| 0.2 | Token 明文用完即清 | `agent-token/index.vue` | 一次性 Token 弹窗关闭后清空 `secretInfo`（避免 JS 仍可读取明文） | 🟡 P1 |
| 0.3 | 确认双 Controller 职责区分 | `Controllers/Admin/` + `Controllers/Agent/` | Admin/ 被 12 个集成测试覆盖 + Agent/ 为 v2.1 增强版 → **确认保留两个，不合并不删除** | 🟢 P2 |

> 若 0.1 实跑发现某项确实不通（如菜单图标/排序、某端点报错），再据实补具体修复项——而不是预设"阻断"。

### Phase 1: 功能增强 (~60 min)

**目标**: 完善 Token 管理功能

| # | 任务 | 说明 | 优先级 |
|---|------|------|--------|
| 1.1 | Scope 可视化选择器 | 用 Checkbox 组替代手动输入逗号分隔的 scopes，提供常用预设组合 | 🟡 P1 |
| 1.2 | Token 复制快捷方式 | 列表页直接复制 Token 前缀，方便在环境变量中引用 | 🟢 P2 |
| 1.3 | 搜索增强 | 支持按 Scope、创建者、状态筛选 | 🟡 P1 |
| 1.4 | 批量操作 | 批量吊销过期 Token | 🟢 P2 |
| 1.5 | Token 详情面板 | 点击行展开详情：创建信息、修改历史、当前速率状态 | 🟡 P1 |

### Phase 2: 审计日志独立页面 (~45 min)

**目标**: 将审计功能从抽屉升级为独立页面

| # | 任务 | 说明 | 优先级 |
|---|------|------|--------|
| 2.1 | 审计日志列表页 | `views/agent-token/audit/index.vue` — 全局审计日志，支持 Token/时间/动作/结果筛选 | 🟡 P1 |
| 2.2 | 审计日志统计卡片 | 总请求数、成功率、Top Token 排行 | 🟢 P2 |
| 2.3 | 导出功能 | CSV 导出审计日志 | 🟢 P2 |
| 2.4 | 注册独立菜单项 | "审计日志" 作为 "Agent Token 管理" 子菜单 | 🔴 P1 |

### Phase 3: 使用监控面板 (~60 min)

**目标**: 可视化 Token 使用情况

| # | 任务 | 说明 | 优先级 |
|---|------|------|--------|
| 3.1 | 使用统计概览 | 各 Token 近 7 天请求趋势（折线图） | 🟢 P2 |
| 3.2 | 速率限制状态 | 实时显示各 Token 当前速率使用情况（进度条） | 🟢 P2 |
| 3.3 | 异常告警 | 高拒绝率 Token 标红提醒 | 🟢 P2 |

---

## 文件结构规划

```
views/agent-token/
├── api.ts              # API 层 (Phase 0 修复路径)
├── index.vue           # Token 列表管理页 (Phase 1 增强)
├── components/
│   ├── ScopeSelector.vue    # Scope 可视化选择器 (Phase 1.1)
│   ├── TokenDetail.vue      # Token 详情面板 (Phase 1.5)
│   └── UsageStats.vue       # 使用统计卡片 (Phase 3)
└── audit/
    └── index.vue   # 审计日志独立页面 (Phase 2)
```

### ⚠️ 预审发现 & 待确认 (2026-06-02)

| # | 问题 | 影响 | 需确认 |
|---|------|------|--------|
| 1 | 文件结构注释过时 | `api.ts` 注释说 "Phase 0 修复路径"，但 API 路径无需修复 | 更新注释 |
| 2 | 风险表过时 | "API 路径修复需重构建" / "旧 Controller 冲突" 已不是风险 | 清理风险表 |
| 3 | 双 Controller 0.3 方向模糊 | 应为"确认保留"而非"决定是否合并" | 明确结论 |
| 4 | Phase 0.1 环境 | 无本地 pnpm dev 环境，应改为"测试机浏览器直接验证" | 调整执行方式 |
| 5 | Scope 选择器设计 | 需定义有效 Scope 列表 + 预设组合 | 设计前明确 |
| 6 | 审计全局查询 | 需确认 Audit API 不传 `id` 是否返回所有 Token 日志 | API 测试 |

---

## 开发进度跟踪

| Phase | 状态 | 计划时间 | 实际时间 | 完成日期 | 备注 |
|-------|------|---------|---------|---------|------|
| Phase 0: 验证现状 + 安全收尾 | ⏳ 待开始 | ~20 min | — | — | 基础链路已就绪，无阻断；重在实跑验证 |
| Phase 1: 功能增强 | ⏳ 待开始 | ~60 min | — | — | — |
| Phase 2: 审计日志独立页面 | ⏳ 待开始 | ~45 min | — | — | — |
| Phase 3: 使用监控面板 | ⏳ 待开始 | ~60 min | — | — | 可选 |

**预计总工时**: ~2.5 小时 (Phase 0-2) / ~3.5 小时 (含 Phase 3)

---

## 风险与依赖

| 风险 | 缓解措施 |
|------|---------|
| 前端构建环境未就绪 | 需确认 pnpm 环境 + 构建产物部署到 Nginx 的方式 |
| Scope 选择器有效值定义 | 开发前需梳理后端 AgentScope 枚举的所有合法值 |
| 审计 API 全局查询支持 | 确认 Audit 端点不传 `id` 时是否返回全量数据 |

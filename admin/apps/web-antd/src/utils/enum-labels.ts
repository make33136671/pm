/**
 * 枚举值中文映射 —— 代码层面保持英文，UI 显示统一用中文
 */

// ========== 项目 ==========

export const projectStatusLabels: Record<string, string> = {
  Planning: '规划中',
  Active: '进行中',
  OnHold: '已暂停',
  Completed: '已完成',
  Archived: '已归档',
};

// ========== 需求 ==========

export const requirementStatusLabels: Record<string, string> = {
  Draft: '草稿',
  Todo: '待办',
  InProgress: '进行中',
  Testing: '测试中',
  Done: '已完成',
  Closed: '已关闭',
};

export const requirementTypeLabels: Record<string, string> = {
  Feature: '功能',
  Bugfix: '修复',
  Improvement: '优化',
  Task: '任务',
  Research: '调研',
};

export const complexityLabels: Record<string, string> = {
  Low: '简单',
  Medium: '中等',
  High: '复杂',
  VeryHigh: '非常复杂',
};

export const requirementSourceLabels: Record<string, string> = {
  Manual: '手动',
  Telegram: 'Telegram',
  AI: 'AI',
  Git: 'Git',
  Hermes: 'Hermes',
};

// 任务来源（自由字符串，由 Bot / Agent 写入）
export const taskSourceLabels: Record<string, string> = {
  manual: '手动',
  tg_bot: 'Telegram',
  ai_auto: 'AI',
  hermes: 'Hermes',
};

// ========== 思维导图 ==========

export const mindMapTypeLabels: Record<string, string> = {
  Requirement: '需求导图',
  CodeStructure: '代码结构',
  ModuleDependency: '模块依赖',
};

export const generatedByLabels: Record<string, string> = {
  Manual: '手动',
  Auto: '自动',
};

// ========== 构建 ==========

export const buildStatusLabels: Record<string, string> = {
  PendingApproval: '待审批',
  Approved: '已审批',
  Building: '构建中',
  Success: '成功',
  Failed: '失败',
  Rejected: '已驳回',
};

// ========== 流水线配置 ==========

export const versionStrategyLabels: Record<string, string> = {
  AutoIncrement: '自动递增',
  Semantic: '语义化手动',
  Timestamp: '时间戳',
};

export const triggerModeLabels: Record<string, string> = {
  Manual: '手动触发',
  MergeRequest: 'MR 合并触发',
  TagPush: 'Tag 推送触发',
};

export const deployModeLabels: Record<string, string> = {
  None: '仅构建',
  GitLabDeploy: 'GitLab Pipeline 部署',
  SbpmDeploy: 'SB.PM 自行部署',
};

// 部署产物来源(怎么出产物)
export const buildSourceLabels: Record<string, string> = {
  GitLabCi: 'GitLab CI 产物',
  LocalBuild: '本机构建',
  LastArtifact: '复用上次产物',
};

// 部署策略(怎么切换上线,与部署通道正交)
export const deployStrategyLabels: Record<string, string> = {
  Replace: '原地覆盖',
  BlueGreen: '蓝绿部署',
  Rolling: '滚动更新',
  Canary: '灰度发布',
};

export const buildTypeLabels: Record<string, string> = {
  Manual: '手动',
  CI: 'CI',
  Scheduled: '定时',
};

// ========== 发布 ==========

export const releaseStatusLabels: Record<string, string> = {
  Pending: '待审批',
  Approved: '已审批',
  Deploying: '部署中',
  Success: '成功',
  Failed: '失败',
  Rollback: '已回滚',
};

export const approvalStatusLabels: Record<string, string> = {
  Pending: '待审批',
  Approved: '已通过',
  Rejected: '已拒绝',
};

export const deployMethodLabels: Record<string, string> = {
  BuiltInApi: '内置API',
  FTP: 'FTP',
  BtPanel: '宝塔面板',
  SSH: 'SSH',
  Manual: '手动',
};

// ========== 服务器管理 ==========

export const serverStatusLabels: Record<string, string> = {
  Active: '正常',
  Disabled: '已禁用',
  Unreachable: '不可达',
};

export const serverTypeLabels: Record<string, string> = {
  Cloud: '云服务器',
  Dedicated: '物理机',
  Local: '本地服务器',
  VPS: 'VPS',
};

export const databaseTypeLabels: Record<string, string> = {
  MySQL: 'MySQL',
  PostgreSQL: 'PostgreSQL',
  SQLite: 'SQLite',
  MSSQL: 'SQL Server',
  MongoDB: 'MongoDB',
};

export const paymentCycleLabels: Record<string, string> = {
  Monthly: '月付',
  Quarterly: '季付',
  Yearly: '年付',
  OneTime: '一次性',
};

// ========== Git ==========

export const pipelineStatusLabels: Record<string, string> = {
  Pending: '等待中',
  Running: '运行中',
  Success: '成功',
  Failed: '失败',
  Cancelled: '已取消',
};

// ========== AI 任务 ==========

export const aiTaskStatusLabels: Record<string, string> = {
  Pending: '等待中',
  Running: '运行中',
  Completed: '已完成',
  Failed: '失败',
};

export const aiTaskTypeLabels: Record<string, string> = {
  AnalyzeChat: '分析对话',
  ExtractRequirement: '提取需求',
  FixBug: '修复Bug',
  GenerateChangelog: '生成日志',
  GenerateMindMap: '生成导图',
};

// ========== 定时任务 ==========

export const scheduledTaskStatusLabels: Record<string, string> = {
  Active: '运行中',
  Paused: '已暂停',
  Disabled: '已禁用',
};

export const scheduledTaskTypeLabels: Record<string, string> = {
  GitSync: 'Git同步',
  Backup: '备份',
  Build: '构建',
  CleanBackup: '清理备份',
  AiAnalyze: 'AI分析',
};

// ========== 审计日志 ==========

export const auditModuleLabels: Record<string, string> = {
  Config: '配置',
  Release: '发布',
  Approval: '审批',
  Sensitive: '敏感操作',
};

// ========== 任务系统 ==========

export const taskItemTypeLabels: Record<string, string> = {
  Development: '开发',
  Bug: '缺陷',
  Design: '设计',
  Testing: '测试',
  Docs: '文档',
  Other: '其他',
};

export const taskItemClaimStatusLabels: Record<string, string> = {
  Pending: '待接单',
  Claimed: '已接单',
  Delivered: '已交付',
};

export const taskItemExecStatusLabels: Record<string, string> = {
  Idle: '未执行',
  Running: '执行中',
  Success: '成功',
  Failed: '失败',
  Timeout: '超时',
  Hibernated: '休眠',
};

export const taskItemExecModeLabels: Record<string, string> = {
  Api: 'API 接单',
  Ai: 'AI 执行',
};

// ========== AI 提供商 ==========

export const aiProviderStatusLabels: Record<string, string> = {
  Active: '启用',
  Disabled: '禁用',
};

// ========== 客户状态 ==========

export const customerStatusLabels: Record<string, string> = {
  Active: '合作中',
  Expired: '已到期',
  Terminated: '已终止',
};

// ========== 通用状态 ==========

export const commonStatusLabels: Record<string, string> = {
  Active: '启用',
  Disabled: '禁用',
};

// ========== 背景任务日志 ==========

export const logLevelLabels: Record<string, string> = {
  Error: '错误',
  Warning: '警告',
  Info: '信息',
};

/**
 * 通用翻译函数：从映射表获取中文，找不到则返回原值
 */
export function t(map: Record<string, string>, value: string): string {
  return map[value] ?? value;
}

/**
 * 将映射表转为 Select options 数组
 */
export function toOptions(map: Record<string, string>) {
  return Object.entries(map).map(([value, label]) => ({ label, value }));
}

/**
 * 共享列渲染配置
 * 需求列表、任务列表的颜色映射和标签映射统一在此定义
 * 修改此处，所有引用的页面（独立页面 + 项目详情内嵌）自动生效
 */

// ==================== 需求 ====================

export const reqStatusColorMap: Record<string, string> = {
  Draft: 'default', Todo: 'blue', InProgress: 'processing',
  Testing: 'orange', Done: 'green', Closed: 'default',
};

export const reqTypeColorMap: Record<string, string> = {
  Feature: 'blue', Bugfix: 'red', Improvement: 'green', Task: 'default', Research: 'purple',
};

export const reqComplexityColorMap: Record<string, string> = {
  Low: 'green', Medium: 'blue', High: 'orange', VeryHigh: 'red',
};

export const reqProgressMap: Record<string, number> = {
  Draft: 0, Todo: 10, InProgress: 50, Testing: 80, Done: 100, Closed: 100,
};

// ==================== 任务 ====================

export const taskTypeColorMap: Record<string, string> = {
  Development: 'blue', Bug: 'red', Design: 'purple', Testing: 'orange', Docs: 'cyan', Other: 'default',
};

export const taskComplexityColorMap: Record<string, string> = {
  Low: 'green', Medium: 'blue', High: 'orange', VeryHigh: 'red',
};

export const taskClaimColorMap: Record<string, string> = {
  Pending: 'default', Claimed: 'blue', Delivered: 'green',
};

export const taskExecColorMap: Record<string, string> = {
  Idle: 'default', Running: 'processing', Success: 'green', Failed: 'red', Timeout: 'orange', Hibernated: 'purple',
};

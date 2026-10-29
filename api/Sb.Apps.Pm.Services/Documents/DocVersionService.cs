using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Documents
{
    [AutoDependencyInjection]
    public class DocVersionService : BaseCrudService<DocVersion>
    {
        private readonly IFreeSql _fsql;

        public DocVersionService(IFreeSql fsql, ILogger<DocVersionService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        /// <summary>保存新版本（创建或编辑时调用）</summary>
        public DocVersion SaveVersion(string sourceType, int sourceId, string? content, string? changeNote, string? operatorName)
        {
            int nextVersion = (_fsql.Select<DocVersion>()
                .Where(d => d.SourceType == sourceType && d.SourceId == sourceId)
                .Max(d => d.Version)) + 1;

            var doc = new DocVersion
            {
                SourceType = sourceType,
                SourceId = sourceId,
                Version = nextVersion,
                Content = content,
                ChangeNote = changeNote,
                Operator = operatorName,
                CreatedTime = DateTime.Now,
            };
            this.Insert(doc);
            return doc;
        }

        /// <summary>获取版本列表</summary>
        public Result GetVersions(string sourceType, int sourceId)
        {
            var list = Select.Where(d => d.SourceType == sourceType && d.SourceId == sourceId)
                .OrderByDescending(d => d.Version)
                .ToList(d => new { d.Id, d.Version, d.ChangeNote, d.Operator, d.CreatedTime });
            return Result.Success(list);
        }

        /// <summary>获取指定版本内容</summary>
        public Result GetVersionDetail(int id)
        {
            var doc = Select.Where(d => d.Id == id).First();
            if (doc == null) return Result.Fail("版本不存在!");
            return Result.Success(doc);
        }

        /// <summary>获取最新版本内容</summary>
        public Result GetLatest(string sourceType, int sourceId)
        {
            var doc = Select.Where(d => d.SourceType == sourceType && d.SourceId == sourceId)
                .OrderByDescending(d => d.Version).First();
            if (doc == null) return Result.Fail("暂无文档!");
            return Result.Success(doc);
        }

        /// <summary>回档到指定版本 —— 将该版本内容写回源实体并创建新版本记录</summary>
        public Result Rollback(string sourceType, int sourceId, int versionId, string? operatorName)
        {
            var target = Select.Where(d => d.Id == versionId).First();
            if (target == null) return Result.Fail("目标版本不存在!");

            // 写回源实体的 Content 字段
            if (sourceType == "Requirement")
            {
                _fsql.Update<Requirement>().Where(r => r.Id == sourceId)
                    .Set(r => r.Content, target.Content)
                    .Set(r => r.UpdatedTime, DateTime.Now)
                    .ExecuteAffrows();
            }
            else if (sourceType == "TaskItem")
            {
                _fsql.Update<TaskItem>().Where(t => t.Id == sourceId)
                    .Set(t => t.Content, target.Content)
                    .Set(t => t.UpdatedTime, DateTime.Now)
                    .ExecuteAffrows();
            }

            // 创建新版本记录（标记为回档）
            SaveVersion(sourceType, sourceId, target.Content, $"回档到 v{target.Version}", operatorName);

            return Result.Success("回档成功!");
        }

        /// <summary>构建需求 MD 文档</summary>
        public static string BuildRequirementMd(Requirement req)
        {
            var lines = new List<string>
            {
                $"# {req.Title}",
                "",
                "| 属性 | 值 |",
                "|------|------|",
                $"| 类型 | {L(RequirementTypeMap, req.Type)} |",
                $"| 复杂度 | {L(ComplexityMap, req.Complexity)} |",
                $"| 状态 | {L(RequirementStatusMap, req.Status)} |",
                $"| 来源 | {L(RequirementSourceMap, req.Source)} |",
                $"| 负责人 | {req.Assignee ?? "-"} |",
                $"| 预估工时 | {req.EstimatedHours?.ToString() ?? "-"} |",
                $"| 截止日期 | {req.DueDate?.ToString("yyyy-MM-dd") ?? "-"} |",
                "",
                "## 需求描述",
                "",
                req.Content ?? "",
            };
            return string.Join("\n", lines);
        }

        /// <summary>构建任务 MD 文档</summary>
        public static string BuildTaskItemMd(TaskItem task)
        {
            var lines = new List<string>
            {
                $"# {task.Title}",
                "",
                "| 属性 | 值 |",
                "|------|------|",
                $"| 类型 | {L(TaskItemTypeMap, task.Type)} |",
                $"| 接单状态 | {L(ClaimStatusMap, task.ClaimStatus)} |",
                $"| 执行状态 | {L(ExecStatusMap, task.ExecStatus)} |",
                $"| 执行模式 | {L(ExecModeMap, task.ExecMode)} |",
                $"| 负责人 | {task.Assignee ?? "-"} |",
                $"| 接单者 | {task.ClaimedBy ?? "-"} |",
                $"| 最大重试 | {task.MaxRetryCount} |",
                $"| 超时(秒) | {task.TimeoutSeconds} |",
                $"| 截止日期 | {task.DueDate?.ToString("yyyy-MM-dd") ?? "-"} |",
                "",
                "## 任务描述",
                "",
                task.Content ?? "",
            };
            return string.Join("\n", lines);
        }

        #region 枚举中文映射

        private static string L<T>(Dictionary<T, string> map, T value) where T : notnull
            => map.TryGetValue(value, out var label) ? label : value.ToString()!;

        private static readonly Dictionary<RequirementType, string> RequirementTypeMap = new()
        {
            [RequirementType.Feature] = "功能",
            [RequirementType.Bugfix] = "修复",
            [RequirementType.Improvement] = "优化",
            [RequirementType.Task] = "任务",
            [RequirementType.Research] = "调研",
        };

        private static readonly Dictionary<RequirementStatus, string> RequirementStatusMap = new()
        {
            [RequirementStatus.Draft] = "草稿",
            [RequirementStatus.Todo] = "待办",
            [RequirementStatus.InProgress] = "进行中",
            [RequirementStatus.Testing] = "测试中",
            [RequirementStatus.Done] = "已完成",
            [RequirementStatus.Closed] = "已关闭",
        };

        private static readonly Dictionary<Complexity, string> ComplexityMap = new()
        {
            [Complexity.Low] = "简单",
            [Complexity.Medium] = "中等",
            [Complexity.High] = "复杂",
            [Complexity.VeryHigh] = "非常复杂",
        };

        private static readonly Dictionary<RequirementSource, string> RequirementSourceMap = new()
        {
            [RequirementSource.Manual] = "手动",
            [RequirementSource.Telegram] = "Telegram",
            [RequirementSource.AI] = "AI",
            [RequirementSource.Git] = "Git",
        };

        private static readonly Dictionary<TaskItemType, string> TaskItemTypeMap = new()
        {
            [TaskItemType.Development] = "开发",
            [TaskItemType.Bug] = "缺陷",
            [TaskItemType.Design] = "设计",
            [TaskItemType.Testing] = "测试",
            [TaskItemType.Docs] = "文档",
            [TaskItemType.Other] = "其他",
        };

        private static readonly Dictionary<TaskItemClaimStatus, string> ClaimStatusMap = new()
        {
            [TaskItemClaimStatus.Pending] = "待接单",
            [TaskItemClaimStatus.Claimed] = "已接单",
        };

        private static readonly Dictionary<TaskItemExecStatus, string> ExecStatusMap = new()
        {
            [TaskItemExecStatus.Idle] = "未执行",
            [TaskItemExecStatus.Running] = "执行中",
            [TaskItemExecStatus.Success] = "成功",
            [TaskItemExecStatus.Failed] = "失败",
            [TaskItemExecStatus.Timeout] = "超时",
            [TaskItemExecStatus.Hibernated] = "休眠",
        };

        private static readonly Dictionary<TaskItemExecMode, string> ExecModeMap = new()
        {
            [TaskItemExecMode.Api] = "API 接单",
            [TaskItemExecMode.Ai] = "AI 执行",
        };

        #endregion
    }
}

using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Chat;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// 系统 → Telegram 通知推送服务
    /// 统一调度：查找项目关联的活跃群组，通过 Bot 发送通知
    /// </summary>
    [AutoDependencyInjection]
    public class ChatNotificationService
    {
        private readonly IFreeSql _fsql;
        private readonly ChatConnectorManager _connectorManager;
        private readonly ILogger<ChatNotificationService> _logger;

        public ChatNotificationService(IFreeSql fsql, ChatConnectorManager connectorManager,
            ILogger<ChatNotificationService> logger)
        {
            _fsql = fsql;
            _connectorManager = connectorManager;
            _logger = logger;
        }

        /// <summary>
        /// 推送通知到项目关联的所有活跃群组
        /// </summary>
        public async Task NotifyProject(int projectId, string message)
        {
            var groups = _fsql.Select<ChatGroupBinding>()
                .Where(g => g.ProjectId == projectId
                    && g.Status == ChatGroupBindingStatus.Active
                    && g.PlatformGroupId != null
                    && !g.IsDeleted)
                .ToList();

            foreach (var group in groups)
            {
                var bot = _connectorManager.GetBot(group.AccountId);
                if (bot == null) continue;

                try
                {
                    await bot.SendTextAsync(group.PlatformGroupId!, message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "推送通知失败: GroupId={GroupId}, ProjectId={ProjectId}",
                        group.Id, projectId);
                }
            }
        }

        // ---- 具体通知场景 ----

        /// <summary>任务状态变更通知</summary>
        public async Task NotifyTaskStatusChanged(TaskItem task, string oldStatus, string newStatus)
        {
            var statusIcons = new Dictionary<string, string>
            {
                { "Idle", "⏳" }, { "Running", "🔄" }, { "Success", "✅" }, { "Failed", "❌" }
            };

            var icon = statusIcons.GetValueOrDefault(newStatus, "📌");
            var assignee = !string.IsNullOrEmpty(task.Assignee) ? $" | 负责人: {task.Assignee}" : "";

            var message = $"{icon} <b>任务状态更新</b>\n\n" +
                          $"<b>{task.Title}</b>\n" +
                          $"{oldStatus} → {newStatus}{assignee}";

            await NotifyProject(task.ProjectId, message);
        }

        /// <summary>任务完成通知</summary>
        public async Task NotifyTaskCompleted(TaskItem task)
        {
            var hours = task.ActualHours.HasValue ? $" | 实际工时: {task.ActualHours}h" : "";
            var assignee = !string.IsNullOrEmpty(task.Assignee) ? $" | 负责人: {task.Assignee}" : "";

            var message = $"✅ <b>任务已完成</b>\n\n" +
                          $"<b>{task.Title}</b>{assignee}{hours}";

            await NotifyProject(task.ProjectId, message);
        }

        /// <summary>需求确认通知</summary>
        public async Task NotifyRequirementConfirmed(ChatRequirement requirement)
        {
            var message = $"✅ <b>需求已确认</b>\n\n" +
                          $"<b>{requirement.Title}</b>\n" +
                          $"{requirement.Description ?? ""}";

            await NotifyProject(requirement.ProjectId, message);
        }

        /// <summary>需求拒绝通知</summary>
        public async Task NotifyRequirementRejected(ChatRequirement requirement)
        {
            var message = $"❌ <b>需求已拒绝</b>\n\n" +
                          $"<b>{requirement.Title}</b>";

            await NotifyProject(requirement.ProjectId, message);
        }

        /// <summary>新需求提取通知（AI 分析完成后）</summary>
        public async Task NotifyNewRequirementsExtracted(int projectId, int count)
        {
            var message = $"🤖 <b>AI 需求分析完成</b>\n\n" +
                          $"共提取到 {count} 条新需求，请在管理后台查看并确认。";

            await NotifyProject(projectId, message);
        }

        /// <summary>自定义通知</summary>
        public async Task NotifyCustom(int projectId, string title, string content)
        {
            var message = $"📢 <b>{title}</b>\n\n{content}";
            await NotifyProject(projectId, message);
        }
    }
}

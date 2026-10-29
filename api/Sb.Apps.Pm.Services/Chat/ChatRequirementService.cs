using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// AI 提炼需求管理
    /// </summary>
    [AutoDependencyInjection]
    public class ChatRequirementService : BaseCrudService<ChatRequirement>
    {
        private readonly IFreeSql _fsql;

        public ChatRequirementService(IFreeSql fsql, ILogger<ChatRequirementService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public virtual Result GetList(int? groupBindingId = null, int? projectId = null,
            ChatRequirementStatus? status = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .Include(r => r.GroupBinding)
                .WhereIf(groupBindingId.HasValue, r => r.GroupBindingId == groupBindingId.Value)
                .WhereIf(projectId.HasValue, r => r.ProjectId == projectId.Value)
                .WhereIf(status.HasValue, r => r.Status == status.Value)
                .WhereIf(!string.IsNullOrEmpty(keyword), r => r.Title.Contains(keyword!) || r.Description!.Contains(keyword!));
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        /// <summary>
        /// 确认需求（含通知推送）
        /// </summary>
        public virtual Result Confirm(int id, int confirmedBy)
        {
            var req = Select.Where(r => r.Id == id).First();
            if (req == null) return Result.Fail("需求不存在!");
            if (req.Status != ChatRequirementStatus.Draft && req.Status != ChatRequirementStatus.PendingConfirm)
                return Result.Fail("当前状态不允许确认!");

            req.Status = ChatRequirementStatus.Confirmed;
            req.ConfirmedTime = DateTime.Now;
            req.ConfirmedBy = confirmedBy;

            UpdateDiy.UpdateColumns(r => new
            {
                r.Status, r.ConfirmedTime, r.ConfirmedBy
            }).SetSource(req).ExecuteAffrows();

            return Result.Success("需求已确认!");
        }

        /// <summary>
        /// 拒绝需求（含通知推送）
        /// </summary>
        public virtual Result Reject(int id)
        {
            var req = Select.Where(r => r.Id == id).First();
            if (req == null) return Result.Fail("需求不存在!");
            if (req.Status != ChatRequirementStatus.Draft && req.Status != ChatRequirementStatus.PendingConfirm)
                return Result.Fail("当前状态不允许拒绝!");

            req.Status = ChatRequirementStatus.Rejected;
            UpdateDiy.UpdateColumns(r => new { r.Status }).SetSource(req).ExecuteAffrows();

            return Result.Success("需求已拒绝!");
        }
    }
}

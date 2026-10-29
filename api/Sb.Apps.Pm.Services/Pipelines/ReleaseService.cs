using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Pipelines
{
    [AutoDependencyInjection]
    public class ReleaseService : BaseCrudService<ReleaseRecord>
    {
        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ReleaseService(IFreeSql fsql, ILogger<ReleaseService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        public Result GetList(int? projectId = null, int? environmentId = null, ReleaseStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(environmentId.HasValue, c => c.EnvironmentId == environmentId.Value)
                .WhereIf(status.HasValue, c => c.Status == status.Value)
                .Include(c => c.Project);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(ReleaseRecord record)
        {
            record.CreatedTime = DateTime.Now;
            record.Status = ReleaseStatus.Pending;
            record.OperatorName = _httpContextAccessor.HttpContext?.User?.Username();
            record = this.Insert(record);
            return Result.Success("发布记录创建成功!", record);
        }

        public Result Approve(int id, string? comment = null)
        {
            var record = Select.Where(c => c.Id == id).First();
            if (record == null) return Result.Fail("发布记录不存在!");

            var username = _httpContextAccessor.HttpContext?.User?.Username();

            record.ApprovalStatus = ApprovalStatus.Approved;
            record.ApprovedBy = username;
            record.ApprovedAt = DateTime.Now;
            if (record.Status == ReleaseStatus.Pending)
                record.Status = ReleaseStatus.Deploying;

            _fsql.Update<ReleaseRecord>().SetSource(record).ExecuteAffrows();

            _fsql.Insert(new ApprovalRecord
            {
                TargetType = "ReleaseRecord",
                TargetId = id,
                Action = "Approved",
                Comment = comment,
                OperatorName = username ?? "",
                CreatedTime = DateTime.Now
            }).ExecuteAffrows();

            return Result.Success("审批通过!");
        }

        public Result Reject(int id, string? comment = null)
        {
            var record = Select.Where(c => c.Id == id).First();
            if (record == null) return Result.Fail("发布记录不存在!");

            var username = _httpContextAccessor.HttpContext?.User?.Username();

            record.ApprovalStatus = ApprovalStatus.Rejected;
            record.ApprovedBy = username;
            record.ApprovedAt = DateTime.Now;

            _fsql.Update<ReleaseRecord>().SetSource(record).ExecuteAffrows();

            _fsql.Insert(new ApprovalRecord
            {
                TargetType = "ReleaseRecord",
                TargetId = id,
                Action = "Rejected",
                Comment = comment,
                OperatorName = username ?? "",
                CreatedTime = DateTime.Now
            }).ExecuteAffrows();

            return Result.Success("审批已驳回!");
        }

        public Result UpdateDeployStatus(int id, ReleaseStatus status, string? deployLog = null)
        {
            var record = Select.Where(c => c.Id == id).First();
            if (record == null) return Result.Fail("发布记录不存在!");

            record.Status = status;
            if (deployLog != null) record.DeployLog = deployLog;
            if (status == ReleaseStatus.Success || status == ReleaseStatus.Failed)
                record.FinishedAt = DateTime.Now;

            _fsql.Update<ReleaseRecord>().SetSource(record).ExecuteAffrows();
            return Result.Success("部署状态更新成功!");
        }

        public Result Rollback(int releaseId)
        {
            var original = Select.Where(c => c.Id == releaseId).First();
            if (original == null) return Result.Fail("发布记录不存在!");

            var rollbackRecord = new ReleaseRecord
            {
                ProjectId = original.ProjectId,
                EnvironmentId = original.EnvironmentId,
                Version = original.Version,
                Status = ReleaseStatus.Rollback,
                RollbackFromId = original.Id,
                ReleaseNote = $"回滚自发布 #{original.Id} (v{original.Version})",
                CreatedTime = DateTime.Now,
                OperatorName = _httpContextAccessor.HttpContext?.User?.Username()
            };

            rollbackRecord = this.Insert(rollbackRecord);
            return Result.Success("回滚记录创建成功!", rollbackRecord);
        }
    }
}

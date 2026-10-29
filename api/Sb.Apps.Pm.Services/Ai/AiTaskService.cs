using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Ai
{
    [AutoDependencyInjection]
    public class AiTaskService : IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "AI 任务";
        public int InitOrder => 191;
        public Type[] GetEntityTypes() => new[] { typeof(AiTask) };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly ILogger<AiTaskService> _logger;

        public AiTaskService(IFreeSql fsql, ILogger<AiTaskService> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        public Result GetList(int? projectId = null, AiTaskType? taskType = null, AiTaskStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<AiTask>()
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(taskType.HasValue, c => c.TaskType == taskType.Value)
                .WhereIf(status.HasValue, c => c.Status == status.Value);

            if (string.IsNullOrEmpty(field))
                query = query.OrderByDescending(c => c.Id);
            else
                query = query.OrderByPropertyName(field, order == "ascend");

            var data = query.Count(out long total).Page(page, pageSize).ToList();
            return Result.Page(data, total);
        }

        public Result Detail(int id)
        {
            var task = _fsql.Select<AiTask>().Where(c => c.Id == id).First();
            return task == null ? Result.Fail("AI 任务不存在!") : Result.Success(task);
        }

        public Result Create(AiTask task)
        {
            task.Status = AiTaskStatus.Pending;
            task.CreatedTime = DateTime.Now;
            _fsql.Insert(task).ExecuteAffrows();
            _logger.LogInformation("AI 任务已创建: {TaskType} (Id={Id})", task.TaskType, task.Id);
            return Result.Success("AI 任务创建成功!", task);
        }

        public Result Cancel(int id)
        {
            var task = _fsql.Select<AiTask>().Where(c => c.Id == id).First();
            if (task == null) return Result.Fail("任务不存在!");
            if (task.Status == AiTaskStatus.Completed || task.Status == AiTaskStatus.Failed)
                return Result.Fail("任务已结束，无法取消!");

            _fsql.Update<AiTask>().Where(c => c.Id == id)
                .Set(c => c.Status, AiTaskStatus.Failed)
                .Set(c => c.ErrorMessage, "用户取消")
                .Set(c => c.FinishedAt, DateTime.Now)
                .ExecuteAffrows();

            return Result.Success("任务已取消");
        }
    }
}

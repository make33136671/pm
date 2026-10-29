using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectMilestoneService : BaseCrudService<ProjectMilestone>
    {
        public ProjectMilestoneService(IFreeSql fsql, ILogger<ProjectMilestoneService> logger) : base(fsql, logger) { }

        public Result GetList(int projectId, string field = "", string order = "", int page = 1, int pageSize = 50)
        {
            var query = Select.Where(c => c.ProjectId == projectId);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(ProjectMilestone milestone)
        {
            milestone.CreatedTime = DateTime.Now;
            milestone = this.Insert(milestone);
            return Result.Success("里程碑创建成功!", milestone);
        }

        public Result Edit(ProjectMilestone milestone)
        {
            int count = UpdateDiy.UpdateColumns(c => new { c.Title, c.Description, c.DueDate, c.CompletedDate, c.Status, c.Sort })
                .SetSource(milestone).ExecuteAffrows();
            return count > 0 ? Result.Success("里程碑更新成功!") : Result.Fail("里程碑更新失败!");
        }
    }
}

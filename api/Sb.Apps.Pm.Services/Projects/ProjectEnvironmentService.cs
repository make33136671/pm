using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectEnvironmentService : BaseCrudService<ProjectEnvironment>
    {
        public ProjectEnvironmentService(IFreeSql fsql, ILogger<ProjectEnvironmentService> logger) : base(fsql, logger) { }

        public Result GetList(int projectId, string field = "", string order = "", int page = 1, int pageSize = 50)
        {
            var query = Select.Where(c => c.ProjectId == projectId);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(ProjectEnvironment env)
        {
            if (Select.Where(c => c.ProjectId == env.ProjectId && c.Code == env.Code).Any())
                return Result.Fail("该环境编码已存在!");

            env.CreatedTime = DateTime.Now;
            env = this.Insert(env);
            return Result.Success("环境创建成功!", env);
        }

        public Result Edit(ProjectEnvironment env)
        {
            int count = UpdateDiy.UpdateColumns(c => new { c.Name, c.Code, c.Description, c.Sort })
                .SetSource(env).ExecuteAffrows();
            return count > 0 ? Result.Success("环境更新成功!") : Result.Fail("环境更新失败!");
        }
    }
}

using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectActivityService : BaseCrudService<ProjectActivity>
    {
        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ProjectActivityService(IFreeSql fsql, ILogger<ProjectActivityService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        public Result GetList(int projectId, int page = 1, int pageSize = 20)
        {
            var query = Select.Where(c => c.ProjectId == projectId).OrderByDescending(c => c.Id);
            return PagedResult(query, page, pageSize);
        }

        public void Log(int projectId, string targetType, int targetId, string action, string summary, string? detail = null)
        {
            var username = _httpContextAccessor.HttpContext?.User?.Username();
            _fsql.Insert(new ProjectActivity
            {
                ProjectId = projectId,
                TargetType = targetType,
                TargetId = targetId,
                Action = action,
                Summary = summary,
                Detail = detail,
                OperatorName = username
            }).ExecuteAffrows();
        }
    }
}

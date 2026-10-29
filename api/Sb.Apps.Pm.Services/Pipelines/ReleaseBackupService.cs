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
    public class ReleaseBackupService : BaseCrudService<ReleaseBackup>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ReleaseBackupService(IFreeSql fsql, ILogger<ReleaseBackupService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Result GetList(int? projectId = null, int? environmentId = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(environmentId.HasValue, c => c.EnvironmentId == environmentId.Value);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(ReleaseBackup backup)
        {
            backup.CreatedTime = DateTime.Now;
            backup = this.Insert(backup);
            return Result.Success("备份记录创建成功!", backup);
        }
    }
}

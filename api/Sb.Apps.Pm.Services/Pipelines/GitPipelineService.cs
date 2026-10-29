using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Pipelines
{
    [AutoDependencyInjection]
    public class GitPipelineService : BaseCrudService<GitPipeline>
    {
        private readonly IFreeSql _fsql;

        public GitPipelineService(IFreeSql fsql, ILogger<GitPipelineService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public Result GetList(int? repositoryId = null, PipelineStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select.Include(c => c.Repository);
            query = query.WhereIf(repositoryId.HasValue, c => c.RepositoryId == repositoryId.Value);
            query = query.WhereIf(status.HasValue, c => c.Status == status.Value);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result GetDetail(int id)
        {
            var entity = Select.Include(c => c.Repository).Where(c => c.Id == id).First();
            if (entity == null) return Result.Fail("流水线记录不存在!");
            return Result.Success(entity);
        }
    }
}

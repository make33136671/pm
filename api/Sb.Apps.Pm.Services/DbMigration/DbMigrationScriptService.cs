using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.DbMigration
{
    [AutoDependencyInjection]
    public class DbMigrationScriptService : BaseCrudService<DbMigrationScript>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "DB 迁移";
        public int InitOrder => 170;
        public Type[] GetEntityTypes() => new[] { typeof(DbMigrationScript) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition { Name = "DbMigration", Path = "/db-migration", Component = "/db-migration/index", Title = "DB 迁移脚本", Icon = "lucide:database", OrderNo = 9 },
        };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DbMigrationScriptService(IFreeSql fsql, ILogger<DbMigrationScriptService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        public Result GetList(int? projectId = null, int? environmentId = null, MigrationStatus? status = null,
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

        public Result Create(DbMigrationScript script)
        {
            script.CreatedTime = DateTime.Now;
            script.Status = MigrationStatus.Pending;
            script = this.Insert(script);
            return Result.Success("迁移脚本创建成功!", script);
        }

        public Result Edit(DbMigrationScript script)
        {
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Version, c.Title, c.SqlContent, c.RollbackSql, c.Remark, c.EnvironmentId
            }).SetSource(script).ExecuteAffrows();

            return count > 0 ? Result.Success("迁移脚本更新成功!") : Result.Fail("迁移脚本更新失败!");
        }

        public Result Execute(int id)
        {
            var script = Select.Where(c => c.Id == id).First();
            if (script == null) return Result.Fail("迁移脚本不存在!");

            script.Status = MigrationStatus.Executed;
            script.ExecutedAt = DateTime.Now;
            script.ExecutedBy = _httpContextAccessor.HttpContext?.User?.Username();

            _fsql.Update<DbMigrationScript>().SetSource(script).ExecuteAffrows();
            return Result.Success("迁移脚本已标记为已执行!");
        }
    }
}

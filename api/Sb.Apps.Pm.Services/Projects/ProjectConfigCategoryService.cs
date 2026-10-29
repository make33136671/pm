using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectConfigCategoryService : BaseCrudService<ProjectConfigCategory>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "配置分类";
        public int InitOrder => 102;
        public Type[] GetEntityTypes() => new[] { typeof(ProjectConfigCategory) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition { ParentPath = "/system", Name = "ConfigCategory", Path = "/system/config-category", Component = "/system/config-category/index", Title = "配置分类", Icon = "lucide:tags", OrderNo = 2 },
        };
        public void SeedData(IFreeSql fsql)
        {
            if (fsql.Select<ProjectConfigCategory>().Any()) return;
            var categories = new[]
            {
                new ProjectConfigCategory { Name = "数据库连接", Code = "database", Icon = "lucide:database", Sort = 1, Description = "MySQL/PostgreSQL 等数据库连接信息" },
                new ProjectConfigCategory { Name = "服务器/部署", Code = "server", Icon = "lucide:server", Sort = 2, Description = "IP、SSH、部署路径、域名" },
                new ProjectConfigCategory { Name = "API/第三方服务", Code = "api_service", Icon = "lucide:plug", Sort = 3, Description = "API 地址、密钥" },
                new ProjectConfigCategory { Name = "Git 仓库", Code = "git", Icon = "lucide:git-branch", Sort = 4, Description = "仓库地址、分支" },
                new ProjectConfigCategory { Name = "宝塔面板", Code = "bt_panel", Icon = "lucide:layout-dashboard", Sort = 5, Description = "宝塔地址、Token" },
                new ProjectConfigCategory { Name = "FTP", Code = "ftp", Icon = "lucide:upload", Sort = 6, Description = "FTP 地址、账号" },
            };
            fsql.Insert(categories).ExecuteAffrows();
        }
        #endregion

        public ProjectConfigCategoryService(IFreeSql fsql, ILogger<ProjectConfigCategoryService> logger) : base(fsql, logger) { }

        public Result GetList(string field = "", string order = "", int page = 1, int pageSize = 50)
        {
            var query = Select;
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(ProjectConfigCategory category)
        {
            if (Select.Where(c => c.Code == category.Code).Any())
                return Result.Fail("分类编码已存在!");
            category = this.Insert(category);
            return Result.Success("分类创建成功!", category);
        }

        public Result Edit(ProjectConfigCategory category)
        {
            int count = UpdateDiy.UpdateColumns(c => new { c.Name, c.Code, c.Icon, c.Sort, c.Description })
                .SetSource(category).ExecuteAffrows();
            return count > 0 ? Result.Success("分类更新成功!") : Result.Fail("分类更新失败!");
        }
    }
}

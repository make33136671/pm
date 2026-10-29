using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectTemplateService : BaseCrudService<ProjectTemplate>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "项目模板";
        public int InitOrder => 101;
        public Type[] GetEntityTypes() => new[] { typeof(ProjectTemplate), typeof(ProjectTemplateConfig) };
        public void SeedData(IFreeSql fsql)
        {
            // 默认项目模板
            if (fsql.Select<ProjectTemplate>().Any()) return;
            var templates = new[]
            {
                new ProjectTemplate { Id = 1, Name = ".NET + Vue 全栈", Description = ".NET 8 后端 + Vue 3 前端", TechStack = ".NET 8, ASP.NET Core, FreeSql, Vue 3, Vben Admin", DefaultEnvs = "dev,test,prod", Sort = 1 },
                new ProjectTemplate { Id = 2, Name = "Vue 前端", Description = "纯前端项目", TechStack = "Vue 3, TypeScript, Vite, pnpm", DefaultEnvs = "dev,test,prod", Sort = 2 },
                new ProjectTemplate { Id = 3, Name = ".NET 后端", Description = "纯后端 API 项目", TechStack = ".NET 8, ASP.NET Core, FreeSql, MySQL", DefaultEnvs = "dev,test,prod", Sort = 3 },
            };
            fsql.Insert(templates).ExecuteAffrows();

            // .NET + Vue 模板配置项
            var configs = new[]
            {
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 1, ConfigKey = "db_host", ConfigName = "数据库主机", DefaultValue = "127.0.0.1", Sort = 1 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 1, ConfigKey = "db_port", ConfigName = "端口", DefaultValue = "3306", Sort = 2 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 1, ConfigKey = "db_user", ConfigName = "用户名", DefaultValue = "root", Sort = 3 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 1, ConfigKey = "db_password", ConfigName = "密码", IsSensitive = true, DisplayType = 2, Sort = 4 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 1, ConfigKey = "db_name", ConfigName = "数据库名", Sort = 5 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 1, ConfigKey = "db_connection_string", ConfigName = "连接字符串", DisplayType = 1, Sort = 6 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 2, ConfigKey = "server_ip", ConfigName = "服务器IP", Sort = 1 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 2, ConfigKey = "server_ssh_port", ConfigName = "SSH端口", DefaultValue = "22", Sort = 2 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 2, ConfigKey = "server_ssh_password", ConfigName = "SSH密码", IsSensitive = true, DisplayType = 2, Sort = 3 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 2, ConfigKey = "server_deploy_path", ConfigName = "部署路径", Sort = 4 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 2, ConfigKey = "server_domain", ConfigName = "域名", Sort = 5 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 2, ConfigKey = "server_nginx_config", ConfigName = "Nginx配置", DisplayType = 1, Sort = 6 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 4, ConfigKey = "git_repo_url", ConfigName = "仓库地址", Sort = 1 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 4, ConfigKey = "git_deploy_branch", ConfigName = "部署分支", DefaultValue = "main", Sort = 2 },
                new ProjectTemplateConfig { TemplateId = 1, CategoryId = 4, ConfigKey = "git_ci_cd_config", ConfigName = "CI/CD配置说明", DisplayType = 1, Sort = 3 },
            };
            fsql.Insert(configs).ExecuteAffrows();
        }
        #endregion

        private readonly IFreeSql _fsql;

        public ProjectTemplateService(IFreeSql fsql, ILogger<ProjectTemplateService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public Result GetList(string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select;
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public override Result Detail(int id)
        {
            var template = Select.Where(c => c.Id == id).First();
            if (template == null) return Result.Fail("模板不存在!");

            var configs = _fsql.Select<ProjectTemplateConfig>()
                .Include(c => c.Category)
                .Where(c => c.TemplateId == id)
                .OrderBy(c => c.Sort).ToList();

            return Result.Success(new { template, configs });
        }

        public Result Create(ProjectTemplate template)
        {
            template.CreatedTime = DateTime.Now;
            template = this.Insert(template);
            return Result.Success("模板创建成功!", template);
        }

        public Result Edit(ProjectTemplate template)
        {
            int count = UpdateDiy.UpdateColumns(c => new { c.Name, c.Description, c.TechStack, c.DefaultEnvs, c.Sort })
                .SetSource(template).ExecuteAffrows();
            return count > 0 ? Result.Success("模板更新成功!") : Result.Fail("模板更新失败!");
        }
    }
}

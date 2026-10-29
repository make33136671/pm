using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectCustomerService : BaseCrudService<ProjectCustomer>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "客户管理";
        public int InitOrder => 105;
        public Type[] GetEntityTypes() => new[] { typeof(ProjectCustomer) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                ParentPath = "/project",
                Name = "ProjectCustomer", Path = "/project/customer",
                Component = "/project-customer/index", Title = "客户管理",
                Icon = "lucide:users", OrderNo = 3,
            },
        };
        #endregion

        private readonly IHttpContextAccessor _httpContextAccessor;

        public ProjectCustomerService(IFreeSql fsql, ILogger<ProjectCustomerService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Result GetList(int? projectId = null, CustomerStatus? status = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select.Include(c => c.Project)
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(status.HasValue, c => c.Status == status.Value)
                .WhereIf(!string.IsNullOrEmpty(keyword), c => c.Name.Contains(keyword) || c.ProductName.Contains(keyword));
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(ProjectCustomer customer)
        {
            customer.CreatedTime = DateTime.Now;
            customer = this.Insert(customer);
            return Result.Success("客户创建成功!", customer);
        }

        public Result Edit(ProjectCustomer customer)
        {
            customer.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Name, c.ProductName, c.Contact, c.BranchName,
                c.ProdServer, c.TestServer, c.ExpireDate, c.Status,
                c.Remark, c.UpdatedTime
            }).SetSource(customer).ExecuteAffrows();

            return count > 0 ? Result.Success("客户更新成功!") : Result.Fail("客户更新失败!");
        }
    }
}

using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Documents
{
    [AutoDependencyInjection]
    public class NotebookService : BaseCrudService<Notebook>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "记事本";
        public int InitOrder => 200;
        public Type[] GetEntityTypes() => new[] { typeof(Notebook) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition { Name = "Notebook", Path = "/notebook", Component = "/notebook/index", Title = "记事本", Icon = "lucide:book-open", OrderNo = 12 },
        };
        #endregion

        private readonly IHttpContextAccessor _httpContextAccessor;

        public NotebookService(IFreeSql fsql, ILogger<NotebookService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private int GetCurrentAdminId() => _httpContextAccessor.HttpContext?.User?.Id() ?? 0;

        public Result GetList(string? keyword = null, string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            int adminId = GetCurrentAdminId();
            var query = Select.Where(c => c.AdminId == adminId);
            query = query.WhereIf(!string.IsNullOrEmpty(keyword), c => c.Title.Contains(keyword) || c.Content.Contains(keyword));

            if (string.IsNullOrEmpty(field))
                query = query.OrderByDescending(c => c.IsPinned).OrderByDescending(c => c.Id);
            else
                query = ApplySorting(query, field, order);

            return PagedResult(query, page, pageSize);
        }

        public Result Create(Notebook notebook)
        {
            notebook.AdminId = GetCurrentAdminId();
            notebook.CreatedTime = DateTime.Now;
            notebook = this.Insert(notebook);
            return Result.Success("记事创建成功!", notebook);
        }

        public Result Edit(Notebook notebook)
        {
            notebook.AdminId = GetCurrentAdminId();
            notebook.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new { c.Title, c.Content, c.Color, c.Tags, c.UpdatedTime })
                .SetSource(notebook).ExecuteAffrows();
            return count > 0 ? Result.Success("记事更新成功!") : Result.Fail("记事更新失败!");
        }

        public Result Pin(int id)
        {
            var notebook = Select.Where(c => c.Id == id && c.AdminId == GetCurrentAdminId()).First();
            if (notebook == null) return Result.Fail("记事不存在!");
            notebook.IsPinned = !notebook.IsPinned;
            UpdateDiy.UpdateColumns(c => c.IsPinned).SetSource(notebook).ExecuteAffrows();
            return Result.Success(notebook.IsPinned ? "已置顶" : "已取消置顶");
        }
    }
}

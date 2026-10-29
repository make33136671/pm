using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Ai
{
    [AutoDependencyInjection]
    public class AiMemoryService : BaseCrudService<AiMemory>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "AI 中心";
        public int InitOrder => 190;
        public Type[] GetEntityTypes() => new[] { typeof(AiMemory) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                Name = "AiCenter", Path = "/ai", Component = "BasicLayout",
                Redirect = "/ai/chat", Title = "AI 中心",
                Icon = "lucide:sparkles", OrderNo = 11,
                Children = new()
                {
                    new() { Name = "AiChat", Path = "/ai/chat", Component = "/ai/chat/index", Title = "AI 对话", Icon = "lucide:message-square-code", OrderNo = 1 },
                    new() { Name = "AiMemory", Path = "/ai/memory", Component = "/ai/memory/index", Title = "AI 记忆", Icon = "lucide:brain-circuit", OrderNo = 2 },
                    new() { Name = "AiTask", Path = "/ai/task", Component = "/ai/task/index", Title = "AI 任务", Icon = "lucide:cpu", OrderNo = 3 },
                }
            }
        };

        public List<SystemSettingDefinition> GetSystemSettings() => new()
        {
            new SystemSettingDefinition { Key = "workspace_root", Name = "项目操作根目录", CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1, DisplayType = SystemSettingDisplayType.Input, DefaultValue = "/data/pm-workspace", DataContentConfig = "{\"placeholder\":\"必须是绝对路径,如 /data/pm-workspace。Git 仓库会克隆到 此根目录/项目编码/子项目编码/\"}", CantDelete = true, Sort = 7 },
        };

        public void SeedData(IFreeSql fsql)
        {
            // 自愈历史脏数据: 清空所有相对路径的 SubProject.LocalPath
            // 旧版 GetLocalPath 在 workspace_root 为空或相对值时会把相对路径回写到 LocalPath,
            // 后续因为 LocalPath 字段优先于 workspace_root,会一直走错分支落到进程 CWD 下。
            // 现在 GetLocalPath 已经始终从 workspace_root 重算,这条 SQL 把历史脏值清掉,
            // 下次同步会按新的绝对路径重新克隆。
            // Linq 表达式树翻译不了 Path.IsPathRooted / Regex.IsMatch(FreeSql PG provider 不支持正则),
            // 改用 StartsWith("/") 判 Linux 绝对路径 + Substring 判 Windows 盘符冒号,MySQL/PG 均可翻译。
            var rows = fsql.Update<SubProject>()
                .Set(s => s.LocalPath, (string?)null)
                .Where(s => s.LocalPath != null && s.LocalPath != ""
                    && !s.LocalPath.StartsWith("/")
                    && !(s.LocalPath.Length >= 2 && s.LocalPath.Substring(1, 1) == ":"))
                .ExecuteAffrows();
            if (rows > 0)
                Serilog.Log.Information("AiMemoryService.SeedData: 清理了 {Rows} 条 SubProject.LocalPath 历史相对路径脏数据", rows);
        }
        #endregion

        private readonly IFreeSql _fsql;

        public AiMemoryService(IFreeSql fsql, ILogger<AiMemoryService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public Result GetList(int? projectId = null, AiMemoryScope? scope = null, string? category = null,
            string? keyword = null, string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(scope.HasValue, c => c.Scope == scope.Value)
                .WhereIf(!string.IsNullOrEmpty(category), c => c.Category == category)
                .WhereIf(!string.IsNullOrEmpty(keyword), c => c.Title.Contains(keyword) || c.Content.Contains(keyword) || c.Tags.Contains(keyword));

            if (string.IsNullOrEmpty(field))
                query = query.OrderByDescending(c => c.Importance).OrderByDescending(c => c.Id);
            else
                query = ApplySorting(query, field, order);

            return PagedResult(query, page, pageSize);
        }

        public Result Create(AiMemory memory)
        {
            memory.CreatedTime = DateTime.Now;
            memory = this.Insert(memory);
            return Result.Success("AI 记忆创建成功!", memory);
        }

        public Result Edit(AiMemory memory)
        {
            memory.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Title, c.Content, c.Scope, c.Category, c.Importance, c.Tags, c.ExpiresAt, c.UpdatedTime
            }).SetSource(memory).ExecuteAffrows();
            return count > 0 ? Result.Success("AI 记忆更新成功!") : Result.Fail("更新失败!");
        }

        public Result Search(string keyword, AiMemoryScope? scope = null, int? projectId = null, int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return Result.Fail("请输入搜索关键词!");

            var query = Select
                .Where(c => c.Title.Contains(keyword) || c.Content.Contains(keyword) || c.Tags.Contains(keyword))
                .WhereIf(scope.HasValue, c => c.Scope == scope.Value)
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .OrderByDescending(c => c.Importance)
                .Limit(limit);

            var data = query.ToList();

            // 更新访问时间和次数
            foreach (var item in data)
            {
                _fsql.Update<AiMemory>().Where(c => c.Id == item.Id)
                    .Set(c => c.LastAccessedAt, DateTime.Now)
                    .Set(c => c.AccessCount + 1)
                    .ExecuteAffrows();
            }

            return Result.Success(data);
        }
    }
}

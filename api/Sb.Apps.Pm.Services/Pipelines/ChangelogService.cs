using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Tasks;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.Services.Pipelines
{
    [AutoDependencyInjection]
    public class ChangelogService : BaseCrudService<Changelog>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "变更日志";
        public int InitOrder => 140;
        public Type[] GetEntityTypes() => new[] { typeof(Changelog) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition { Name = "Changelog", Path = "/changelog", Component = "/changelog/index", Title = "变更日志", Icon = "lucide:file-text", OrderNo = 6 },
        };
        #endregion

        private readonly IHttpContextAccessor _httpContextAccessor;

        public ChangelogService(IFreeSql fsql, ILogger<ChangelogService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Result GetList(int? projectId = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(!string.IsNullOrEmpty(keyword), c => c.Version.Contains(keyword) || c.Content.Contains(keyword))
                .Include(c => c.Project);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(Changelog changelog)
        {
            changelog.CreatedTime = DateTime.Now;
            changelog = this.Insert(changelog);
            return Result.Success("变更日志创建成功!", changelog);
        }

        public Result Edit(Changelog changelog)
        {
            changelog.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Version, c.Content, c.PublishedAt, c.UpdatedTime
            }).SetSource(changelog).ExecuteAffrows();

            return count > 0 ? Result.Success("变更日志更新成功!") : Result.Fail("变更日志更新失败!");
        }

        /// <summary>
        /// 读取指定项目已完成的任务列表，调用 AI 生成 changelog 内容并写入。
        /// 若无 AI Provider 则返回模板供手动编辑。
        /// </summary>
        public async Task<Result> GenerateByAi(int projectId, string version, IAiProvider? aiProvider,
            DateTime? since = null, int? releaseRecordId = null)
        {
            // 1) 读取已完成任务
            var query = Fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId && t.ExecStatus == TaskItemExecStatus.Success && !t.IsDeleted);
            if (since.HasValue) query = query.Where(t => t.CompletedDate >= since.Value);
            var tasks = query.OrderByDescending(t => t.CompletedDate).Take(100)
                .ToList(t => new { t.Title, t.Content, t.Type });

            if (tasks.Count == 0)
                return Result.Fail("没有已完成的任务，无法生成变更日志");

            string content;
            if (aiProvider == null)
            {
                // 无 AI：返回结构化模板
                var lines = tasks.Select(t => $"- {t.Title}");
                content = $"## {version}\n\n### 变更内容\n\n{string.Join("\n", lines)}\n";
            }
            else
            {
                var taskList = string.Join("\n", tasks.Select((t, i) => $"{i + 1}. [{t.Type}] {t.Title}{(string.IsNullOrWhiteSpace(t.Content) ? "" : $"：{t.Content?.Replace("\n", " ")}")}"));
                var prompt = $@"你是一名技术写作工程师，请根据以下已完成的开发任务列表，生成一份专业的变更日志（Changelog）。

版本号：{version}
已完成任务：
{taskList}

要求：
1. 按类型分组（新增功能/Bug修复/优化改进/其他）
2. 每条用 Markdown 列表格式（- 开头）
3. 语言简洁专业，面向开发者
4. 只返回 Markdown 正文，不要包含其他内容";

                var resp = await aiProvider.SendAsync(new AiRequest
                {
                    SystemPrompt = "你是技术写作工程师，只返回 Markdown 内容。",
                    Messages = new List<AiMessage> { new() { Role = "user", Content = prompt } },
                    MaxTokens = 2048,
                    Temperature = 0.3f,
                });
                if (!resp.Success) return Result.Fail($"AI 生成失败: {resp.Error ?? "未知错误"}");
                content = resp.Content.Trim();
            }

            // 2) 写入
            var changelog = new Changelog
            {
                ProjectId = projectId,
                Version = version,
                Content = content,
                GeneratedBy = aiProvider != null ? GeneratedBy.Auto : GeneratedBy.Manual,
                ReleaseRecordId = releaseRecordId,
                CreatedTime = DateTime.Now,
            };
            changelog = this.Insert(changelog);
            return Result.Success("变更日志生成成功", changelog);
        }

        private IFreeSql Fsql => (IFreeSql)typeof(BaseCrudService<Changelog>)
            .GetField("_fsql", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(this)!;
    }
}

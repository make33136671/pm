using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class SubProjectService : BaseCrudService<SubProject>
    {
        private readonly IFreeSql _fsql;

        public SubProjectService(IFreeSql fsql, ILogger<SubProjectService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public Result GetList(int? projectId = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 100)
        {
            var query = Select.Include(c => c.Project);
            query = query.WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value);
            query = query.WhereIf(!string.IsNullOrEmpty(keyword), c => c.Name.Contains(keyword) || c.Code.Contains(keyword));
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(SubProject subProject)
        {
            subProject.CreatedTime = DateTime.Now;
            subProject = this.Insert(subProject);
            return Result.Success("子项目创建成功!", subProject);
        }

        public Result Edit(SubProject subProject)
        {
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Name, c.Code, c.Description,
                c.BaseRepoUrl, c.GitlabProjectId, c.GitlabProjectPath, c.GitlabProjectUrl,
                c.DefaultBranch, c.LocalPath, c.Status, c.Progress,
                c.DeployEnv, c.LastDeployTime, c.DeployStatus, c.Sort
            }).SetSource(subProject).ExecuteAffrows();

            return count > 0 ? Result.Success("子项目更新成功!") : Result.Fail("子项目更新失败!");
        }

        /// <summary>
        /// 批量保存子项目（新增 + 更新）
        /// </summary>
        public Result SaveList(int projectId, List<SubProject> items)
        {
            // 获取现有子项目
            var existing = Select.Where(c => c.ProjectId == projectId).ToList();
            var existingIds = existing.Select(e => e.Id).ToHashSet();
            var incomingIds = items.Where(i => i.Id > 0).Select(i => i.Id).ToHashSet();

            // 删除不在提交列表中的
            var toDelete = existing.Where(e => !incomingIds.Contains(e.Id)).ToList();
            foreach (var del in toDelete)
            {
                _fsql.Update<SubProject>().Where(c => c.Id == del.Id)
                    .Set(c => c.IsDeleted, true).ExecuteAffrows();
            }

            // 新增或更新
            foreach (var item in items)
            {
                item.ProjectId = projectId;
                if (item.Id > 0 && existingIds.Contains(item.Id))
                {
                    Edit(item);
                }
                else
                {
                    item.Id = 0;
                    Create(item);
                }
            }

            return Result.Success("子项目保存成功!");
        }
    }
}

using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Util;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectServerService : BaseCrudService<ProjectServer>
    {
        private readonly IFreeSql _fsql;

        public ProjectServerService(IFreeSql fsql, ILogger<ProjectServerService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        /// <summary>
        /// 手动加载 ProjectServer 的多对一导航并拼接。
        /// 规避 FreeSql 3.5.x 对本实体内联 .Include() 的物化 bug:子表列被内联进父表列中间,
        /// 叠加同名列(Id/ProjectId/SubProjectId/CreatedTime/IsDeleted)+ MapType 映射列(PreferredDeployMethod),
        /// 导致父实体读取列错位 —— 可能静默把 ServerId/BuiltInApiKey 等关键字段读错(部署到错服务器/用错密钥)。
        /// 详见 PipelineProfileService.LoadNavs。
        /// </summary>
        private void LoadNavs(IReadOnlyList<ProjectServer> rows,
            bool project = false, bool subProject = false, bool server = false, bool environment = false)
        {
            if (rows.Count == 0) return;
            if (project)
            {
                var ids = rows.Select(p => p.ProjectId).Distinct().ToList();
                var map = _fsql.Select<Project>().Where(x => ids.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
                foreach (var r in rows) r.Project = map.TryGetValue(r.ProjectId, out var v) ? v : null;
            }
            if (subProject)
            {
                var ids = rows.Where(p => p.SubProjectId != null).Select(p => p.SubProjectId!.Value).Distinct().ToList();
                var map = ids.Count == 0 ? new Dictionary<int, SubProject>()
                    : _fsql.Select<SubProject>().Where(x => ids.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
                foreach (var r in rows) r.SubProject = r.SubProjectId != null && map.TryGetValue(r.SubProjectId.Value, out var v) ? v : null;
            }
            if (server)
            {
                var ids = rows.Select(p => p.ServerId).Distinct().ToList();
                var map = _fsql.Select<Server>().Where(x => ids.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
                foreach (var r in rows) r.Server = map.TryGetValue(r.ServerId, out var v) ? v : null;
            }
            if (environment)
            {
                var ids = rows.Where(p => p.EnvironmentId != null).Select(p => p.EnvironmentId!.Value).Distinct().ToList();
                var map = ids.Count == 0 ? new Dictionary<int, ProjectEnvironment>()
                    : _fsql.Select<ProjectEnvironment>().Where(x => ids.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
                foreach (var r in rows) r.Environment = r.EnvironmentId != null && map.TryGetValue(r.EnvironmentId.Value, out var v) ? v : null;
            }
        }

        private ProjectServer? LoadNavs(ProjectServer? row,
            bool project = false, bool subProject = false, bool server = false, bool environment = false)
        {
            if (row != null) LoadNavs(new[] { row }, project, subProject, server, environment);
            return row;
        }

        public Result GetList(int? projectId = null, int? subProjectId = null, int? serverId = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .WhereIf(subProjectId.HasValue, c => c.SubProjectId == subProjectId.Value)
                .WhereIf(serverId.HasValue, c => c.ServerId == serverId.Value);
            query = ApplySorting(query, field, order);
            var data = query.Count(out long total).Page(page, pageSize).ToList();
            LoadNavs(data, project: true, subProject: true, server: true, environment: true);
            foreach (var ps in data)
                if (!string.IsNullOrEmpty(ps.BuiltInApiKey)) ps.BuiltInApiKey = "******";
            return Result.Page(data, total);
        }

        public override Result Detail(int id)
        {
            var entity = LoadNavs(Select.Where(c => c.Id == id).First(),
                project: true, subProject: true, server: true, environment: true);
            if (entity == null) return Result.Fail("部署配置不存在!");
            if (!string.IsNullOrEmpty(entity.BuiltInApiKey)) entity.BuiltInApiKey = "******";
            return Result.Success(entity);
        }

        public Result Create(ProjectServer entity)
        {
            // 加密 BuiltInApiKey
            if (!string.IsNullOrEmpty(entity.BuiltInApiKey))
                entity.BuiltInApiKey = EncryptionUtil.AesEncrypt(entity.BuiltInApiKey);

            entity.CreatedTime = DateTime.Now;
            entity = this.Insert(entity);
            return Result.Success("部署配置创建成功!", entity);
        }

        public Result Edit(ProjectServer entity)
        {
            var existing = Select.Where(c => c.Id == entity.Id).First();
            if (existing == null) return Result.Fail("部署配置不存在!");

            // BuiltInApiKey 敏感字段处理
            if (string.IsNullOrEmpty(entity.BuiltInApiKey) || entity.BuiltInApiKey == "******")
                entity.BuiltInApiKey = existing.BuiltInApiKey;
            else
                entity.BuiltInApiKey = EncryptionUtil.AesEncrypt(entity.BuiltInApiKey);

            entity.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.ProjectId, c.SubProjectId, c.ServerId, c.EnvironmentId,
                c.Label, c.IsDefault,
                c.PreferredDeployMethod, c.AvailableDeployMethods,
                c.BuiltInApiUrl, c.BuiltInApiKey,
                c.ApiDeployPath, c.FrontendDeployPath, c.FtpDeployPath,
                c.DatabaseName,
                c.ApiPort, c.FrontendPort, c.ExtraPorts,
                c.SystemdServiceName, c.BtSiteName,
                c.Remark, c.Sort, c.UpdatedTime
            }).SetSource(entity).ExecuteAffrows();

            return count > 0 ? Result.Success("部署配置更新成功!") : Result.Fail("更新失败!");
        }

        /// <summary>
        /// 获取项目的默认部署配置
        /// </summary>
        public ProjectServer? GetDefaultServer(int projectId, int? subProjectId = null)
        {
            var entity = Select
                .Where(c => c.ProjectId == projectId)
                .WhereIf(subProjectId.HasValue, c => c.SubProjectId == subProjectId.Value)
                .Where(c => c.IsDefault)
                .First()
                ?? Select
                    .Where(c => c.ProjectId == projectId)
                    .WhereIf(subProjectId.HasValue, c => c.SubProjectId == subProjectId.Value)
                    .First();
            return LoadNavs(entity, server: true);
        }
    }
}

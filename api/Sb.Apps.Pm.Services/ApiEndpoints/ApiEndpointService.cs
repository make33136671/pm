using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Models;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.ApiEndpoints;

namespace Sb.Apps.Pm.Services.ApiEndpoints
{
    [AutoDependencyInjection]
    public class ApiEndpointService : BaseCrudService<ApiEndpoint>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "接口管理";
        public int InitOrder => 135;
        public Type[] GetEntityTypes() => new[]
        {
            typeof(ApiEndpoint), typeof(ApiEndpointFeatureRel),
        };
        public List<SystemSettingDefinition> GetSystemSettings() => new();
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                ParentPath = "/project", Name = "ApiEndpointList",
                Path = "/project/api-endpoint/list", Component = "/api-endpoint/list/index",
                Title = "接口管理", Icon = "lucide:webhook", OrderNo = 10,
            },
        };
        public void SeedData(IFreeSql fsql) { }
        #endregion

        private readonly IFreeSql _fsql;

        public ApiEndpointService(IFreeSql fsql, ILogger<ApiEndpointService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        #region CRUD

        public Result GetList(int? projectId = null, ApiSide? side = null, ApiEndpointStatus? status = null,
            int? featureModuleId = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select.Include(c => c.Project)
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId!.Value)
                .WhereIf(side.HasValue, c => c.Side == side!.Value)
                .WhereIf(status.HasValue, c => c.Status == status!.Value)
                .WhereIf(!string.IsNullOrEmpty(keyword), c => c.Name.Contains(keyword!) || c.Path.Contains(keyword!));

            // 按功能模块过滤（多对多）
            if (featureModuleId.HasValue)
            {
                var ids = _fsql.Select<ApiEndpointFeatureRel>()
                    .Where(r => r.FeatureModuleId == featureModuleId.Value)
                    .ToList(r => r.ApiEndpointId);
                query = query.Where(c => ids.Contains(c.Id));
            }

            query = string.IsNullOrEmpty(field)
                ? query.OrderBy(c => c.Sort).OrderByDescending(c => c.Id)
                : ApplySorting(query, field, order);

            var items = query.Count(out long total).Page(page, pageSize).ToList();

            // 附带每条接口的关联功能模块 id 列表
            var endpointIds = items.Select(i => i.Id).ToList();
            var rels = endpointIds.Count > 0
                ? _fsql.Select<ApiEndpointFeatureRel>().Where(r => endpointIds.Contains(r.ApiEndpointId)).ToList()
                : new List<ApiEndpointFeatureRel>();
            var relMap = rels.GroupBy(r => r.ApiEndpointId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.FeatureModuleId).ToList());

            var data = items.Select(i => new
            {
                i.Id, i.ProjectId, i.Name, i.Path, i.Method, i.Side, i.Status,
                i.Description, i.Sort, i.CreatedTime,
                projectName = i.Project?.Name,
                featureModuleIds = relMap.GetValueOrDefault(i.Id) ?? new List<int>(),
            }).ToList();

            return Result.Page(data, total);
        }

        public Result Detail(int id)
        {
            var ep = Select.Where(c => c.Id == id).First();
            if (ep == null) return Result.Fail("接口不存在!");
            var moduleIds = _fsql.Select<ApiEndpointFeatureRel>()
                .Where(r => r.ApiEndpointId == id).ToList(r => r.FeatureModuleId);
            return Result.Success(new
            {
                ep.Id, ep.ProjectId, ep.Name, ep.Path, ep.Method, ep.Side, ep.Status,
                ep.Description, ep.Sort, ep.CreatedTime,
                featureModuleIds = moduleIds,
            });
        }

        public Result Create(ApiEndpoint ep)
        {
            ep.CreatedTime = DateTime.Now;
            ep = this.Insert(ep);
            return Result.Success("接口创建成功!", ep);
        }

        public Result Edit(ApiEndpoint ep)
        {
            ep.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Name, c.Path, c.Method, c.Side, c.Status, c.Description, c.Sort, c.UpdatedTime
            }).SetSource(ep).ExecuteAffrows();
            return count > 0 ? Result.Success("接口更新成功!") : Result.Fail("更新失败!");
        }

        public override Result Del(int id)
        {
            var ep = Select.Where(c => c.Id == id).First();
            if (ep == null) return Result.Fail("接口不存在!");
            var result = base.Del(id);
            if (result.Code == 0)
                _fsql.Delete<ApiEndpointFeatureRel>().Where(r => r.ApiEndpointId == id).ExecuteAffrows();
            return result;
        }

        #endregion

        #region 功能模块关联（多对多）

        /// <summary>保存接口的功能模块关联（覆盖式）</summary>
        public Result SaveRelations(int apiEndpointId, List<int> featureModuleIds)
        {
            _fsql.Delete<ApiEndpointFeatureRel>().Where(r => r.ApiEndpointId == apiEndpointId).ExecuteAffrows();
            if (featureModuleIds != null && featureModuleIds.Count > 0)
            {
                var rels = featureModuleIds.Select(fmId => new ApiEndpointFeatureRel
                {
                    ApiEndpointId = apiEndpointId,
                    FeatureModuleId = fmId,
                    CreatedTime = DateTime.Now,
                }).ToList();
                _fsql.Insert(rels).ExecuteAffrows();
            }
            return Result.Success("关联保存成功!");
        }

        /// <summary>获取接口关联的功能模块 id 列表</summary>
        public Result GetRelations(int apiEndpointId)
        {
            var ids = _fsql.Select<ApiEndpointFeatureRel>()
                .Where(r => r.ApiEndpointId == apiEndpointId).ToList(r => r.FeatureModuleId);
            return Result.Success(ids);
        }

        #endregion
    }
}

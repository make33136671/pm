using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.Services.Ai
{
    [AutoDependencyInjection]
    public class AiProviderService : BaseCrudService<AiProvider>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "AI 提供商";
        public int InitOrder => 192;
        public Type[] GetEntityTypes() => new[] { typeof(AiProvider) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition { ParentPath = "/system", Name = "AiProvider", Path = "/system/ai-provider", Component = "/ai-provider/index", Title = "AI 提供商", Icon = "lucide:brain-circuit", OrderNo = 5 },
        };
        public void SeedData(IFreeSql fsql)
        {
            // 按 ProviderType 单条 upsert,允许新增 Provider 时增量补种,不影响已存在记录
            if (!fsql.Select<AiProvider>().Any(c => c.ProviderType == "Stub"))
            {
                fsql.Insert(new AiProvider { Name = "Stub (测试用)", ProviderType = "Stub", ModelName = "stub-v1", IsDefault = true, Status = AiProviderStatus.Active }).ExecuteAffrows();
            }
            if (!fsql.Select<AiProvider>().Any(c => c.ProviderType == "Hermes"))
            {
                fsql.Insert(new AiProvider
                {
                    Name = "Hermes Agent",
                    ProviderType = "Hermes",
                    ModelName = "default",
                    Endpoint = "",   // Gateway 部署后通过 Admin UI 填入,或在 appsettings.Hermes:GatewayUrl 配
                    ApiKey = "",
                    ExtraConfig = """{"systemPrompt":"你是 PM 项目智能助手。","maxTokens":4096,"temperature":0.3}""",
                    Status = AiProviderStatus.Disabled,  // Gateway 未接入,默认禁用避免误调用
                }).ExecuteAffrows();
            }
            // Llama (本地大模型) — 由运营在 Admin UI 中启用和配置
            if (!fsql.Select<AiProvider>().Any(c => c.ProviderType == "Llama"))
            {
                fsql.Insert(new AiProvider
                {
                    Name = "Llama.cpp (本地大模型)",
                    ProviderType = "Llama",
                    ModelName = "",
                    Endpoint = "http://192.168.10.80:8080",
                    ApiKey = "",
                    ExtraConfig = """{"systemPrompt":"你是项目管理系统智能助手。","maxTokens":4096,"temperature":0.3}""",
                    Status = AiProviderStatus.Disabled,
                }).ExecuteAffrows();
            }
        }
        #endregion

        private readonly IFreeSql _fsql;
        private readonly AiProviderFactory _factory;

        public AiProviderService(IFreeSql fsql, ILogger<AiProviderService> logger, AiProviderFactory factory) : base(fsql, logger)
        {
            _fsql = fsql;
            _factory = factory;
        }

        public Result GetList(string? keyword = null, string? providerType = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(!string.IsNullOrEmpty(keyword), c => c.Name.Contains(keyword))
                .WhereIf(!string.IsNullOrEmpty(providerType), c => c.ProviderType == providerType);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(AiProvider entity)
        {
            if (entity.IsDefault)
            {
                _fsql.Update<AiProvider>().Set(c => c.IsDefault, false).Where(c => c.IsDefault == true).ExecuteAffrows();
            }
            entity.CreatedTime = DateTime.Now;
            entity = this.Insert(entity);
            return Result.Success("AI 提供商创建成功!", entity);
        }

        public Result Edit(AiProvider entity)
        {
            if (entity.IsDefault)
            {
                _fsql.Update<AiProvider>().Set(c => c.IsDefault, false).Where(c => c.IsDefault == true && c.Id != entity.Id).ExecuteAffrows();
            }
            entity.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Name, c.ProviderType, c.Endpoint, c.ApiKey, c.ModelName,
                c.ExtraConfig, c.IsDefault, c.Status, c.UpdatedTime
            }).SetSource(entity).ExecuteAffrows();
            return count > 0 ? Result.Success("更新成功!") : Result.Fail("更新失败!");
        }

        /// <summary>获取已注册的适配器类型列表（供前端下拉）</summary>
        public Result GetAdapterTypes()
        {
            return Result.Success(_factory.GetRegisteredAdapters());
        }

        /// <summary>获取可用的 AI 提供商列表（Active 状态 + 适配器已注册）</summary>
        public Result GetAvailable()
        {
            var configs = Select.Where(c => c.Status == AiProviderStatus.Active).OrderByDescending(c => c.IsDefault).ToList();
            var registered = _factory.GetRegisteredAdapters().Select(a => a.ProviderType).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var list = configs.Where(c => registered.Contains(c.ProviderType)).Select(c =>
            {
                var adapter = _factory.GetAdapter(c.ProviderType);
                return new
                {
                    c.Id, c.Name, c.ProviderType, c.ModelName, c.IsDefault,
                    SupportStream = adapter?.SupportStream ?? false,
                };
            }).ToList();

            return Result.Success(list);
        }
    }
}

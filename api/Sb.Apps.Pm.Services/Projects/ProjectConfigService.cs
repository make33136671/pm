using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Util;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Projects
{
    [AutoDependencyInjection]
    public class ProjectConfigService : BaseCrudService<ProjectConfig>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "项目配置";
        public int InitOrder => 103;
        public Type[] GetEntityTypes() => new[] { typeof(ProjectConfig) };
        #endregion

        private readonly IFreeSql _fsql;

        public ProjectConfigService(IFreeSql fsql, ILogger<ProjectConfigService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public Result GetList(int projectId, int? environmentId = null, int? categoryId = null)
        {
            var query = Select.Include(c => c.Category)
                .Where(c => c.ProjectId == projectId);
            query = query.WhereIf(environmentId.HasValue, c => c.EnvironmentId == environmentId.Value);
            query = query.WhereIf(categoryId.HasValue, c => c.CategoryId == categoryId.Value);
            query = query.OrderBy(c => c.CategoryId).OrderBy(c => c.Sort);

            var data = query.ToList();
            // 隐藏敏感值
            foreach (var item in data)
            {
                if (item.IsSensitive && !string.IsNullOrEmpty(item.ConfigValue))
                    item.ConfigValue = "******";
            }
            return Result.Success(data);
        }

        public Result Save(List<ProjectConfig> configs)
        {
            foreach (var config in configs)
            {
                if (config.IsSensitive && !string.IsNullOrEmpty(config.ConfigValue) && config.ConfigValue != "******")
                    config.ConfigValue = EncryptionUtil.AesEncrypt(config.ConfigValue);

                if (config.Id > 0)
                {
                    config.UpdatedTime = DateTime.Now;
                    UpdateDiy.UpdateColumns(c => new
                    {
                        c.ConfigKey, c.ConfigName, c.ConfigValue, c.IsSensitive,
                        c.DisplayType, c.CategoryId, c.EnvironmentId,
                        c.Remark, c.Sort, c.UpdatedTime
                    }).SetSource(config).ExecuteAffrows();
                }
                else
                {
                    config.CreatedTime = DateTime.Now;
                    this.Insert(config);
                }
            }
            return Result.Success("配置保存成功!");
        }

        public Result DecryptValue(int configId)
        {
            var config = Select.Where(c => c.Id == configId).First();
            if (config == null) return Result.Fail("配置不存在!");
            if (!config.IsSensitive) return Result.Success(config.ConfigValue);

            var decrypted = EncryptionUtil.AesDecrypt(config.ConfigValue);
            return Result.Success(new { value = decrypted });
        }

        public Result InitFromTemplate(int projectId, int templateId)
        {
            var templateConfigs = _fsql.Select<ProjectTemplateConfig>()
                .Where(c => c.TemplateId == templateId).ToList();

            var envs = _fsql.Select<ProjectEnvironment>()
                .Where(c => c.ProjectId == projectId).ToList();

            foreach (var tc in templateConfigs)
            {
                foreach (var env in envs)
                {
                    if (!Select.Where(c => c.ProjectId == projectId && c.EnvironmentId == env.Id && c.CategoryId == tc.CategoryId && c.ConfigKey == tc.ConfigKey).Any())
                    {
                        this.Insert(new ProjectConfig
                        {
                            ProjectId = projectId,
                            EnvironmentId = env.Id,
                            CategoryId = tc.CategoryId,
                            ConfigKey = tc.ConfigKey,
                            ConfigName = tc.ConfigName,
                            ConfigValue = tc.DefaultValue,
                            IsSensitive = tc.IsSensitive,
                            DisplayType = (ConfigDisplayType)tc.DisplayType,
                            Sort = tc.Sort,
                            Remark = tc.Remark,
                            CreatedTime = DateTime.Now
                        });
                    }
                }
            }
            return Result.Success("配置初始化成功!");
        }
    }
}

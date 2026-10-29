using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class FeatureModulesModuleInitializer : IModuleInitializer
{
    public string ModuleName => "功能模块";
    public int InitOrder => 130;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.FeatureModules.FeatureModule),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();
    public List<MenuDefinition> GetMenus() => new();
    public void SeedData(IFreeSql fsql) { }
}

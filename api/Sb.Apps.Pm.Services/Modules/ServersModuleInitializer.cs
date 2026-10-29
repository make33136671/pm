using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class ServersModuleInitializer : IModuleInitializer
{
    public string ModuleName => "服务器";
    public int InitOrder => 170;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.Servers.Server),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();
    public List<MenuDefinition> GetMenus() => new();
    public void SeedData(IFreeSql fsql) { }
}

using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class MindMapsModuleInitializer : IModuleInitializer
{
    public string ModuleName => "思维导图";
    public int InitOrder => 140;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.MindMaps.MindMap),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();

    public List<MenuDefinition> GetMenus() => new()
    {
        new MenuDefinition { Name = "MindMap", Path = "/mindmap", Component = "/mindmap/index", Title = "思维导图", Icon = "lucide:brain", OrderNo = 7 },
    };

    public void SeedData(IFreeSql fsql) { }
}

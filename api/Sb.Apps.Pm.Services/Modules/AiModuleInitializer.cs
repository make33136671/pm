using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class AiModuleInitializer : IModuleInitializer
{
    public string ModuleName => "AI 服务";
    public int InitOrder => 190;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.Ai.AiMemory),
        typeof(Sb.Apps.Pm.Models.Ai.AiProvider),
        typeof(Sb.Apps.Pm.Models.Ai.AiTask),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();
    public List<MenuDefinition> GetMenus() => new();
    public void SeedData(IFreeSql fsql) { }
}

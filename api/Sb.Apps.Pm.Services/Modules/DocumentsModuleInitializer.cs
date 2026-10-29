using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class DocumentsModuleInitializer : IModuleInitializer
{
    public string ModuleName => "文档管理";
    public int InitOrder => 180;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.Documents.DocVersion),
        typeof(Sb.Apps.Pm.Models.Documents.Notebook),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();
    public List<MenuDefinition> GetMenus() => new();
    public void SeedData(IFreeSql fsql) { }
}

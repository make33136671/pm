using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class DbMigrationModuleInitializer : IModuleInitializer
{
    public string ModuleName => "数据库迁移";
    public int InitOrder => 160;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.DbMigration.DbMigrationScript),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();
    public List<MenuDefinition> GetMenus() => new();
    public void SeedData(IFreeSql fsql) { }
}

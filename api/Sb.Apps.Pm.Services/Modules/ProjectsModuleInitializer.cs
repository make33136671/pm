using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class ProjectsModuleInitializer : IModuleInitializer
{
    public string ModuleName => "项目管理";
    public int InitOrder => 100;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.Projects.Project),
        typeof(Sb.Apps.Pm.Models.Projects.SubProject),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectCustomer),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectEnvironment),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectMilestone),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectConfig),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectConfigCategory),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectServer),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectActivity),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectTemplate),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectTemplateConfig),
        typeof(Sb.Apps.Pm.Models.Projects.ProjectDocument),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new()
    {
        new() { Key = "test_server_address", Name = "测试服务器地址",
                CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1,
                DisplayType = SystemSettingDisplayType.Input,
                DefaultValue = "", CantDelete = true, Sort = 1 },
        new() { Key = "test_server_port", Name = "测试服务器端口",
                CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1,
                DisplayType = SystemSettingDisplayType.InputNumber,
                DefaultValue = "", CantDelete = true, Sort = 2 },
        new() { Key = "test_server_auth_key", Name = "测试服务器授权Key",
                CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1,
                DisplayType = SystemSettingDisplayType.InputPassword,
                DefaultValue = "", CantDelete = true, Sort = 3 },
        new() { Key = "test_server_username", Name = "测试服务器用户名",
                CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1,
                DisplayType = SystemSettingDisplayType.Input,
                DefaultValue = "root", CantDelete = true, Sort = 4 },
    };

    public List<MenuDefinition> GetMenus() => new()
    {
        new MenuDefinition
        {
            Name = "Project", Path = "/project", Component = "BasicLayout",
            Redirect = "/project/list", Title = "项目管理",
            Icon = "lucide:folder-kanban", OrderNo = 2,
            Children = new()
            {
                new() { Name = "ProjectList", Path = "/project/list", Component = "/project/list/index", Title = "项目列表", Icon = "lucide:list", OrderNo = 1 },
                new() { Name = "ProjectTemplate", Path = "/project/template", Component = "/project/template/index", Title = "项目模板", Icon = "lucide:layout-template", OrderNo = 2 },
                new() { Name = "ProjectDetail", Path = "/project/detail", Component = "/project/detail/index", Title = "项目详情", OrderNo = 3, HideInMenu = true },
                new() { Name = "ProjectForm", Path = "/project/form", Component = "/project/form/index", Title = "项目表单", OrderNo = 4, HideInMenu = true },
            }
        }
    };

    public void SeedData(IFreeSql fsql) { }
}

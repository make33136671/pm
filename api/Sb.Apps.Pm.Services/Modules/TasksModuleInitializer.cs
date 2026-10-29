using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class TasksModuleInitializer : IModuleInitializer
{
    public string ModuleName => "任务管理";
    public int InitOrder => 120;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.Tasks.TaskItem),
        typeof(Sb.Apps.Pm.Models.Tasks.TaskItemLog),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();

    public List<MenuDefinition> GetMenus() => new()
    {
        // 顶级路由容器（隐藏，实际入口在项目管理下）
        new MenuDefinition
        {
            Name = "TaskItem", Path = "/task-item", Component = "BasicLayout",
            Redirect = "/task-item/list", Title = "任务管理",
            Icon = "lucide:check-square", OrderNo = 4, HideInMenu = true,
        },
        // 挂载到项目管理下
        new MenuDefinition { ParentPath = "/project", Name = "TaskItemList", Path = "/project/task-item/list", Component = "/task-item/list/index", Title = "任务列表", Icon = "lucide:list", OrderNo = 8 },
        new MenuDefinition { ParentPath = "/project", Name = "TaskItemForm", Path = "/project/task-item/form", Component = "/task-item/form/index", Title = "任务表单", OrderNo = 9, HideInMenu = true },
    };

    public void SeedData(IFreeSql fsql) { }
}

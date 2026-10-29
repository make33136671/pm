using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class RequirementsModuleInitializer : IModuleInitializer
{
    public string ModuleName => "需求管理";
    public int InitOrder => 110;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.Requirements.Requirement),
        typeof(Sb.Apps.Pm.Models.Requirements.RequirementComment),
        typeof(Sb.Apps.Pm.Models.Requirements.RequirementFeatureRel),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();

    public List<MenuDefinition> GetMenus() => new()
    {
        // 顶级路由容器（隐藏，实际入口在项目管理下）
        new MenuDefinition
        {
            Name = "Requirement", Path = "/requirement", Component = "BasicLayout",
            Redirect = "/requirement/list", Title = "需求管理",
            Icon = "lucide:clipboard-list", OrderNo = 3, HideInMenu = true,
        },
        // 挂载到项目管理下
        new MenuDefinition { ParentPath = "/project", Name = "FeatureModule", Path = "/project/feature-module", Component = "/feature-module/index", Title = "功能模块", Icon = "lucide:puzzle", OrderNo = 5 },
        new MenuDefinition { ParentPath = "/project", Name = "RequirementList", Path = "/project/requirement/list", Component = "/requirement/list/index", Title = "需求列表", Icon = "lucide:list", OrderNo = 6 },
        new MenuDefinition { ParentPath = "/project", Name = "RequirementForm", Path = "/project/requirement/form", Component = "/requirement/form/index", Title = "需求表单", OrderNo = 7, HideInMenu = true },
        new MenuDefinition { ParentPath = "/project", Name = "RequirementAnalysis", Path = "/project/requirement/analysis", Component = "/requirement/analysis/index", Title = "AI 需求分析", Icon = "lucide:brain", OrderNo = 8 },
    };

    public void SeedData(IFreeSql fsql) { }
}

using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class PipelinesModuleInitializer : IModuleInitializer
{
    public string ModuleName => "构建发布";
    public int InitOrder => 150;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.Pipelines.BuildRecord),
        typeof(Sb.Apps.Pm.Models.Pipelines.Changelog),
        typeof(Sb.Apps.Pm.Models.Pipelines.GitPipeline),
        typeof(Sb.Apps.Pm.Models.Pipelines.GitRepository),
        typeof(Sb.Apps.Pm.Models.Pipelines.GitWebhookLog),
        typeof(Sb.Apps.Pm.Models.Pipelines.PipelineProfile),
        typeof(Sb.Apps.Pm.Models.Pipelines.ReleaseBackup),
        typeof(Sb.Apps.Pm.Models.Pipelines.ReleaseRecord),
        typeof(Sb.Apps.Pm.Models.Pipelines.ApprovalRecord),
        typeof(Sb.Apps.Pm.Models.Pipelines.ScheduledTask),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();

    public List<MenuDefinition> GetMenus() => new()
    {
        new MenuDefinition
        {
            Name = "BuildRelease", Path = "/build-release", Component = "BasicLayout",
            Redirect = "/build-release/pipeline-profile", Title = "构建与发布",
            Icon = "lucide:rocket", OrderNo = 5,
            Children = new()
            {
                new() { Name = "PipelineProfileList", Path = "/build-release/pipeline-profile", Component = "/pipeline-profile/list/index", Title = "部署方案", Icon = "lucide:git-branch", OrderNo = 1 },
                new() { Name = "DeployChannelList", Path = "/build-release/deploy-channel", Component = "/deploy-channel/list/index", Title = "部署通道", Icon = "lucide:plug", OrderNo = 2 },
                new() { Name = "BuildList", Path = "/build-release/build", Component = "/build/list/index", Title = "构建记录", Icon = "lucide:hammer", OrderNo = 3 },
                new() { Name = "ReleaseList", Path = "/build-release/release", Component = "/release/list/index", Title = "发布管理", Icon = "lucide:upload", OrderNo = 4 },
                new() { Name = "BackupList", Path = "/build-release/backup", Component = "/release/backup/index", Title = "备份管理", Icon = "lucide:archive", OrderNo = 5 },
                new() { Name = "GitRepoList", Path = "/build-release/git-repo", Component = "/git/repo/index", Title = "Git 仓库", Icon = "lucide:git-merge", OrderNo = 6 },
            }
        }
    };

    public void SeedData(IFreeSql fsql) { }
}

using FreeSql;
using SB.Framework.Services.Configuration;

namespace Sb.Apps.Pm.Services.Configuration;

public class PmFreeSqlConfigurator : IFreeSqlConfigurator
{
    public int Order => 100;

    public void Configure(IFreeSql fsql)
    {
        // Projects 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.Project>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.SubProject>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.ProjectCustomer>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.ProjectEnvironment>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.ProjectMilestone>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.ProjectConfig>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.ProjectServer>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.ProjectTemplate>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Projects.ProjectDocument>("IsDeleted", a => a.IsDeleted == false);

        // Requirements 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Requirements.Requirement>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Requirements.RequirementComment>("IsDeleted", a => a.IsDeleted == false);

        // Tasks 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Tasks.TaskItem>("IsDeleted", a => a.IsDeleted == false);

        // FeatureModules / MindMaps / Servers 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.FeatureModules.FeatureModule>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.MindMaps.MindMap>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Servers.Server>("IsDeleted", a => a.IsDeleted == false);

        // Pipelines 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Pipelines.Changelog>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Pipelines.GitRepository>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Pipelines.PipelineProfile>("IsDeleted", a => a.IsDeleted == false);

        // DbMigration 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.DbMigration.DbMigrationScript>("IsDeleted", a => a.IsDeleted == false);

        // Documents 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Documents.Notebook>("IsDeleted", a => a.IsDeleted == false);

        // Ai 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Ai.AiMemory>("IsDeleted", a => a.IsDeleted == false);

        // Chat 域
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Chat.ChatAccount>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Chat.ChatBatch>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Chat.ChatGroupBinding>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Chat.ChatUserBinding>("IsDeleted", a => a.IsDeleted == false);
        fsql.GlobalFilter.Apply<Sb.Apps.Pm.Models.Chat.ChatRequirement>("IsDeleted", a => a.IsDeleted == false);

        // Agent 域
    }
}

using FreeSql;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace Sb.Apps.Pm.Services.Modules;

[AutoDependencyInjection]
public class ChatModuleInitializer : IModuleInitializer
{
    public string ModuleName => "聊天平台";
    public int InitOrder => 200;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(Sb.Apps.Pm.Models.Chat.AiPipeline),
        typeof(Sb.Apps.Pm.Models.Chat.AiPipelineStep),
        typeof(Sb.Apps.Pm.Models.Chat.ChatAccount),
        typeof(Sb.Apps.Pm.Models.Chat.ChatBatch),
        typeof(Sb.Apps.Pm.Models.Chat.ChatGroupBinding),
        typeof(Sb.Apps.Pm.Models.Chat.ChatMedia),
        typeof(Sb.Apps.Pm.Models.Chat.ChatMessage),
        typeof(Sb.Apps.Pm.Models.Chat.ChatRequirement),
        typeof(Sb.Apps.Pm.Models.Chat.ChatUserBinding),
    };

    public List<SystemSettingDefinition> GetSystemSettings() => new();

    public List<MenuDefinition> GetMenus() => new()
    {
        new MenuDefinition
        {
            Name = "Chat", Path = "/chat", Component = "BasicLayout",
            Redirect = "/chat/account", Title = "聊天平台",
            Icon = "lucide:message-circle", OrderNo = 10,
            Children = new()
            {
                new() { Name = "ChatAccount", Path = "/chat/account", Component = "/chat/account/index", Title = "账号管理", Icon = "lucide:key-round", OrderNo = 1 },
                new() { Name = "ChatGroup", Path = "/chat/group", Component = "/chat/group/index", Title = "群组绑定", Icon = "lucide:users", OrderNo = 2 },
                new() { Name = "ChatUser", Path = "/chat/user", Component = "/chat/user/index", Title = "用户绑定", Icon = "lucide:user-check", OrderNo = 3 },
                new() { Name = "ChatMessage", Path = "/chat/message", Component = "/chat/message/index", Title = "消息记录", Icon = "lucide:message-square", OrderNo = 4 },
                new() { Name = "ChatRequirement", Path = "/chat/requirement", Component = "/chat/requirement/index", Title = "需求提取", Icon = "lucide:lightbulb", OrderNo = 5 },
                new() { Name = "ChatBatch", Path = "/chat/batch", Component = "/chat/batch/index", Title = "分析批次", Icon = "lucide:layers", OrderNo = 6 },
                new() { Name = "AiPipeline", Path = "/chat/pipeline", Component = "/chat/pipeline/index", Title = "AI 管线", Icon = "lucide:workflow", OrderNo = 7 },
            }
        }
    };

    public void SeedData(IFreeSql fsql) { }
}

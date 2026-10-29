using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// 聊天平台账号管理
    /// </summary>
    [AutoDependencyInjection]
    public class ChatAccountService : BaseCrudService<ChatAccount>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "聊天平台";
        public int InitOrder => 180;

        public Type[] GetEntityTypes() => new[]
        {
            typeof(ChatAccount), typeof(ChatGroupBinding), typeof(ChatUserBinding),
            typeof(ChatMessage), typeof(ChatMedia),
            typeof(AiPipeline), typeof(AiPipelineStep), typeof(ChatRequirement),
            typeof(ChatBatch), typeof(ChatBatchMessage), typeof(ChatBatchRequirement), typeof(ChatBatchTask)
        };

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
        #endregion

        private readonly IFreeSql _fsql;
        private readonly ChatConnectorManager _connectorManager;

        public ChatAccountService(IFreeSql fsql, ILogger<ChatAccountService> logger,
            ChatConnectorManager connectorManager) : base(fsql, logger)
        {
            _fsql = fsql;
            _connectorManager = connectorManager;
        }

        public virtual Result GetList(string? platformType = null, ChatAccountType? accountType = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(!string.IsNullOrEmpty(platformType), c => c.PlatformType == platformType)
                .WhereIf(accountType.HasValue, c => c.AccountType == accountType.Value);
            query = ApplySorting(query, field, order);

            var data = query.Count(out long total).Page(page, pageSize).ToList();
            var currentEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "";

            var items = data.Select(a => new
            {
                a.Id, a.PlatformType, a.AccountType, a.AccountName, a.Env, a.Status, a.CreatedTime,
                Connected = _connectorManager.GetBot(a.Id)?.IsConnected == true
                            || _connectorManager.GetClient(a.Id) != null,
                CurrentAppEnv = currentEnv
            }).ToList();

            return Result.Page(items, total);
        }

        public virtual Result Create(ChatAccount account)
        {
            if (!string.IsNullOrEmpty(account.ConnectionConfig))
                account.ConnectionConfig = EncryptionUtil.AesEncrypt(account.ConnectionConfig);

            account.CreatedTime = DateTime.Now;
            account = this.Insert(account);
            return Result.Success("账号创建成功!", account);
        }

        public virtual Result Edit(ChatAccount account)
        {
            var hasNewConfig = !string.IsNullOrEmpty(account.ConnectionConfig);
            if (hasNewConfig && !account.ConnectionConfig!.Contains("=="))
                account.ConnectionConfig = EncryptionUtil.AesEncrypt(account.ConnectionConfig);

            int count;
            if (hasNewConfig)
            {
                count = UpdateDiy.UpdateColumns(c => new
                {
                    c.AccountName, c.PlatformType, c.AccountType, c.ConnectionConfig, c.Env, c.Status
                }).SetSource(account).ExecuteAffrows();
            }
            else
            {
                count = UpdateDiy.UpdateColumns(c => new
                {
                    c.AccountName, c.PlatformType, c.AccountType, c.Env, c.Status
                }).SetSource(account).ExecuteAffrows();
            }
            return count > 0 ? Result.Success("账号更新成功!") : Result.Fail("更新失败!");
        }
    }
}

using System.Collections.Concurrent;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Chat;
using SB.Chat.Telegram;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using Sb.Apps.Pm.Models;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// 聊天连接器生命周期管理
    /// 启动时自动连接所有 Active 状态的 Bot 账号
    /// </summary>
    [AutoDependencyInjection]
    public class ChatConnectorManager : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ChatConnectorManager> _logger;
        private readonly ConcurrentDictionary<int, IChatBot> _bots = new();
        private readonly ConcurrentDictionary<int, IChatClient> _clients = new();

        public ChatConnectorManager(IServiceProvider serviceProvider, ILogger<ChatConnectorManager> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>断开所有连接</summary>
        public async Task StopAllAsync()
        {
            foreach (var bot in _bots.Values)
            {
                try { await bot.StopPollingAsync(); await bot.DisconnectAsync(); }
                catch { }
            }
            foreach (var client in _clients.Values)
            {
                try { await client.DisconnectAsync(); }
                catch { }
            }
            _bots.Clear();
            _clients.Clear();
        }

        /// <summary>连接一个账号</summary>
        public async Task ConnectAccount(ChatAccount account)
        {
            var config = DecryptConfig(account.ConnectionConfig);
            if (config == null)
            {
                _logger.LogWarning("账号 {AccountName}(Id={Id}) 连接配置为空", account.AccountName, account.Id);
                return;
            }

            if (account.AccountType == ChatAccountType.Bot)
            {
                var token = config.GetValueOrDefault("BotToken", "");
                if (string.IsNullOrEmpty(token)) return;

                var bot = new TelegramBotConnector(token);
                await bot.ConnectAsync();

                // 注册事件监听（Polling 模式下消息通过事件触发）
                var accountId = account.Id;
                bot.OnMessage += (sender, message) => _ = HandleBotMessage(bot, message, accountId);
                bot.OnCallback += (sender, callback) => _ = HandleBotCallback(bot, callback, accountId);
                bot.OnGroupEvent += (sender, groupEvent) => HandleBotGroupEvent(groupEvent, accountId);

                // 启动 Polling
                await bot.StartPollingAsync();

                _bots[account.Id] = bot;
                _logger.LogInformation("Bot {AccountName}(Id={Id}) 已连接并启动 Polling", account.AccountName, account.Id);
            }
            else if (account.AccountType == ChatAccountType.Personal)
            {
                var apiId = config.GetValueOrDefault("ApiId", "");
                var apiHash = config.GetValueOrDefault("ApiHash", "");
                var phone = config.GetValueOrDefault("Phone", "");
                var sessionPath = config.GetValueOrDefault("SessionPath", $"sessions/{account.Id}.session");

                if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(apiHash)) return;

                var client = new TelegramClientConnector(apiId, apiHash, phone, sessionPath);

                // 注册消息监听（被动采集，存入数据库）
                var accountId = account.Id;
                client.OnMessage += (sender, message) => _ = SaveClientMessage(message, accountId);

                await client.ConnectAsync();
                _clients[account.Id] = client;
                _logger.LogInformation("个人账号 {AccountName}(Id={Id}) 已连接", account.AccountName, account.Id);
            }
        }

        /// <summary>断开一个账号</summary>
        public async Task DisconnectAccount(int accountId)
        {
            if (_bots.TryRemove(accountId, out var bot))
            {
                await bot.StopPollingAsync();
                await bot.DisconnectAsync();
            }
            if (_clients.TryRemove(accountId, out var client))
                await client.DisconnectAsync();
        }

        /// <summary>获取 Bot 连接器</summary>
        public IChatBot? GetBot(int accountId) => _bots.GetValueOrDefault(accountId);

        /// <summary>获取个人账号连接器</summary>
        public IChatClient? GetClient(int accountId) => _clients.GetValueOrDefault(accountId);

        /// <summary>获取所有已连接的 Bot</summary>
        public IReadOnlyDictionary<int, IChatBot> GetAllBots() => _bots;

        // ---- Bot Polling 事件处理 ----

        private async Task HandleBotMessage(IChatBot bot, SB.Chat.Models.ChatMessageEvent message, int accountId)
        {
            Console.WriteLine($"[ConnMgr] HandleBotMessage: ChatId={message.ChatId}, Content={message.Content}, IsPrivate={message.IsPrivateChat}");
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var commandHandler = scope.ServiceProvider.GetRequiredService<TelegramBotCommandHandler>();
                await commandHandler.HandleMessage(bot, message, accountId);
                Console.WriteLine("[ConnMgr] 命令处理完成");

                // 非命令消息保存到数据库
                if (string.IsNullOrEmpty(message.Content) || !message.Content.TrimStart().StartsWith("/"))
                {
                    await SaveBotMessage(message, accountId);
                    Console.WriteLine("[ConnMgr] 消息已保存");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConnMgr] HandleBotMessage 异常: {ex}");
            }
        }

        private async Task HandleBotCallback(IChatBot bot, SB.Chat.Models.ChatCallbackEvent callback, int accountId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var commandHandler = scope.ServiceProvider.GetRequiredService<TelegramBotCommandHandler>();
                await commandHandler.HandleCallback(bot, callback, accountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling 回调处理失败: Data={Data}", callback.CallbackData);
            }
        }

        private void HandleBotGroupEvent(SB.Chat.Models.ChatGroupEvent groupEvent, int accountId)
        {
            _logger.LogInformation("群组事件: Type={EventType}, ChatId={ChatId}", groupEvent.EventType, groupEvent.ChatId);
            if (groupEvent.EventType == SB.Chat.Models.ChatGroupEventType.BotAdded)
            {
                _ = CreatePendingGroupBinding(groupEvent, accountId);
            }
        }

        private async Task SaveBotMessage(SB.Chat.Models.ChatMessageEvent message, int accountId)
        {
            using var scope = _serviceProvider.CreateScope();
            var messageService = scope.ServiceProvider.GetRequiredService<ChatMessageService>();
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();

            // 群组消息查找绑定，私聊消息 GroupBindingId 为 0
            var groupBinding = message.IsPrivateChat ? null : groupService.FindByPlatformGroupId(message.ChatId);
            if (!message.IsPrivateChat && groupBinding == null) return; // 未绑定的群组不保存

            var chatMessage = new ChatMessage
            {
                GroupBindingId = groupBinding?.Id,
                AccountId = accountId,
                PlatformMessageId = message.MessageId,
                SenderId = message.SenderId,
                SenderName = message.SenderName,
                MessageType = MapClientMessageType(message.MessageType),
                Content = message.Content,
                ReplyToMessageId = message.ReplyToMessageId
            };

            var saved = messageService.SaveMessage(chatMessage);

            // 下载媒体附件 & 自动归档到项目文档
            if (message.Media.Count > 0 && _bots.TryGetValue(accountId, out var bot))
            {
                var mediaService = scope.ServiceProvider.GetRequiredService<ChatMediaService>();
                var savedMedia = await mediaService.DownloadAndSave(saved.Id, message.Media, bot);

                // 自动归档：文件类消息存入项目文档（待分类）
                if (savedMedia.Count > 0 && groupBinding != null && groupBinding.ProjectId > 0)
                {
                    ArchiveMediaToDocument(scope.ServiceProvider, savedMedia, groupBinding.ProjectId, message.SenderName);
                }
            }

            // 实时 AI 分析
            if (groupBinding != null && groupBinding.AiEnabled && groupBinding.AiRealtimeEnabled)
            {
                try
                {
                    var executor = scope.ServiceProvider.GetRequiredService<AiPipelineExecutor>();
                    await executor.ProcessRealtimeMessage(saved, groupBinding);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "实时 AI 分析失败: MessageId={Id}", saved.Id);
                }
            }
        }

        private async Task CreatePendingGroupBinding(SB.Chat.Models.ChatGroupEvent groupEvent, int accountId)
        {
            using var scope = _serviceProvider.CreateScope();
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();
            var existing = groupService.FindByPlatformGroupId(groupEvent.ChatId);
            if (existing != null) return;

            var binding = new ChatGroupBinding
            {
                AccountId = accountId,
                ProjectId = 0,
                PlatformGroupId = groupEvent.ChatId,
                GroupName = groupEvent.ChatTitle,
                Status = ChatGroupBindingStatus.Pending,
                CreatedTime = DateTime.Now
            };
            groupService.Insert(binding);
            _logger.LogInformation("Bot 加入新群组: {GroupName} (ChatId={ChatId})", groupEvent.ChatTitle, groupEvent.ChatId);
        }

        /// <summary>个人账号消息监听回调 — 保存到数据库</summary>
        private async Task SaveClientMessage(SB.Chat.Models.ChatMessageEvent message, int accountId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var messageService = scope.ServiceProvider.GetRequiredService<ChatMessageService>();
                var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();

                var groupBinding = groupService.FindByPlatformGroupId(message.ChatId);
                if (groupBinding == null) return;

                var chatMessage = new ChatMessage
                {
                    GroupBindingId = groupBinding.Id,
                    AccountId = accountId,
                    PlatformMessageId = message.MessageId,
                    SenderId = message.SenderId,
                    SenderName = message.SenderName,
                    MessageType = MapClientMessageType(message.MessageType),
                    Content = message.Content,
                    ReplyToMessageId = message.ReplyToMessageId
                };

                var saved = messageService.SaveMessage(chatMessage);

                // 下载媒体附件 & 自动归档到项目文档
                if (message.Media.Count > 0 && _clients.TryGetValue(accountId, out var client))
                {
                    var mediaService = scope.ServiceProvider.GetRequiredService<ChatMediaService>();
                    var savedMedia = await mediaService.DownloadAndSave(saved.Id, message.Media, client);

                    if (savedMedia.Count > 0 && groupBinding != null && groupBinding.ProjectId > 0)
                    {
                        ArchiveMediaToDocument(scope.ServiceProvider, savedMedia, groupBinding.ProjectId, message.SenderName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "个人账号消息保存失败: AccountId={AccountId}, ChatId={ChatId}",
                    accountId, message.ChatId);
            }
        }

        private static ChatMessageType MapClientMessageType(SB.Chat.Models.ChatMessageType type) => type switch
        {
            SB.Chat.Models.ChatMessageType.Image => ChatMessageType.Image,
            SB.Chat.Models.ChatMessageType.File => ChatMessageType.File,
            SB.Chat.Models.ChatMessageType.Audio => ChatMessageType.Audio,
            SB.Chat.Models.ChatMessageType.Video => ChatMessageType.Video,
            SB.Chat.Models.ChatMessageType.Sticker => ChatMessageType.Sticker,
            _ => ChatMessageType.Text
        };

        private Dictionary<string, string>? DecryptConfig(string? encryptedConfig)
        {
            if (string.IsNullOrEmpty(encryptedConfig)) return null;
            try
            {
                var decrypted = EncryptionUtil.AesDecrypt(encryptedConfig);
                _logger.LogDebug("解密配置结果: {Config}", decrypted);

                // 尝试 JSON 解析
                if (decrypted.TrimStart().StartsWith("{"))
                {
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted);
                }

                // 兼容直接存 Token 的情况
                return new Dictionary<string, string> { { "BotToken", decrypted } };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解密连接配置失败");
                return null;
            }
        }

        /// <summary>将媒体文件归档到项目文档（Inbox 待分类）</summary>
        private void ArchiveMediaToDocument(IServiceProvider sp, List<ChatMedia> mediaList, int projectId, string? senderName)
        {
            try
            {
                var fsql = sp.GetRequiredService<IFreeSql>();
                var docs = mediaList.Select(media => new ProjectDocument
                {
                    ProjectId = projectId,
                    Title = media.FileName ?? "未命名文件",
                    Category = "Inbox",
                    FilePath = $"chat-media/{media.LocalPath}",
                    FileSize = media.FileSize,
                    FileType = Path.GetExtension(media.FileName ?? "").TrimStart('.').ToLower(),
                    OperatorName = senderName,
                    CreatedTime = DateTime.Now
                }).ToList();
                fsql.Insert(docs).ExecuteAffrows();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动归档媒体文件到项目文档失败");
            }
        }

        public void Dispose()
        {
            foreach (var bot in _bots.Values) bot.Dispose();
            foreach (var client in _clients.Values) client.Dispose();
            _bots.Clear();
            _clients.Clear();
            GC.SuppressFinalize(this);
        }
    }
}

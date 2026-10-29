using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SB.Chat;
using SB.Chat.Models;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Ai;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// Telegram Webhook 入口处理
    /// 接收 Webhook 请求 → 分发到命令处理器和消息存储
    /// </summary>
    public class TelegramWebhookHandler
    {
        private readonly ChatConnectorManager _connectorManager;
        private readonly TelegramBotCommandHandler _commandHandler;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TelegramWebhookHandler> _logger;

        public TelegramWebhookHandler(
            ChatConnectorManager connectorManager,
            TelegramBotCommandHandler commandHandler,
            IServiceProvider serviceProvider,
            ILogger<TelegramWebhookHandler> logger)
        {
            _connectorManager = connectorManager;
            _commandHandler = commandHandler;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// 处理指定账号的 Webhook 请求
        /// </summary>
        public async Task HandleWebhook(int accountId, string payload)
        {
            var bot = _connectorManager.GetBot(accountId);
            if (bot == null)
            {
                _logger.LogWarning("收到 Webhook 但账号 {AccountId} 未连接", accountId);
                return;
            }

            // 注册事件处理（一次性）
            void OnMessage(object? sender, ChatMessageEvent e) => _ = ProcessMessage(bot, e, accountId);
            void OnCallback(object? sender, ChatCallbackEvent e) => _ = ProcessCallback(bot, e, accountId);
            void OnGroupEvent(object? sender, ChatGroupEvent e) => _ = ProcessGroupEvent(e, accountId);

            bot.OnMessage += OnMessage;
            bot.OnCallback += OnCallback;
            bot.OnGroupEvent += OnGroupEvent;

            try
            {
                await bot.ProcessWebhookAsync(payload);
            }
            finally
            {
                bot.OnMessage -= OnMessage;
                bot.OnCallback -= OnCallback;
                bot.OnGroupEvent -= OnGroupEvent;
            }
        }

        private async Task ProcessMessage(IChatBot bot, ChatMessageEvent message, int accountId)
        {
            try
            {
                // 1. 尝试处理命令
                await _commandHandler.HandleMessage(bot, message, accountId);

                // 2. 如果不是命令，保存消息记录
                if (string.IsNullOrEmpty(message.Content) || !message.Content.TrimStart().StartsWith("/"))
                {
                    await SaveMessage(message, accountId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息失败: ChatId={ChatId}, MessageId={MessageId}", message.ChatId, message.MessageId);
            }
        }

        private async Task ProcessCallback(IChatBot bot, ChatCallbackEvent callback, int accountId)
        {
            try
            {
                await _commandHandler.HandleCallback(bot, callback, accountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理回调失败: CallbackData={Data}", callback.CallbackData);
            }
        }

        private Task ProcessGroupEvent(ChatGroupEvent groupEvent, int accountId)
        {
            _logger.LogInformation("群组事件: Type={EventType}, ChatId={ChatId}, ChatTitle={ChatTitle}",
                groupEvent.EventType, groupEvent.ChatId, groupEvent.ChatTitle);

            if (groupEvent.EventType == ChatGroupEventType.BotAdded)
            {
                // Bot 被加入新群组 → 自动创建待绑定记录
                _ = CreatePendingGroupBinding(groupEvent, accountId);
            }

            return Task.CompletedTask;
        }

        /// <summary>保存消息到数据库（含媒体下载）</summary>
        private async Task SaveMessage(ChatMessageEvent message, int accountId)
        {
            using var scope = _serviceProvider.CreateScope();
            var messageService = scope.ServiceProvider.GetRequiredService<ChatMessageService>();
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();

            // 查找消息所属的群组绑定
            var groupBinding = groupService.FindByPlatformGroupId(message.ChatId);
            if (groupBinding == null) return; // 未绑定的群组不保存

            var chatMessage = new ChatMessage
            {
                GroupBindingId = groupBinding.Id,
                AccountId = accountId,
                PlatformMessageId = message.MessageId,
                SenderId = message.SenderId,
                SenderName = message.SenderName,
                MessageType = MapMessageType(message.MessageType),
                Content = message.Content,
                ReplyToMessageId = message.ReplyToMessageId,
                RawData = message.RawData != null ? JsonSerializer.Serialize(message.RawData) : null
            };

            var saved = messageService.SaveMessage(chatMessage);

            // 下载媒体附件
            if (message.Media.Count > 0)
            {
                var bot = _connectorManager.GetBot(accountId);
                if (bot != null)
                {
                    var mediaService = scope.ServiceProvider.GetRequiredService<ChatMediaService>();
                    await mediaService.DownloadAndSave(saved.Id, message.Media, bot);
                }
            }

            // 实时 AI 分析
            if (groupBinding.AiEnabled && groupBinding.AiRealtimeEnabled)
            {
                try
                {
                    var executor = scope.ServiceProvider.GetRequiredService<AiPipelineExecutor>();
                    await executor.ProcessRealtimeMessage(saved, groupBinding);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "实时 AI 分析失败: MessageId={MessageId}", saved.Id);
                }
            }

            // 自动需求创建：非命令、内容有实质内容（>20字）、群组开启了 AutoCreateRequirement
            if (groupBinding.AiEnabled
                && groupBinding.AutoCreateRequirement
                && !message.Content.TrimStart().StartsWith("/")
                && message.Content.Length > 20)
            {
                _ = AutoAnalyzeAndCreateRequirement(saved, groupBinding, message.ChatId);
            }
        }

        private async Task AutoAnalyzeAndCreateRequirement(ChatMessage saved, ChatGroupBinding groupBinding, string chatId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
                var provider = fsql.Select<AiProvider>()
                    .Where(p => p.Status == AiProviderStatus.Active)
                    .OrderByDescending(p => p.IsDefault)
                    .First();
                if (provider == null) return;

                var factory = scope.ServiceProvider.GetRequiredService<AiProviderFactory>();
                var adapter = factory.GetAdapter(provider.ProviderType);
                if (adapter == null) return;

                var prompt = "你是需求识别助手。判断下面这段对话是否包含一个明确的产品需求或 bug 反馈，" +
                    "置信度用 0-1 的小数表示。只返回 JSON：\n" +
                    "{\"isRequirement\":true/false,\"confidence\":0.85,\"title\":\"简洁标题\",\"description\":\"补充描述\"}\n" +
                    "不要输出 JSON 以外的内容。\n\n输入：\n" + saved.Content;

                var raw = await adapter.GenerateAsync(provider, prompt);
                var json = ExtractJson(raw);
                if (string.IsNullOrWhiteSpace(json)) return;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("isRequirement", out var isReqProp) || !isReqProp.GetBoolean()) return;
                if (!root.TryGetProperty("confidence", out var confProp) || confProp.GetDouble() < 0.7) return;

                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var desc = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(title)) return;

                var reqService = scope.ServiceProvider.GetRequiredService<RequirementService>();
                var result = reqService.Create(new Requirement
                {
                    ProjectId = groupBinding.ProjectId,
                    Title = title.Length > 500 ? title[..500] : title,
                    Content = string.IsNullOrWhiteSpace(desc) ? saved.Content : desc,
                    Source = RequirementSource.Telegram,
                    SourceRef = $"tg:auto:{saved.Id}",
                    Status = RequirementStatus.Draft,
                    IsConfirmed = false,
                    IsAiFriendly = true,
                });

                if (result.Code == 0 && result.Data is Requirement req)
                {
                    var bot = _connectorManager.GetBot(groupBinding.AccountId);
                    if (bot != null)
                        await bot.SendTextAsync(chatId, $"🤖 AI 识别到需求建议 #{req.Id}：{req.Title}\n点击查看并确认");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoAnalyzeAndCreateRequirement 失败: MessageId={MessageId}", saved.Id);
            }
        }

        private static string ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            return start >= 0 && end > start ? raw[start..(end + 1)] : "";
        }

        /// <summary>Bot 加入新群组时创建待绑定记录</summary>
        private async Task CreatePendingGroupBinding(ChatGroupEvent groupEvent, int accountId)
        {
            using var scope = _serviceProvider.CreateScope();
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();

            // 检查是否已绑定
            var existing = groupService.FindByPlatformGroupId(groupEvent.ChatId);
            if (existing != null) return;

            var binding = new ChatGroupBinding
            {
                AccountId = accountId,
                ProjectId = 0, // 待后台分配
                PlatformGroupId = groupEvent.ChatId,
                GroupName = groupEvent.ChatTitle,
                Status = ChatGroupBindingStatus.Pending,
                CreatedTime = DateTime.Now
            };

            groupService.Insert(binding);
            _logger.LogInformation("Bot 加入新群组，已创建待绑定记录: {GroupName} (ChatId={ChatId})",
                groupEvent.ChatTitle, groupEvent.ChatId);
        }

        private static Models.ChatMessageType MapMessageType(SB.Chat.Models.ChatMessageType type) => type switch
        {
            SB.Chat.Models.ChatMessageType.Image => Models.ChatMessageType.Image,
            SB.Chat.Models.ChatMessageType.File => Models.ChatMessageType.File,
            SB.Chat.Models.ChatMessageType.Audio => Models.ChatMessageType.Audio,
            SB.Chat.Models.ChatMessageType.Video => Models.ChatMessageType.Video,
            SB.Chat.Models.ChatMessageType.Sticker => Models.ChatMessageType.Sticker,
            _ => Models.ChatMessageType.Text
        };
    }
}

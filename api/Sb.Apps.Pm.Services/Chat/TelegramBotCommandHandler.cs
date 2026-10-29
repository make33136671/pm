using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SB.Chat;
using SB.Chat.Models;
using Sb.Apps.Pm.Models;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// Telegram Bot 命令/回调分发处理
    /// 处理 /bind、/verify、/unbind、/menu 等命令和 Inline Keyboard 回调
    /// </summary>
    public class TelegramBotCommandHandler
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TelegramBotCommandHandler> _logger;

        public TelegramBotCommandHandler(IServiceProvider serviceProvider, ILogger<TelegramBotCommandHandler> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>处理消息（检测命令）</summary>
        public async Task HandleMessage(IChatBot bot, ChatMessageEvent message, int accountId)
        {
            if (string.IsNullOrEmpty(message.Content)) return;

            var text = message.Content.Trim();
            if (!text.StartsWith("/")) return;

            // 解析命令和参数
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower().Split('@')[0]; // 去掉 @botname
            var args = parts.Length > 1 ? parts[1].Trim() : "";

            switch (command)
            {
                case "/start":
                    await HandleStart(bot, message);
                    break;
                case "/bind":
                    await HandleBind(bot, message, args);
                    break;
                case "/verify":
                    await HandleVerify(bot, message, args);
                    break;
                case "/unbind":
                    await HandleUnbind(bot, message);
                    break;
                case "/menu":
                    await HandleMenu(bot, message);
                    break;
                case "/newreq":
                    await HandleNewReq(bot, message, args);
                    break;
                case "/newtask":
                    await HandleNewTask(bot, message, args);
                    break;
                case "/ai":
                    await HandleAiRequirement(bot, message, args);
                    break;
                case "/mystatus":
                    await HandleMyStatus(bot, message);
                    break;
            }
        }

        /// <summary>处理回调（Inline Keyboard 点击）</summary>
        public async Task HandleCallback(IChatBot bot, ChatCallbackEvent callback, int accountId)
        {
            var data = callback.CallbackData;
            _logger.LogInformation("收到回调: UserId={UserId}, Data={Data}", callback.UserId, data);

            // 应答回调（消除加载状态）
            await bot.AnswerCallbackAsync(callback.CallbackId);

            // 根据回调数据路由处理
            if (data.StartsWith("menu:"))
            {
                await HandleMenuCallback(bot, callback, data[5..]);
            }
        }

        // ---- 命令处理 ----

        private async Task HandleStart(IChatBot bot, ChatMessageEvent message)
        {
            if (message.IsPrivateChat)
            {
                await bot.SendTextAsync(message.ChatId,
                    "👋 欢迎使用项目管理 Bot!\n\n" +
                    "可用命令:\n" +
                    "/verify [验证码] - 绑定系统账号\n" +
                    "/unbind - 解绑账号\n" +
                    "/menu - 打开功能菜单");
            }
        }

        private async Task HandleBind(IChatBot bot, ChatMessageEvent message, string bindCode)
        {
            if (message.IsPrivateChat)
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ /bind 命令请在群组中使用");
                return;
            }

            if (string.IsNullOrEmpty(bindCode))
            {
                await bot.SendTextAsync(message.ChatId, "用法: /bind [绑定码]");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();

            var result = groupService.BindByCode(bindCode.Trim().ToUpper(), message.ChatId, message.ChatTitle ?? "");
            await bot.SendTextAsync(message.ChatId, result.Code == 0
                ? "✅ 群组绑定成功!"
                : $"❌ {result.Message}");
        }

        private async Task HandleVerify(IChatBot bot, ChatMessageEvent message, string verifyCode)
        {
            if (!message.IsPrivateChat)
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ /verify 命令请私聊 Bot 使用", message.MessageId);
                return;
            }

            if (string.IsNullOrEmpty(verifyCode))
            {
                await bot.SendTextAsync(message.ChatId, "用法: /verify [验证码]");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();

            var result = userService.VerifyAndBind(verifyCode.Trim(), message.SenderId, message.SenderName);
            await bot.SendTextAsync(message.ChatId, result.Code == 0
                ? "✅ 账号绑定成功! 现在可以使用 /menu 查看功能菜单"
                : $"❌ {result.Message}");
        }

        private async Task HandleUnbind(IChatBot bot, ChatMessageEvent message)
        {
            if (!message.IsPrivateChat)
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ /unbind 命令请私聊 Bot 使用", message.MessageId);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();

            var binding = userService.FindByPlatformUserId("Telegram", message.SenderId);
            if (binding == null)
            {
                await bot.SendTextAsync(message.ChatId, "您尚未绑定任何账号");
                return;
            }

            userService.Del(binding.Id);
            await bot.SendTextAsync(message.ChatId, "✅ 账号已解绑");
        }

        private async Task HandleMenu(IChatBot bot, ChatMessageEvent message)
        {
            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();

            var binding = userService.FindByPlatformUserId("Telegram", message.SenderId);

            if (binding == null)
            {
                // 未绑定用户
                var buttons = new List<List<MenuButton>>
                {
                    new() { new MenuButton("🔗 绑定账号", "menu:bind_guide") }
                };
                await bot.SendMenuAsync(message.ChatId, "您尚未绑定系统账号，请先绑定:", buttons);
                return;
            }

            // 根据角色展示不同菜单
            var menuButtons = BuildMenuByRole(binding.Role);
            await bot.SendMenuAsync(message.ChatId, "📋 功能菜单:", menuButtons);
        }

        // ---- 命令：创建 / AI / 状态 ----

        /// <summary>/newreq &lt;需求描述&gt; —— 在当前绑定项目下创建一条需求</summary>
        private async Task HandleNewReq(IChatBot bot, ChatMessageEvent message, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                await bot.SendTextAsync(message.ChatId, "用法: /newreq <需求描述>\n例: /newreq 登录页支持微信扫码登录", message.MessageId);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();
            if (userService.FindByPlatformUserId("Telegram", message.SenderId) == null)
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ 请先绑定系统账号（私聊 Bot 发送 /verify [验证码]）", message.MessageId);
                return;
            }

            var projectId = FindProjectId(scope, message.ChatId, message.SenderId);
            if (projectId == null)
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ 未找到关联项目，请先在群组 /bind", message.MessageId);
                return;
            }

            var title = args.Length > 100 ? args[..100] : args;
            var reqService = scope.ServiceProvider.GetRequiredService<RequirementService>();
            var result = reqService.Create(new Requirement
            {
                ProjectId = projectId.Value,
                Title = title,
                Content = args,
                Source = RequirementSource.Telegram,
                SourceRef = $"tg:{message.SenderId}",
                Status = RequirementStatus.Todo,
                IsConfirmed = true,
            });

            await bot.SendTextAsync(message.ChatId,
                result.Code == 0 && result.Data is Requirement req
                    ? $"✅ 已创建需求 #{req.Id}\n{title}"
                    : $"❌ 创建失败: {result.Message}", message.MessageId);
        }

        /// <summary>/newtask &lt;需求ID&gt; &lt;任务描述&gt; —— 在指定需求下创建任务</summary>
        private async Task HandleNewTask(IChatBot bot, ChatMessageEvent message, string args)
        {
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[0], out var reqId))
            {
                await bot.SendTextAsync(message.ChatId, "用法: /newtask <需求ID> <任务描述>\n例: /newtask 42 实现微信扫码前端页面", message.MessageId);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();
            var binding = userService.FindByPlatformUserId("Telegram", message.SenderId);
            if (binding == null || (binding.Role != ChatUserRole.TeamMember && binding.Role != ChatUserRole.Admin))
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ 仅团队成员 / 管理员可创建任务", message.MessageId);
                return;
            }

            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            var req = fsql.Select<Requirement>().Where(r => r.Id == reqId && !r.IsDeleted).First();
            if (req == null)
            {
                await bot.SendTextAsync(message.ChatId, $"⚠️ 需求 #{reqId} 不存在", message.MessageId);
                return;
            }

            var title = parts[1].Length > 200 ? parts[1][..200] : parts[1];
            var taskService = scope.ServiceProvider.GetRequiredService<TaskItemService>();
            var result = taskService.Create(new TaskItem
            {
                ProjectId = req.ProjectId,
                RequirementId = reqId,
                Title = title,
                Source = "tg_bot",
                SourceRef = $"tg:{message.SenderId}",
            });

            await bot.SendTextAsync(message.ChatId,
                result.Code == 0 && result.Data is TaskItem t
                    ? $"✅ 已创建任务 #{t.Id}（需求 #{reqId}）\n{title}"
                    : $"❌ 创建失败: {result.Message}", message.MessageId);
        }

        /// <summary>/ai &lt;自然语言&gt; —— AI 把一段话拆成需求 + 多个任务</summary>
        private async Task HandleAiRequirement(IChatBot bot, ChatMessageEvent message, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                await bot.SendTextAsync(message.ChatId, "用法: /ai <自然语言描述>\n例: /ai 登录页加微信扫码，点图标弹二维码，扫码后登录并跳首页", message.MessageId);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();
            if (userService.FindByPlatformUserId("Telegram", message.SenderId) == null)
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ 请先绑定系统账号（私聊 Bot 发送 /verify [验证码]）", message.MessageId);
                return;
            }

            var projectId = FindProjectId(scope, message.ChatId, message.SenderId);
            if (projectId == null)
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ 未找到关联项目，请先在群组 /bind", message.MessageId);
                return;
            }

            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            // 选 provider：优先 IsDefault，其次第一个 Active（与 ActiveAiProvider 一致）
            var provider = fsql.Select<AiProvider>()
                .Where(p => p.Status == AiProviderStatus.Active)
                .OrderByDescending(p => p.IsDefault)
                .First();
            var factory = scope.ServiceProvider.GetRequiredService<AiProviderFactory>();
            var adapter = provider == null ? null : factory.GetAdapter(provider.ProviderType);
            if (provider == null || adapter == null)
            {
                await bot.SendTextAsync(message.ChatId, "⚠️ 未配置可用的 AI Provider", message.MessageId);
                return;
            }

            await bot.SendTextAsync(message.ChatId, "🤖 正在分析…", message.MessageId);

            var prompt = "你是需求分析助手。把下面这段自然语言整理成一条需求和若干开发任务，只返回 JSON：\n" +
                "{\"title\":\"简洁标题\",\"description\":\"详细描述\",\"tasks\":[{\"name\":\"任务名\",\"type\":\"Development|Bug|Design|Testing|Docs|Other\"}]}\n" +
                "不要输出 JSON 以外的内容。\n\n输入：\n" + args;

            AiReqResult? parsed = null;
            try
            {
                var raw = await adapter.GenerateAsync(provider, prompt);
                parsed = JsonSerializer.Deserialize<AiReqResult>(ExtractJson(raw),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "/ai 分析失败");
            }

            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Title))
            {
                await bot.SendTextAsync(message.ChatId, "❌ AI 未返回有效结果，请换种说法再试", message.MessageId);
                return;
            }

            var reqService = scope.ServiceProvider.GetRequiredService<RequirementService>();
            var reqResult = reqService.Create(new Requirement
            {
                ProjectId = projectId.Value,
                Title = parsed.Title.Length > 500 ? parsed.Title[..500] : parsed.Title,
                Content = string.IsNullOrWhiteSpace(parsed.Description) ? args : parsed.Description,
                Source = RequirementSource.Telegram,
                SourceRef = $"tg:{message.SenderId}",
                Status = RequirementStatus.Todo,
                IsConfirmed = true,
                IsAiFriendly = true,
            });
            if (reqResult.Code != 0 || reqResult.Data is not Requirement createdReq)
            {
                await bot.SendTextAsync(message.ChatId, $"❌ 需求创建失败: {reqResult.Message}", message.MessageId);
                return;
            }

            var taskService = scope.ServiceProvider.GetRequiredService<TaskItemService>();
            var taskCount = 0;
            foreach (var t in parsed.Tasks ?? new())
            {
                if (string.IsNullOrWhiteSpace(t.Name)) continue;
                var type = Enum.TryParse<TaskItemType>(t.Type, true, out var tt) ? tt : TaskItemType.Development;
                var r = taskService.Create(new TaskItem
                {
                    ProjectId = projectId.Value,
                    RequirementId = createdReq.Id,
                    Title = t.Name.Length > 200 ? t.Name[..200] : t.Name,
                    Type = type,
                    Source = "ai_auto",
                    SourceRef = $"tg:{message.SenderId}",
                });
                if (r.Code == 0) taskCount++;
            }

            await bot.SendTextAsync(message.ChatId,
                $"✅ 已创建需求 #{createdReq.Id}（{createdReq.Title}），含 {taskCount} 个任务", message.MessageId);
        }

        /// <summary>/mystatus —— 当前项目我的任务/需求概览</summary>
        private async Task HandleMyStatus(IChatBot bot, ChatMessageEvent message)
        {
            using var scope = _serviceProvider.CreateScope();
            var text = BuildMyStatusText(scope, message.ChatId, message.SenderId);
            await bot.SendTextAsync(message.ChatId, text, message.MessageId);
        }

        private string BuildMyStatusText(IServiceScope scope, string chatId, string userId)
        {
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();
            if (userService.FindByPlatformUserId("Telegram", userId) == null)
                return "⚠️ 请先绑定系统账号（私聊 Bot 发送 /verify [验证码]）";

            var projectId = FindProjectId(scope, chatId, userId);
            if (projectId == null) return "⚠️ 未找到关联项目，请先 /bind";

            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            var project = fsql.Select<Project>().Where(p => p.Id == projectId.Value).First();

            var todo = fsql.Select<TaskItem>().Where(t => t.ProjectId == projectId.Value && !t.IsDeleted && t.ExecStatus == TaskItemExecStatus.Idle).Count();
            var running = fsql.Select<TaskItem>().Where(t => t.ProjectId == projectId.Value && !t.IsDeleted && t.ExecStatus == TaskItemExecStatus.Running).Count();
            var done = fsql.Select<TaskItem>().Where(t => t.ProjectId == projectId.Value && !t.IsDeleted && t.ExecStatus == TaskItemExecStatus.Success).Count();
            var reqInProgress = fsql.Select<Requirement>().Where(r => r.ProjectId == projectId.Value && r.IsConfirmed == true && !r.IsDeleted && r.Status == RequirementStatus.InProgress).Count();
            var reqTotal = fsql.Select<Requirement>().Where(r => r.ProjectId == projectId.Value && r.IsConfirmed == true && !r.IsDeleted).Count();

            return $"📊 <b>我的状态 · {project?.Name ?? "项目"}</b>\n\n" +
                   $"任务: ⏳ 待办 {todo} | 🔄 进行中 {running} | ✅ 完成 {done}\n" +
                   $"需求: 进行中 {reqInProgress} / 共 {reqTotal}";
        }

        /// <summary>从 AI 输出里抽出 JSON 主体（去掉 ```json 围栏或前后解释文字）</summary>
        private static string ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "{}";
            var s = raw.Trim();
            var start = s.IndexOf('{');
            var end = s.LastIndexOf('}');
            return start >= 0 && end > start ? s.Substring(start, end - start + 1) : s;
        }

        private class AiReqResult
        {
            public string? Title { get; set; }
            public string? Description { get; set; }
            public List<AiReqTask>? Tasks { get; set; }
        }

        private class AiReqTask
        {
            public string? Name { get; set; }
            public string? Type { get; set; }
        }

        // ---- 菜单回调 ----

        private async Task HandleMenuCallback(IChatBot bot, ChatCallbackEvent callback, string action)
        {
            switch (action)
            {
                case "bind_guide":
                    await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                        "绑定方式:\n\n" +
                        "1️⃣ <b>内部用户:</b> 在系统个人设置中获取验证码，然后私聊 Bot 发送 /verify [验证码]\n\n" +
                        "2️⃣ <b>外部客户:</b> 请联系管理员获取绑定邀请链接");
                    break;

                case "my_status":
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var text = BuildMyStatusText(scope, callback.ChatId, callback.UserId);
                        await bot.EditMessageAsync(callback.ChatId, callback.MessageId, text, BackButton());
                    }
                    break;

                case "new_req_help":
                    await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                        "➕ <b>提需求</b>\n\n在群里发送：\n<code>/newreq 需求描述</code>\n例: /newreq 登录页支持微信扫码", BackButton());
                    break;

                case "ai_req_help":
                    await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                        "🤖 <b>AI 需求</b>\n\n用自然语言描述，AI 自动拆成需求 + 任务：\n<code>/ai 你的描述</code>", BackButton());
                    break;

                case "my_requirements":
                    await QueryMyRequirements(bot, callback);
                    break;

                case "project_progress":
                    await QueryProjectProgress(bot, callback);
                    break;

                case "recent_updates":
                    await QueryRecentUpdates(bot, callback);
                    break;

                case "my_tasks":
                    await QueryMyTasks(bot, callback);
                    break;

                case "ai_control":
                    await ShowAiControlMenu(bot, callback);
                    break;

                case "ai_on":
                    await ToggleAi(bot, callback, true);
                    break;
                case "ai_off":
                    await ToggleAi(bot, callback, false);
                    break;
                case "ai_rt_on":
                    await ToggleAiRealtime(bot, callback, true);
                    break;
                case "ai_rt_off":
                    await ToggleAiRealtime(bot, callback, false);
                    break;

                case "back_main":
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();
                        var binding = userService.FindByPlatformUserId("Telegram", callback.UserId);
                        var role = binding?.Role ?? ChatUserRole.Client;
                        var menuButtons = BuildMenuByRole(role);
                        await bot.EditMessageAsync(callback.ChatId, callback.MessageId, "📋 功能菜单:", menuButtons);
                    }
                    break;
            }
        }

        // ---- 查询功能实现 ----

        private async Task QueryMyRequirements(IChatBot bot, ChatCallbackEvent callback)
        {
            using var scope = _serviceProvider.CreateScope();
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();

            var projectId = FindProjectId(scope, callback.ChatId, callback.UserId);
            if (projectId == null)
            {
                await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                    "⚠️ 未找到关联项目，请先绑定", BackButton());
                return;
            }

            var requirements = fsql.Select<Requirement>()
                .Where(r => r.ProjectId == projectId.Value && r.IsConfirmed == true && !r.IsDeleted)
                .OrderByDescending(r => r.Id)
                .Limit(10)
                .ToList();

            if (requirements.Count == 0)
            {
                await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                    "📋 暂无需求记录", BackButton());
                return;
            }

            var statusMap = new Dictionary<RequirementStatus, string>
            {
                { RequirementStatus.Draft, "📝 草稿" }, { RequirementStatus.Todo, "⏳ 待办" },
                { RequirementStatus.InProgress, "🔄 进行中" }, { RequirementStatus.Testing, "🧪 测试中" },
                { RequirementStatus.Done, "✅ 完成" }, { RequirementStatus.Closed, "🔒 关闭" }
            };

            var lines = requirements.Select((r, i) =>
                $"{i + 1}. {statusMap.GetValueOrDefault(r.Status, "❓")} {r.Title}");
            var text = $"📋 <b>最近需求 (共{requirements.Count}条)</b>\n\n" + string.Join("\n", lines);

            await bot.EditMessageAsync(callback.ChatId, callback.MessageId, text, BackButton());
        }

        private async Task QueryProjectProgress(IChatBot bot, ChatCallbackEvent callback)
        {
            using var scope = _serviceProvider.CreateScope();
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();

            var projectId = FindProjectId(scope, callback.ChatId, callback.UserId);
            if (projectId == null)
            {
                await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                    "⚠️ 未找到关联项目，请先绑定", BackButton());
                return;
            }

            var project = fsql.Select<Project>().Where(p => p.Id == projectId.Value).First();
            if (project == null)
            {
                await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                    "⚠️ 关联项目不存在", BackButton());
                return;
            }

            // 统计任务
            var totalTasks = fsql.Select<TaskItem>().Where(t => t.ProjectId == project.Id && !t.IsDeleted).Count();
            var doneTasks = fsql.Select<TaskItem>().Where(t => t.ProjectId == project.Id && !t.IsDeleted
                && t.ExecStatus == TaskItemExecStatus.Success).Count();
            var runningTasks = fsql.Select<TaskItem>().Where(t => t.ProjectId == project.Id && !t.IsDeleted
                && t.ExecStatus == TaskItemExecStatus.Running).Count();
            var pendingTasks = totalTasks - doneTasks - runningTasks;

            // 统计需求
            var totalReqs = fsql.Select<Requirement>().Where(r => r.ProjectId == project.Id && r.IsConfirmed == true && !r.IsDeleted).Count();
            var doneReqs = fsql.Select<Requirement>().Where(r => r.ProjectId == project.Id && r.IsConfirmed == true && !r.IsDeleted
                && r.Status == RequirementStatus.Done).Count();

            var progressPct = totalTasks > 0 ? (doneTasks * 100 / totalTasks) : 0;
            var bar = BuildProgressBar(progressPct);

            var text = $"📊 <b>{project.Name}</b>\n" +
                       $"状态: {project.Status}\n\n" +
                       $"任务进度: {bar} {progressPct}%\n" +
                       $"  总计: {totalTasks} | 完成: {doneTasks} | 进行中: {runningTasks} | 待办: {pendingTasks}\n\n" +
                       $"需求: {doneReqs}/{totalReqs} 已完成";

            await bot.EditMessageAsync(callback.ChatId, callback.MessageId, text, BackButton());
        }

        private async Task QueryRecentUpdates(IChatBot bot, ChatCallbackEvent callback)
        {
            using var scope = _serviceProvider.CreateScope();
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();

            var projectId = FindProjectId(scope, callback.ChatId, callback.UserId);
            if (projectId == null)
            {
                await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                    "⚠️ 未找到关联项目，请先绑定", BackButton());
                return;
            }

            // 最近 7 天完成的任务
            var since = DateTime.Now.AddDays(-7);
            var recentTasks = fsql.Select<TaskItem>()
                .Where(t => t.ProjectId == projectId.Value && !t.IsDeleted
                    && t.CompletedDate != null && t.CompletedDate > since)
                .OrderByDescending(t => t.CompletedDate)
                .Limit(10)
                .ToList();

            if (recentTasks.Count == 0)
            {
                await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                    "🆕 最近 7 天暂无完成的任务", BackButton());
                return;
            }

            var lines = recentTasks.Select((t, i) =>
                $"{i + 1}. ✅ {t.Title}\n   {t.CompletedDate:MM-dd HH:mm} | {t.Assignee ?? "未分配"}");
            var text = $"🆕 <b>最近 7 天完成 ({recentTasks.Count}项)</b>\n\n" + string.Join("\n", lines);

            await bot.EditMessageAsync(callback.ChatId, callback.MessageId, text, BackButton());
        }

        private async Task QueryMyTasks(IChatBot bot, ChatCallbackEvent callback)
        {
            using var scope = _serviceProvider.CreateScope();
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();

            var userBinding = userService.FindByPlatformUserId("Telegram", callback.UserId);
            if (userBinding?.AdminId == null)
            {
                await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                    "⚠️ 无法识别您的系统账号", BackButton());
                return;
            }

            // 通过 AdminId 查找用户名
            var admin = fsql.Select<Admin>().Where(a => a.Id == userBinding.AdminId.Value).First();
            var userName = admin?.Name ?? "";

            var tasks = fsql.Select<TaskItem>()
                .Where(t => !t.IsDeleted && t.ExecStatus != TaskItemExecStatus.Success
                    && (t.Assignee == userName || t.ClaimedBy == userName))
                .OrderBy(t => t.DueDate)
                .Limit(10)
                .ToList();

            if (tasks.Count == 0)
            {
                await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                    "📝 您当前没有待办任务", BackButton());
                return;
            }

            var statusIcons = new Dictionary<TaskItemExecStatus, string>
            {
                { TaskItemExecStatus.Idle, "⏳" }, { TaskItemExecStatus.Running, "🔄" },
                { TaskItemExecStatus.Failed, "❌" }, { TaskItemExecStatus.Timeout, "⏰" },
                { TaskItemExecStatus.Hibernated, "💤" }
            };

            var lines = tasks.Select((t, i) =>
            {
                var icon = statusIcons.GetValueOrDefault(t.ExecStatus, "❓");
                var due = t.DueDate.HasValue ? $" | 截止: {t.DueDate:MM-dd}" : "";
                return $"{i + 1}. {icon} {t.Title}{due}";
            });
            var text = $"📝 <b>我的待办 ({tasks.Count}项)</b>\n\n" + string.Join("\n", lines);

            await bot.EditMessageAsync(callback.ChatId, callback.MessageId, text, BackButton());
        }

        // ---- AI 控制 ----

        private async Task ShowAiControlMenu(IChatBot bot, ChatCallbackEvent callback)
        {
            using var scope = _serviceProvider.CreateScope();
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();

            var groupBinding = groupService.FindByPlatformGroupId(callback.ChatId);
            var aiStatus = groupBinding?.AiEnabled == true ? "🟢 已开启" : "🔴 已关闭";
            var rtStatus = groupBinding?.AiRealtimeEnabled == true ? "⚡ 已开启" : "⏸️ 已关闭";

            var buttons = new List<List<MenuButton>>
            {
                new() { new MenuButton("🟢 开启 AI", "menu:ai_on"), new MenuButton("🔴 关闭 AI", "menu:ai_off") },
                new() { new MenuButton("⚡ 开启实时", "menu:ai_rt_on"), new MenuButton("⏸️ 关闭实时", "menu:ai_rt_off") },
                new() { new MenuButton("◀️ 返回", "menu:back_main") }
            };

            await bot.EditMessageAsync(callback.ChatId, callback.MessageId,
                $"⚙️ <b>AI 分析控制</b>\n\nAI 分析: {aiStatus}\n实时分析: {rtStatus}", buttons);
        }

        private async Task ToggleAi(IChatBot bot, ChatCallbackEvent callback, bool enabled)
        {
            using var scope = _serviceProvider.CreateScope();
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();

            var groupBinding = groupService.FindByPlatformGroupId(callback.ChatId);
            if (groupBinding == null)
            {
                await bot.AnswerCallbackAsync(callback.CallbackId, "当前群组未绑定", true);
                return;
            }

            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            fsql.Update<ChatGroupBinding>()
                .Set(g => g.AiEnabled, enabled)
                .Where(g => g.Id == groupBinding.Id)
                .ExecuteAffrows();

            // 刷新 AI 控制菜单
            groupBinding.AiEnabled = enabled;
            await ShowAiControlMenu(bot, callback);
        }

        private async Task ToggleAiRealtime(IChatBot bot, ChatCallbackEvent callback, bool enabled)
        {
            using var scope = _serviceProvider.CreateScope();
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();

            var groupBinding = groupService.FindByPlatformGroupId(callback.ChatId);
            if (groupBinding == null)
            {
                await bot.AnswerCallbackAsync(callback.CallbackId, "当前群组未绑定", true);
                return;
            }

            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            fsql.Update<ChatGroupBinding>()
                .Set(g => g.AiRealtimeEnabled, enabled)
                .Where(g => g.Id == groupBinding.Id)
                .ExecuteAffrows();

            groupBinding.AiRealtimeEnabled = enabled;
            await ShowAiControlMenu(bot, callback);
        }

        // ---- 辅助方法 ----

        /// <summary>
        /// 查找当前上下文的项目 ID：群组→通过群组绑定，私聊→通过用户绑定
        /// </summary>
        private int? FindProjectId(IServiceScope scope, string chatId, string userId)
        {
            var groupService = scope.ServiceProvider.GetRequiredService<ChatGroupBindingService>();
            var groupBinding = groupService.FindByPlatformGroupId(chatId);
            if (groupBinding != null) return groupBinding.ProjectId;

            // 私聊：通过用户绑定找项目
            var userService = scope.ServiceProvider.GetRequiredService<ChatUserBindingService>();
            var userBinding = userService.FindByPlatformUserId("Telegram", userId);
            if (userBinding != null) return userBinding.ProjectId;

            return null;
        }

        private static List<List<MenuButton>> BackButton() => new()
        {
            new() { new MenuButton("◀️ 返回主菜单", "menu:back_main") }
        };

        private static string BuildProgressBar(long pct)
        {
            var filled = (int)(pct / 10);
            var empty = 10 - filled;
            return new string('█', filled) + new string('░', empty);
        }

        // ---- 菜单构建 ----

        private static List<List<MenuButton>> BuildMenuByRole(ChatUserRole role)
        {
            var buttons = new List<List<MenuButton>>();

            // 所有已绑定用户
            buttons.Add(new()
            {
                new MenuButton("📋 我的需求", "menu:my_requirements"),
                new MenuButton("📊 项目进度", "menu:project_progress")
            });
            buttons.Add(new()
            {
                new MenuButton("🆕 最近更新", "menu:recent_updates"),
                new MenuButton("📊 我的状态", "menu:my_status")
            });

            // 团队成员 + 管理员
            if (role == ChatUserRole.TeamMember || role == ChatUserRole.Admin)
            {
                buttons.Add(new()
                {
                    new MenuButton("📝 我的任务", "menu:my_tasks")
                });
                buttons.Add(new()
                {
                    new MenuButton("➕ 提需求", "menu:new_req_help"),
                    new MenuButton("🤖 AI 需求", "menu:ai_req_help")
                });
            }

            // 管理员
            if (role == ChatUserRole.Admin)
            {
                buttons.Add(new()
                {
                    new MenuButton("⚙️ AI 控制", "menu:ai_control")
                });
            }

            return buttons;
        }
    }
}

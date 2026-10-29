using FreeSql;
using Microsoft.Extensions.Logging;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Ai;
using SB.Framework.Core.Attribute;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// AI 管线执行引擎
    /// 按步骤顺序调用配置的 AiProvider 处理消息
    /// </summary>
    [AutoDependencyInjection]
    public class AiPipelineExecutor
    {
        private readonly IFreeSql _fsql;
        private readonly AiProviderFactory _providerFactory;
        private readonly ChatMessageService _messageService;
        private readonly ChatNotificationService _notificationService;
        private readonly ILogger<AiPipelineExecutor> _logger;

        public AiPipelineExecutor(
            IFreeSql fsql,
            AiProviderFactory providerFactory,
            ChatMessageService messageService,
            ChatNotificationService notificationService,
            ILogger<AiPipelineExecutor> logger)
        {
            _fsql = fsql;
            _providerFactory = providerFactory;
            _messageService = messageService;
            _notificationService = notificationService;
            _logger = logger;
        }

        /// <summary>
        /// 执行实时分析（单条消息）
        /// </summary>
        public async Task ProcessRealtimeMessage(ChatMessage message, ChatGroupBinding groupBinding)
        {
            var pipeline = _fsql.Select<AiPipeline>()
                .Where(p => p.GroupBindingId == groupBinding.Id && p.Enabled && p.RealtimeEnabled)
                .First();
            if (pipeline == null) return;

            var steps = _fsql.Select<AiPipelineStep>()
                .Where(s => s.PipelineId == pipeline.Id && s.Enabled)
                .OrderBy(s => s.OrderNo)
                .ToList();

            await ExecuteSteps(steps, new List<ChatMessage> { message }, groupBinding);
        }

        /// <summary>
        /// 批量分析（后台手动触发 — 整理需求）
        /// </summary>
        public async Task<List<ChatRequirement>> ProcessBatch(int groupBindingId)
        {
            var groupBinding = _fsql.Select<ChatGroupBinding>()
                .Where(g => g.Id == groupBindingId)
                .First();
            if (groupBinding == null) return new();

            var pipeline = _fsql.Select<AiPipeline>()
                .Where(p => p.GroupBindingId == groupBindingId && p.Enabled)
                .First();

            // 获取未处理的消息
            var messages = _messageService.GetUnprocessedMessages(groupBindingId);
            if (messages.Count == 0) return new();

            List<ChatRequirement> result;

            if (pipeline != null)
            {
                var steps = _fsql.Select<AiPipelineStep>()
                    .Where(s => s.PipelineId == pipeline.Id && s.Enabled)
                    .OrderBy(s => s.OrderNo)
                    .ToList();

                result = await ExecuteSteps(steps, messages, groupBinding);
            }
            else
            {
                // 没有管线配置，直接用默认需求提取
                result = await ExtractRequirements(messages, groupBinding, null);
            }

            // 通知群组
            if (result.Count > 0)
            {
                _ = _notificationService.NotifyNewRequirementsExtracted(groupBinding.ProjectId, result.Count);
            }

            return result;
        }

        /// <summary>
        /// 分析指定的消息列表（选中消息 AI 分析）
        /// </summary>
        public async Task<List<ChatRequirement>> ProcessSelectedMessages(List<ChatMessage> messages, ChatGroupBinding groupBinding)
        {
            if (messages.Count == 0) return new();

            var pipeline = _fsql.Select<AiPipeline>()
                .Where(p => p.GroupBindingId == groupBinding.Id && p.Enabled)
                .First();

            List<ChatRequirement> result;

            if (pipeline != null)
            {
                var steps = _fsql.Select<AiPipelineStep>()
                    .Where(s => s.PipelineId == pipeline.Id && s.Enabled)
                    .OrderBy(s => s.OrderNo)
                    .ToList();

                result = await ExecuteSteps(steps, messages, groupBinding);
            }
            else
            {
                result = await ExtractRequirements(messages, groupBinding, null);
            }

            return result;
        }

        // ---- 管线步骤执行 ----

        private async Task<List<ChatRequirement>> ExecuteSteps(
            List<AiPipelineStep> steps, List<ChatMessage> messages, ChatGroupBinding groupBinding)
        {
            var extractedRequirements = new List<ChatRequirement>();
            var filteredMessages = messages;

            foreach (var step in steps)
            {
                var provider = _fsql.Select<AiProvider>().Where(p => p.Id == step.AiProviderId).First();
                if (provider == null)
                {
                    _logger.LogWarning("管线步骤 {StepId} 的 AiProvider {ProviderId} 不存在，跳过",
                        step.Id, step.AiProviderId);
                    continue;
                }

                var adapter = _providerFactory.GetAdapter(provider.ProviderType);
                if (adapter == null)
                {
                    _logger.LogWarning("找不到 ProviderType={ProviderType} 的适配器，跳过", provider.ProviderType);
                    continue;
                }

                try
                {
                    switch (step.StepType)
                    {
                        case AiPipelineStepType.Filter:
                            filteredMessages = await ExecuteFilter(adapter, provider, filteredMessages);
                            break;

                        case AiPipelineStepType.RequirementExtract:
                            var reqs = await ExtractRequirements(filteredMessages, groupBinding, adapter, provider);
                            extractedRequirements.AddRange(reqs);
                            break;

                        case AiPipelineStepType.ImageRecognition:
                            await ExecuteImageRecognition(adapter, provider, filteredMessages);
                            break;

                        case AiPipelineStepType.AudioTranscription:
                            await ExecuteAudioTranscription(adapter, provider, filteredMessages);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "管线步骤 {StepType} 执行失败", step.StepType);
                }
            }

            // 标记消息为已处理
            var messageIds = messages.Select(m => m.Id).ToList();
            _messageService.MarkAsProcessed(messageIds);

            return extractedRequirements;
        }

        // ---- 废话过滤 ----

        private async Task<List<ChatMessage>> ExecuteFilter(
            IAiProviderAdapter adapter, AiProvider provider, List<ChatMessage> messages)
        {
            if (messages.Count == 0) return messages;

            var messagesText = string.Join("\n", messages.Select((m, i) =>
                $"[{i}] {m.SenderName}: {m.Content}"));

            var prompt = $"""
                你是一个消息过滤助手。请分析以下群聊消息，判断每条消息是否包含有价值的业务信息（需求、Bug 反馈、功能建议、问题咨询等）。
                过滤掉纯闲聊、问候、表情回复等无业务价值的内容。

                消息列表：
                {messagesText}

                请返回有价值消息的编号列表，格式为 JSON 数组，例如：[0, 2, 5]
                只返回 JSON 数组，不要其他内容。
                """;

            var result = await adapter.GenerateAsync(provider, prompt);

            try
            {
                var indices = JsonSerializer.Deserialize<List<int>>(result.Trim());
                if (indices != null)
                {
                    return indices.Where(i => i >= 0 && i < messages.Count)
                        .Select(i => messages[i]).ToList();
                }
            }
            catch
            {
                _logger.LogWarning("废话过滤结果解析失败，保留所有消息: {Result}", result);
            }

            return messages;
        }

        // ---- 需求提取 ----

        private async Task<List<ChatRequirement>> ExtractRequirements(
            List<ChatMessage> messages, ChatGroupBinding groupBinding,
            IAiProviderAdapter? adapter, AiProvider? provider = null)
        {
            if (messages.Count == 0) return new();

            // 如果没有配置 AI，使用默认提供商
            if (adapter == null || provider == null)
            {
                var defaultProvider = _fsql.Select<AiProvider>()
                    .Where(p => p.IsDefault && p.Status == AiProviderStatus.Active)
                    .First();
                if (defaultProvider == null)
                {
                    _logger.LogWarning("无可用 AI 提供商，跳过需求提取");
                    return new();
                }
                adapter = _providerFactory.GetAdapter(defaultProvider.ProviderType);
                provider = defaultProvider;
                if (adapter == null) return new();
            }

            // 每条消息带 ID，让 AI 返回每条需求关联的消息 ID
            var messagesText = string.Join("\n", messages.Select(m =>
                $"[#{m.Id}] {m.SenderName} ({m.CreatedTime:MM-dd HH:mm}): {m.Content}"));

            var prompt = "你是一个需求分析助手。请分析以下客户群聊消息，提取出明确的功能需求或改进建议。\n" +
                "每条消息前有 [#ID] 标记。\n\n" +
                "聊天记录：\n" + messagesText + "\n\n" +
                "请提取需求并以 JSON 数组格式返回，每个需求包含：\n" +
                "- title: 简洁标题\n" +
                "- description: 详细描述\n" +
                "- sourceIds: 该需求来源的消息 ID 数组（一个需求可能来自多条消息）\n\n" +
                "示例: [{\"title\": \"标题\", \"description\": \"描述\", \"sourceIds\": [1, 3, 5]}]\n\n" +
                "注意：只提取明确的需求，忽略闲聊。合并重复需求。没有需求返回 []。只返回 JSON。";

            var result = await adapter.GenerateAsync(provider, prompt);
            var requirements = new List<ChatRequirement>();

            try
            {
                var extracted = JsonSerializer.Deserialize<List<ExtractedRequirement>>(result.Trim(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (extracted == null) return requirements;

                foreach (var req in extracted)
                {
                    // 每条需求关联各自的来源消息 ID
                    var sourceIds = req.SourceIds?.Count > 0
                        ? JsonSerializer.Serialize(req.SourceIds)
                        : JsonSerializer.Serialize(messages.Select(m => m.Id).ToList());

                    var chatReq = new ChatRequirement
                    {
                        GroupBindingId = groupBinding.Id,
                        ProjectId = groupBinding.ProjectId,
                        Title = req.Title ?? "未命名需求",
                        Description = req.Description,
                        SourceMessageIds = sourceIds,
                        Status = ChatRequirementStatus.Draft,
                        CreatedTime = DateTime.Now
                    };
                    _fsql.Insert(chatReq).ExecuteInserted();
                    chatReq = _fsql.Select<ChatRequirement>()
                        .OrderByDescending(r => r.Id).First();
                    if (chatReq != null) requirements.Add(chatReq);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "需求提取结果解析失败: {Result}", result);
            }

            return requirements;
        }

        // ---- 图像识别（将图片内容附加到消息文本） ----

        private async Task ExecuteImageRecognition(
            IAiProviderAdapter adapter, AiProvider provider, List<ChatMessage> messages)
        {
            var imageMessages = messages.Where(m => m.MessageType == ChatMessageType.Image).ToList();
            if (imageMessages.Count == 0) return;

            foreach (var msg in imageMessages)
            {
                var media = _fsql.Select<ChatMedia>()
                    .Where(m => m.MessageId == msg.Id && m.MediaType == ChatMessageType.Image)
                    .First();
                if (media?.LocalPath == null) continue;

                var prompt = $"请描述这张图片的内容，特别关注其中是否包含功能需求、UI 设计、流程图等业务相关信息。图片路径: {media.LocalPath}";

                try
                {
                    var description = await adapter.GenerateAsync(provider, prompt);
                    // 将识别结果追加到消息内容
                    msg.Content = $"{msg.Content}\n[图片识别]: {description}";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "图像识别失败: MessageId={MessageId}", msg.Id);
                }
            }
        }

        // ---- 音频转文字 ----

        private async Task ExecuteAudioTranscription(
            IAiProviderAdapter adapter, AiProvider provider, List<ChatMessage> messages)
        {
            var audioMessages = messages.Where(m => m.MessageType == ChatMessageType.Audio).ToList();
            if (audioMessages.Count == 0) return;

            foreach (var msg in audioMessages)
            {
                var media = _fsql.Select<ChatMedia>()
                    .Where(m => m.MessageId == msg.Id && m.MediaType == ChatMessageType.Audio)
                    .First();
                if (media?.LocalPath == null) continue;

                var prompt = $"请将以下音频文件转录为文字。音频路径: {media.LocalPath}";

                try
                {
                    var transcription = await adapter.GenerateAsync(provider, prompt);
                    msg.Content = $"{msg.Content}\n[语音转文字]: {transcription}";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "音频转文字失败: MessageId={MessageId}", msg.Id);
                }
            }
        }

        private class ExtractedRequirement
        {
            public string? Title { get; set; }
            public string? Description { get; set; }
            public List<int>? SourceIds { get; set; }
        }
    }
}

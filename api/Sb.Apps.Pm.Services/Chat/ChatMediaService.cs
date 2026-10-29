using FreeSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SB.Chat;
using SB.Chat.Models;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// 聊天媒体文件下载与存储
    /// </summary>
    [AutoDependencyInjection]
    public class ChatMediaService : BaseCrudService<ChatMedia>
    {
        private readonly IFreeSql _fsql;
        private readonly string _mediaStorageRoot;

        public ChatMediaService(IFreeSql fsql, ILogger<ChatMediaService> logger, IConfiguration configuration) : base(fsql, logger)
        {
            _fsql = fsql;
            _mediaStorageRoot = configuration.GetValue<string>("UploadPath") ?? "storage/uploads/";
            _mediaStorageRoot = Path.Combine(_mediaStorageRoot, "chat-media");
        }

        /// <summary>
        /// 下载并保存消息中的媒体附件
        /// </summary>
        public virtual async Task<List<ChatMedia>> DownloadAndSave(int messageId, List<ChatMediaInfo> mediaList, IChatBot bot)
        {
            var savedMedia = new List<ChatMedia>();

            foreach (var media in mediaList)
            {
                try
                {
                    var chatMedia = await DownloadSingleMedia(messageId, media, bot);
                    if (chatMedia != null)
                        savedMedia.Add(chatMedia);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "下载媒体文件失败: FileId={FileId}", media.FileId);
                }
            }

            return savedMedia;
        }

        /// <summary>
        /// 通过个人账号下载并保存媒体附件
        /// </summary>
        public virtual async Task<List<ChatMedia>> DownloadAndSave(int messageId, List<ChatMediaInfo> mediaList, IChatClient client)
        {
            var savedMedia = new List<ChatMedia>();

            foreach (var media in mediaList)
            {
                try
                {
                    var chatMedia = await DownloadSingleMedia(messageId, media, client);
                    if (chatMedia != null)
                        savedMedia.Add(chatMedia);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "下载媒体文件失败: FileId={FileId}", media.FileId);
                }
            }

            return savedMedia;
        }

        /// <summary>
        /// 获取消息的所有媒体文件
        /// </summary>
        public virtual List<ChatMedia> GetByMessageId(int messageId)
        {
            return Select.Where(m => m.MessageId == messageId).ToList();
        }

        private async Task<ChatMedia?> DownloadSingleMedia(int messageId, ChatMediaInfo media, IChatBot bot)
        {
            // 生成本地存储路径
            var dateDir = DateTime.Now.ToString("yyyy/MM/dd");
            var ext = GetExtension(media.MimeType, media.FileName);
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var relativePath = $"{dateDir}/{fileName}";
            var fullPath = Path.Combine(_mediaStorageRoot, relativePath);

            // 确保目录存在
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 下载文件
            using var stream = await bot.DownloadMediaAsync(media.FileId);
            await using var fileStream = File.Create(fullPath);
            await stream.CopyToAsync(fileStream);

            // 保存记录
            var chatMedia = new ChatMedia
            {
                MessageId = messageId,
                MediaType = MapMediaType(media.MediaType),
                PlatformFileId = media.FileId,
                LocalPath = relativePath,
                FileName = media.FileName ?? fileName,
                FileSize = media.FileSize ?? fileStream.Length,
                MimeType = media.MimeType ?? "application/octet-stream",
                CreatedTime = DateTime.Now
            };
            chatMedia = this.Insert(chatMedia);
            return chatMedia;
        }

        private async Task<ChatMedia?> DownloadSingleMedia(int messageId, ChatMediaInfo media, IChatClient client)
        {
            var dateDir = DateTime.Now.ToString("yyyy/MM/dd");
            var ext = GetExtension(media.MimeType, media.FileName);
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var relativePath = $"{dateDir}/{fileName}";
            var fullPath = Path.Combine(_mediaStorageRoot, relativePath);

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var stream = await client.DownloadMediaAsync(media.FileId);
            await using var fileStream = File.Create(fullPath);
            await stream.CopyToAsync(fileStream);

            var chatMedia = new ChatMedia
            {
                MessageId = messageId,
                MediaType = MapMediaType(media.MediaType),
                PlatformFileId = media.FileId,
                LocalPath = relativePath,
                FileName = media.FileName ?? fileName,
                FileSize = media.FileSize ?? fileStream.Length,
                MimeType = media.MimeType ?? "application/octet-stream",
                CreatedTime = DateTime.Now
            };
            chatMedia = this.Insert(chatMedia);
            return chatMedia;
        }

        private static string GetExtension(string? mimeType, string? fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                var ext = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(ext)) return ext;
            }

            return mimeType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "audio/ogg" => ".ogg",
                "audio/mpeg" => ".mp3",
                "video/mp4" => ".mp4",
                "video/webm" => ".webm",
                "application/x-tgsticker" => ".tgs",
                _ => ""
            };
        }

        private static Models.ChatMessageType MapMediaType(SB.Chat.Models.ChatMessageType type) => type switch
        {
            SB.Chat.Models.ChatMessageType.Image => Models.ChatMessageType.Image,
            SB.Chat.Models.ChatMessageType.Audio => Models.ChatMessageType.Audio,
            SB.Chat.Models.ChatMessageType.Video => Models.ChatMessageType.Video,
            SB.Chat.Models.ChatMessageType.Sticker => Models.ChatMessageType.Sticker,
            _ => Models.ChatMessageType.File
        };
    }
}

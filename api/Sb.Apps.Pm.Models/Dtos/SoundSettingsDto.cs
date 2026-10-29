using SB.Framework.Models;
namespace Sb.Apps.Pm.Models.Dtos
{
    /// <summary>
    /// 声音设置 DTO
    /// </summary>
    public class SoundSettingsDto
    {
        public bool NewMessageEnabled { get; set; } = true;
        public string NewMessageSound { get; set; } = "ding.mp3";
        public int NewMessageVolume { get; set; } = 80;
        public bool SystemNotificationEnabled { get; set; } = true;
        public string SystemNotificationSound { get; set; } = "dingdong.mp3";
        public int SystemNotificationVolume { get; set; } = 80;
    }

    /// <summary>
    /// 可用提示音列表项
    /// </summary>
    public class SoundOptionDto
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }
}

namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

public class TrustedBypassConfig
{
    public const string UsernameVariable = "{username}";
    public const string ChatNameVariable = "{chat_name}";
    public const int MinAnnouncementTtlSeconds = 0;
    // 3500 = 4096 (Telegram wire limit) − ~600 chars headroom for
    // {username}/{chat_name} expansion and worst-case HTML encoding.
    public const int MaxAnnouncementTemplateLength = 3500;
    internal const int DefaultAnnouncementTtlSeconds = 30;
    internal const string DefaultAnnouncementMessageAdmin =
        UsernameVariable + " welcomed automatically — admin.";
    internal const string DefaultAnnouncementMessageTrusted =
        UsernameVariable + " welcomed automatically — trusted from other groups.";

    public bool Enabled { get; set; } = false;
    public string AnnouncementMessageAdmin { get; set; } = DefaultAnnouncementMessageAdmin;
    public string AnnouncementMessageTrusted { get; set; } = DefaultAnnouncementMessageTrusted;
    public int AnnouncementTtlSeconds { get; set; } = DefaultAnnouncementTtlSeconds;
}

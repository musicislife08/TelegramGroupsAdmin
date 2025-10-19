namespace TelegramGroupsAdmin.Configuration;

public sealed class MessageHistoryOptions
{
    public bool Enabled { get; set; } = true; // Enable/disable message history tracking
    public int RetentionHours { get; set; } = 720; // 30 days
    public int CleanupIntervalMinutes { get; set; } = 1440; // 24 hours (once per day)
    public string ImageStoragePath { get; set; } = "/data/images";
    public int ThumbnailSize { get; set; } = 200; // 200x200 pixels
}

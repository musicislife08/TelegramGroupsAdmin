namespace TelegramGroupsAdmin.Configuration;

public sealed record MessageHistoryOptions
{
    public bool Enabled { get; init; } = true; // Enable/disable message history tracking
    public int RetentionHours { get; init; } = 720; // 30 days
    public int CleanupIntervalMinutes { get; init; } = 1440; // 24 hours (once per day)
    public string ImageStoragePath { get; init; } = "/data/images";
    public int ThumbnailSize { get; init; } = 200; // 200x200 pixels
}

namespace TelegramGroupsAdmin.Configuration;

public class MessageHistoryOptions
{
    public string DatabasePath { get; set; } = "/data/message_history.db";
    public int RetentionHours { get; set; } = 24;
    public int CleanupIntervalMinutes { get; set; } = 5;
}

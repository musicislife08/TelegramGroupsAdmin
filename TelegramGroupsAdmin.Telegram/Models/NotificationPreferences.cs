namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for user notification preferences
/// Deserialized from notification_preferences table
/// Changed from record to class for Blazor two-way binding support
/// </summary>
public class NotificationPreferences
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public bool TelegramDmEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public NotificationChannelConfigs ChannelConfigs { get; set; } = new();
    public NotificationEventFilters EventFilters { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of feature availability check
/// </summary>
public record WebBotFeatureAvailability(
    bool IsAvailable,
    long? BotUserId,
    string? LinkedUsername,
    string? UnavailableReason);

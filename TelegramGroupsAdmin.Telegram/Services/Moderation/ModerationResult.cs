namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

/// <summary>
/// Result of a moderation action.
/// </summary>
public record ModerationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool MessageDeleted { get; init; }
    public bool TrustRemoved { get; init; }
    public bool TrustRestored { get; init; }
    public int ChatsAffected { get; init; }
    public int WarningCount { get; init; }
    public bool AutoBanTriggered { get; init; }

    public static ModerationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static ModerationResult SystemAccountBlocked() =>
        new() { Success = false, ErrorMessage = "Cannot perform moderation actions on Telegram system accounts (channel posts, anonymous admins, etc.)" };
}

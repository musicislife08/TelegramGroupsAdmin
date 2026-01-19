namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

/// <summary>
/// Result of a moderation action.
/// </summary>
public class ModerationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool MessageDeleted { get; set; }
    public bool TrustRemoved { get; set; }
    public bool TrustRestored { get; set; }
    public int ChatsAffected { get; set; }
    public int WarningCount { get; set; }
    public bool AutoBanTriggered { get; set; }

    public static ModerationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static ModerationResult SystemAccountBlocked() =>
        new() { Success = false, ErrorMessage = "Cannot perform moderation actions on Telegram system accounts (channel posts, anonymous admins, etc.)" };
}

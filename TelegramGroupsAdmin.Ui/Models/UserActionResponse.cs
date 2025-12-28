namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for user action operations (trust, ban, unban, etc.).
/// </summary>
public record UserActionResponse(
    bool Success,
    string? Error = null,
    int? ChatsAffected = null,
    bool? TrustRestored = null,
    DateTimeOffset? BannedUntil = null
) : ApiResponse(Success, Error);

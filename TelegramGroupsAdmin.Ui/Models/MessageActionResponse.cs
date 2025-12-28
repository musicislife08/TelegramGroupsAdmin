namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for message action operations (delete, spam, ham, etc.).
/// </summary>
public record MessageActionResponse(
    bool Success,
    string? Error = null,
    bool? MessageDeleted = null,
    int? ChatsAffected = null,
    bool? TrustRestored = null
) : ApiResponse(Success, Error);

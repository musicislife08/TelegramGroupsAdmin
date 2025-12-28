using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the UserDetailDialog.
/// Bundles all data needed to render the dialog in a single HTTP call.
/// Reduces network round-trips for WASM clients.
/// </summary>
public record UserDetailDialogResponse(
    TelegramUserDetail? UserDetail,
    Dictionary<string, TagColor> TagColors
);

/// <summary>
/// Request to add a note to a user.
/// </summary>
public record AddNoteRequest(string NoteText);

/// <summary>
/// Request to add tags to a user.
/// </summary>
public record AddTagsRequest(List<string> TagNames);

/// <summary>
/// Request for temporary ban.
/// </summary>
public record UserTempBanRequest(TimeSpan Duration, string? Reason);

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

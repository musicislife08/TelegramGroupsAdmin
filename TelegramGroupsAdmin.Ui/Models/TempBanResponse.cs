namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for temporary ban operations.
/// </summary>
public record TempBanResponse(
    bool Success,
    string? Error = null,
    DateTimeOffset? BannedUntil = null
) : ApiResponse(Success, Error);

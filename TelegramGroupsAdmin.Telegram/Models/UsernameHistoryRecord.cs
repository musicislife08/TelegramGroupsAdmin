namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for a username history entry.
/// Each record captures the previous profile values at the moment a change was detected.
/// </summary>
public record UsernameHistoryRecord(
    long Id,
    long UserId,
    string? Username,
    string? FirstName,
    string? LastName,
    DateTimeOffset RecordedAt);

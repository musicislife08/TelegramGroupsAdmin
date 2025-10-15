namespace TelegramGroupsAdmin.SpamDetection.Models;

/// <summary>
/// Stop word domain model (public API for SpamDetection library)
/// </summary>
public record StopWord(
    long Id,
    string Word,
    bool Enabled,
    DateTimeOffset AddedDate,
    string? AddedBy,
    string? Notes
);

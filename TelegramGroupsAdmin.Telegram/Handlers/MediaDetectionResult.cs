using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Result of media detection (pure metadata extraction from Telegram message)
/// </summary>
public record MediaDetectionResult(
    MediaType MediaType,
    string FileId,
    long FileSize,
    string? FileName,
    string? MimeType,
    int? Duration
);

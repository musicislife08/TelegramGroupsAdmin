using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Result of complete media processing (detection + download coordination)
/// </summary>
public record MediaProcessingResult(
    MediaType MediaType,
    string? FileId, // Null for oversized files (prevents download attempts)
    long FileSize,
    string? FileName,
    string? MimeType,
    int? Duration,
    string? LocalPath // Null for Documents (metadata-only)
);

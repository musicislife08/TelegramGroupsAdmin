namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Result of scannable file detection (pure metadata extraction)
/// </summary>
public record FileDetectionResult(
    string FileId,
    long FileSize,
    string? FileName,
    string? ContentType
);

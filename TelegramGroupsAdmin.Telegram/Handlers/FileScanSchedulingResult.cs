namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Result of file scan scheduling (detection + background job scheduling)
/// </summary>
public record FileScanSchedulingResult(
    string FileId,
    long FileSize,
    string? FileName,
    string? ContentType,
    bool Scheduled,
    string? SkipReason = null
);

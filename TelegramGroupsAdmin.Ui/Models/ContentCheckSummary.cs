namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Summary of content check for the spam badge (projection of DetectionResultRecord).
/// </summary>
public record ContentCheckSummary(
    bool IsSpam,
    int Confidence,
    string? Reason,
    DateTimeOffset CheckedAt
);

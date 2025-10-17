using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for impersonation alerts
/// </summary>
public record ImpersonationAlertRecord
{
    public int Id { get; init; }
    public long SuspectedUserId { get; init; }
    public long TargetUserId { get; init; }
    public long ChatId { get; init; }

    // Composite scoring
    public int TotalScore { get; init; }
    public ImpersonationRiskLevel RiskLevel { get; init; }

    // Match details
    public bool NameMatch { get; init; }
    public bool PhotoMatch { get; init; }
    public double? PhotoSimilarityScore { get; init; }

    // Review workflow
    public DateTimeOffset DetectedAt { get; init; }
    public bool AutoBanned { get; init; }
    public string? ReviewedByUserId { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public ImpersonationVerdict? Verdict { get; init; }

    // Denormalized for display (joined data)
    public string? SuspectedUserName { get; init; }
    public string? SuspectedFirstName { get; init; }
    public string? SuspectedPhotoPath { get; init; }

    public string? TargetUserName { get; init; }
    public string? TargetFirstName { get; init; }
    public string? TargetPhotoPath { get; init; }

    public string? ChatName { get; init; }
    public string? ReviewedByEmail { get; init; }
}

using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// UI model for impersonation alerts
/// </summary>
public record ImpersonationAlertRecord
{
    public long Id { get; init; }
    public required UserIdentity SuspectedUser { get; init; }
    public required UserIdentity TargetUser { get; init; }
    public required ChatIdentity Chat { get; init; }

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

    // Photo paths (not part of identity objects — specific to impersonation display)
    public string? SuspectedPhotoPath { get; init; }
    public string? TargetPhotoPath { get; init; }

    public string? ReviewedByEmail { get; init; }
}

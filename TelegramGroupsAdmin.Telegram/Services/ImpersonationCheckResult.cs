using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of impersonation detection check
/// </summary>
public record ImpersonationCheckResult
{
    public bool ShouldTakeAction => TotalScore >= 50;
    public bool ShouldAutoBan => TotalScore >= 100;

    public int TotalScore { get; init; }
    public ImpersonationRiskLevel RiskLevel { get; init; }

    public long SuspectedUserId { get; init; }
    public long TargetUserId { get; init; }
    public long ChatId { get; init; }

    public bool NameMatch { get; init; }
    public bool PhotoMatch { get; init; }
    public double? PhotoSimilarityScore { get; init; }
}

using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Result of a profile scan containing all extracted data, computed score, and outcome.
/// </summary>
public record ProfileScanResult(
    long TelegramUserId,
    string? Bio,
    long? PersonalChannelId,
    string? PersonalChannelTitle,
    string? PersonalChannelAbout,
    bool HasPinnedStories,
    string? PinnedStoryCaptions,
    bool IsScam,
    bool IsFake,
    bool IsVerified,
    decimal Score,
    ProfileScanOutcome Outcome,
    string? AiReason,
    string[]? AiSignalsDetected,
    bool ContainsNudity = false,
    bool ExplicitDisplayText = false,
    string? SkipReason = null,
    bool PromotionalDisplayText = false,
    ProfileScanSource Source = ProfileScanSource.FullScan)
{
    /// <summary>
    /// This result with its score-derived fields taken from <paramref name="scoring"/>: the one mapping
    /// used by the full scan and the name-only scan.
    /// </summary>
    public ProfileScanResult WithScoring(ScoringResult scoring, ProfileScanSource source) => this with
    {
        Score = scoring.Score,
        Outcome = scoring.Outcome,
        AiReason = scoring.AiReason,
        AiSignalsDetected = scoring.AiSignals,
        ContainsNudity = scoring.ContainsNudity,
        ExplicitDisplayText = scoring.ExplicitDisplayText,
        PromotionalDisplayText = scoring.PromotionalDisplayText,
        Source = source
    };
}

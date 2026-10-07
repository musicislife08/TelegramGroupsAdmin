using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>Result from the full two-layer scoring pipeline, or from the name-only scan.</summary>
public record ScoringResult(
    decimal Score,
    ProfileScanOutcome Outcome,
    decimal RuleScore,
    decimal AiScore,
    string? AiReason,
    string[]? AiSignals,
    bool ContainsNudity = false,
    bool ExplicitDisplayText = false,
    bool PromotionalDisplayText = false)
{
    /// <summary>
    /// The one outcome rule for every scan: at or above <paramref name="banThreshold"/> banned, else at
    /// or above <paramref name="notifyThreshold"/> held for review, else clean.
    /// </summary>
    public static ProfileScanOutcome OutcomeFor(decimal score, decimal banThreshold, decimal notifyThreshold) =>
        score >= banThreshold
            ? ProfileScanOutcome.Banned
            : score >= notifyThreshold
                ? ProfileScanOutcome.HeldForReview
                : ProfileScanOutcome.Clean;
}

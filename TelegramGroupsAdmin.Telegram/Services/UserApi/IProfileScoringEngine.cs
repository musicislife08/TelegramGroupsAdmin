using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Two-layer scoring engine for profile risk assessment.
/// Layer 1: Cheap rule-based pre-filters (instant, run first).
/// Layer 2: AI vision analysis (expensive, skipped if Layer 1 already hits ban threshold).
/// </summary>
public interface IProfileScoringEngine
{
    Task<ScoringResult> ScoreAsync(
        ProfileData profile,
        IReadOnlyList<ImageInput> images,
        string? imageLabels,
        decimal banThreshold,
        decimal notifyThreshold,
        CancellationToken cancellationToken);

    /// <summary>
    /// Name-only scan: scores the user's display name and username with the full scan's system
    /// prompt when the profile could not be read. Outcome: below <paramref name="notifyThreshold"/>
    /// clean, below <paramref name="nameOnlyBanThreshold"/> held for review, otherwise banned.
    /// </summary>
    /// <returns>The result, or null when the AI feature is unavailable or the call fails (logged as a warning).</returns>
    Task<ScoringResult?> ScoreNameOnlyAsync(
        UserIdentity user,
        decimal nameOnlyBanThreshold,
        decimal notifyThreshold,
        CancellationToken cancellationToken);
}

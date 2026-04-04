using TelegramGroupsAdmin.Core.Services.AI;

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
}

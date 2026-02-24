namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Outcome of a profile scan — what action was taken.
/// </summary>
public enum ProfileScanOutcome
{
    /// <summary>Score below notify threshold — continue welcome flow normally</summary>
    Clean,

    /// <summary>Score between notify and ban thresholds — report created, held for admin review</summary>
    HeldForReview,

    /// <summary>Score above ban threshold — user auto-banned</summary>
    Banned
}

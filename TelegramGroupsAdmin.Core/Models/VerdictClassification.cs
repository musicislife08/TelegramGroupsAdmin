namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// The single stored decision for a verdict event: spam/ham × explicit/implicit/untrained.
/// Stored as int by the Data layer; values are explicit and must never be renumbered.
/// </summary>
public enum VerdictClassification
{
    ExplicitSpam = 0,
    ExplicitHam = 1,
    ImplicitSpam = 2,
    ImplicitHam = 3,
    UntrainedSpam = 4,
    UntrainedHam = 5,
    /// <summary>View-only: the message has no verdict event. Never stored on a row.</summary>
    Unscanned = 6
}

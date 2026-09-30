namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Source of a training sample.
/// </summary>
public enum TrainingSampleSource
{
    /// <summary>
    /// explicit = ExplicitSpam/ExplicitHam verdict (admin decision overrides auto-detection).
    /// High quality, manually verified.
    /// </summary>
    Explicit,

    /// <summary>
    /// implicit = ImplicitSpam/ImplicitHam/Unscanned verdict (auto-detection, never
    /// manually corrected).
    /// </summary>
    Implicit
}

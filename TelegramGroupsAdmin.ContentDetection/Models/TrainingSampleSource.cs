namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Source of a training sample.
/// </summary>
public enum TrainingSampleSource
{
    /// <summary>
    /// Explicit label from training_labels table (admin decision overrides auto-detection).
    /// High quality, manually verified.
    /// </summary>
    Explicit,

    /// <summary>
    /// Implicit sample from auto-detection (high-confidence spam or quality ham).
    /// Never manually corrected.
    /// </summary>
    Implicit
}

using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Represents a single text sample for ML spam classifier training.
/// Encapsulates both explicit labels (admin decisions) and implicit samples (auto-detected).
/// </summary>
public record TrainingSample
{
    /// <summary>
    /// Message text content (translated if available, otherwise original).
    /// This is the text used for TF-IDF feature extraction.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Training label: Spam or Ham.
    /// </summary>
    public required TrainingLabel Label { get; init; }

    /// <summary>
    /// Source of this training sample: explicit admin label or implicit auto-detection.
    /// </summary>
    public required TrainingSampleSource Source { get; init; }

    /// <summary>
    /// Original message ID (for traceability and debugging).
    /// </summary>
    public required long MessageId { get; init; }

    /// <summary>
    /// User ID who labeled this sample (null for implicit/auto-detected samples).
    /// </summary>
    public long? LabeledByUserId { get; init; }

    /// <summary>
    /// When this sample was labeled (null for implicit samples).
    /// </summary>
    public DateTimeOffset? LabeledAt { get; init; }
}

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

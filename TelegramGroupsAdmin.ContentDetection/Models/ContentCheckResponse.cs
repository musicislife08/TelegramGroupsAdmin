using TelegramGroupsAdmin.ContentDetection.Constants;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Response from individual content check, based on tg-spam's Response model
/// </summary>
public record ContentCheckResponse
{
    /// <summary>
    /// Name of the check that produced this response
    /// </summary>
    public required CheckName CheckName { get; init; }

    /// <summary>
    /// Classification result (Clean, Spam, or Review)
    /// </summary>
    public CheckResultType Result { get; init; } = CheckResultType.Clean;

    /// <summary>
    /// Human-readable explanation of the decision
    /// </summary>
    public string Details { get; init; } = string.Empty;

    /// <summary>
    /// Confidence score (0-100) for this check's decision
    /// </summary>
    public int Confidence { get; init; } = 0;

    /// <summary>
    /// Error that occurred during check (if any)
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// Additional message IDs to delete (for duplicate detection)
    /// </summary>
    public List<int> ExtraDeleteIds { get; init; } = [];

    /// <summary>
    /// Processing time for this check in milliseconds
    /// </summary>
    public long ProcessingTimeMs { get; init; } = 0;
}

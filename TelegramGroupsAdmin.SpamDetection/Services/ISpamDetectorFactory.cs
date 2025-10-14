using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Services;

/// <summary>
/// Factory for creating spam detectors and orchestrating spam detection checks
/// </summary>
public interface ISpamDetectorFactory
{
    /// <summary>
    /// Run all applicable spam checks on a message and return aggregated results
    /// </summary>
    Task<SpamDetectionResult> CheckMessageAsync(SpamCheckRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run only non-OpenAI checks to determine if message should be vetoed by OpenAI
    /// </summary>
    Task<SpamDetectionResult> CheckMessageWithoutOpenAIAsync(SpamCheckRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated spam detection result from multiple checks
/// </summary>
public record SpamDetectionResult
{
    /// <summary>
    /// Overall spam determination
    /// </summary>
    public bool IsSpam { get; init; }

    /// <summary>
    /// Highest confidence score from all checks
    /// </summary>
    public int MaxConfidence { get; init; }

    /// <summary>
    /// Average confidence from spam-flagging checks
    /// </summary>
    public int AvgConfidence { get; init; }

    /// <summary>
    /// Number of checks that flagged as spam
    /// </summary>
    public int SpamFlags { get; init; }

    /// <summary>
    /// Phase 2.6: Net confidence from weighted voting
    /// Sum(spam confidences) - Sum(ham confidences)
    /// </summary>
    public int NetConfidence { get; init; }

    /// <summary>
    /// Results from individual checks
    /// </summary>
    public List<SpamCheckResponse> CheckResults { get; init; } = [];

    /// <summary>
    /// Primary reason for spam classification
    /// </summary>
    public string PrimaryReason { get; init; } = string.Empty;

    /// <summary>
    /// Recommended action based on confidence thresholds
    /// </summary>
    public SpamAction RecommendedAction { get; init; }

    /// <summary>
    /// Whether the message should be submitted to OpenAI for veto
    /// </summary>
    public bool ShouldVeto { get; init; }
}

/// <summary>
/// Recommended action for spam messages
/// </summary>

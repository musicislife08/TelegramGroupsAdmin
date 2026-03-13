namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Details about a specific message that was flagged as spam but vetoed by OpenAI.
/// Used for manual inspection and algorithm tuning.
/// </summary>
public record VetoedMessage
{
    /// <summary>
    /// ID of the message that was vetoed
    /// </summary>
    public int MessageId { get; init; }

    /// <summary>
    /// When the detection occurred
    /// </summary>
    public DateTimeOffset DetectedAt { get; init; }

    /// <summary>
    /// Truncated message text for display (first 100 characters)
    /// </summary>
    public string? MessagePreview { get; init; }

    /// <summary>
    /// Names of algorithms that flagged this message as spam
    /// </summary>
    public List<string> ContentCheckNames { get; init; } = [];

    /// <summary>
    /// OpenAI's score when vetoing (0.0-5.0)
    /// </summary>
    public double OpenAIScore { get; init; }

    /// <summary>
    /// OpenAI's explanation for why the message is not spam
    /// </summary>
    public string? OpenAIReason { get; init; }
}

using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// DTO for training data deduplication analysis
/// Represents a single training sample with metadata for duplicate detection
/// </summary>
public class TrainingSampleDto
{
    public long Id { get; set; }
    public int MessageId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public bool IsSpam { get; set; }
    public double Score { get; set; }
    public string DetectionSource { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
    public Actor AddedBy { get; set; } = Actor.Unknown;
}

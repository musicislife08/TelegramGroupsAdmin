using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Training label record (domain model for UI layer).
/// Represents explicit spam/ham label for ML training.
/// </summary>
public record TrainingLabelRecord
{
    public required long MessageId { get; init; }
    public required TrainingLabel Label { get; init; }
    public long? LabeledByUserId { get; init; }
    public required DateTimeOffset LabeledAt { get; init; }
    public string? Reason { get; init; }
    public long? AuditLogId { get; init; }
}

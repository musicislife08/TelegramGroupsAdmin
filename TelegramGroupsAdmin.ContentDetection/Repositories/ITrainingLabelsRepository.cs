namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing explicit spam/ham training labels.
/// Separates ML training intent from detection_results history.
/// </summary>
public interface ITrainingLabelsRepository
{
    /// <summary>
    /// Upserts a training label for a message (INSERT ... ON CONFLICT DO UPDATE).
    /// Admin corrections override auto-detection with no invalidation dance.
    /// </summary>
    /// <param name="messageId">Message ID to label</param>
    /// <param name="label">Label: "spam" or "ham"</param>
    /// <param name="labeledByUserId">User ID who created the label (nullable for system/migrated labels)</param>
    /// <param name="reason">Optional reason/explanation for the label</param>
    /// <param name="auditLogId">Optional audit log ID linking to moderation action</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpsertLabelAsync(
        long messageId,
        string label,
        long? labeledByUserId = null,
        string? reason = null,
        long? auditLogId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a training label for a message.
    /// Used when removing a sample from training data.
    /// </summary>
    /// <param name="messageId">Message ID to delete label for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteLabelAsync(long messageId, CancellationToken cancellationToken = default);
}

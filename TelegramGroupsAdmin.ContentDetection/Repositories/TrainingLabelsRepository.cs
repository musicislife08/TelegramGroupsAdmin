using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing explicit spam/ham training labels.
/// Uses PostgreSQL ON CONFLICT DO UPDATE for atomic upserts.
/// </summary>
public class TrainingLabelsRepository : ITrainingLabelsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<TrainingLabelsRepository> _logger;

    public TrainingLabelsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<TrainingLabelsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Upserts a training label using PostgreSQL ON CONFLICT DO UPDATE.
    /// Atomic operation - no race conditions, no invalidation dance.
    /// </summary>
    public async Task UpsertLabelAsync(
        long messageId,
        string label,
        long? labeledByUserId = null,
        string? reason = null,
        long? auditLogId = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Validate label before insertion
        if (label != "spam" && label != "ham")
        {
            throw new ArgumentException($"Invalid label '{label}'. Must be 'spam' or 'ham'.", nameof(label));
        }

        await context.Database.ExecuteSqlRawAsync(@"
            INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at, reason, audit_log_id)
            VALUES (@p0, @p1, @p2, NOW(), @p3, @p4)
            ON CONFLICT (message_id) DO UPDATE SET
                label = EXCLUDED.label,
                labeled_by_user_id = EXCLUDED.labeled_by_user_id,
                labeled_at = EXCLUDED.labeled_at,
                reason = EXCLUDED.reason,
                audit_log_id = EXCLUDED.audit_log_id
            ",
            new NpgsqlParameter("@p0", messageId),
            new NpgsqlParameter("@p1", label),
            new NpgsqlParameter("@p2", labeledByUserId ?? (object)DBNull.Value),
            new NpgsqlParameter("@p3", reason ?? (object)DBNull.Value),
            new NpgsqlParameter("@p4", auditLogId ?? (object)DBNull.Value),
            cancellationToken);

        _logger.LogInformation(
            "Upserted training label for message {MessageId}: {Label} (by user {UserId})",
            messageId, label, labeledByUserId);
    }

    /// <summary>
    /// Deletes a training label for a message.
    /// </summary>
    public async Task DeleteLabelAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM training_labels WHERE message_id = @p0
            ",
            new NpgsqlParameter("@p0", messageId),
            cancellationToken);

        _logger.LogInformation("Deleted training label for message {MessageId}", messageId);
    }
}

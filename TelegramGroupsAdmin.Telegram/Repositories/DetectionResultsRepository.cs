using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class DetectionResultsRepository : IDetectionResultsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<DetectionResultsRepository> _logger;

    public DetectionResultsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<DetectionResultsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task InsertAsync(DetectionResultRecord result)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = result.ToDto();
        context.DetectionResults.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogDebug(
            "Inserted detection result for message {MessageId}: {IsSpam} (confidence: {Confidence}, net: {NetConfidence}, training: {UsedForTraining}, edit_version: {EditVersion})",
            result.MessageId,
            result.IsSpam ? "spam" : "ham",
            result.Confidence,
            result.NetConfidence,
            result.UsedForTraining,
            result.EditVersion);
    }

    public async Task<DetectionResultRecord?> GetByIdAsync(long id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var result = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.Id == id)
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { DetectionResult = dr, Message = m })
            .Select(x => new DetectionResultRecord
            {
                Id = x.DetectionResult.Id,
                MessageId = x.DetectionResult.MessageId,
                DetectedAt = x.DetectionResult.DetectedAt,
                DetectionSource = x.DetectionResult.DetectionSource,
                DetectionMethod = x.DetectionResult.DetectionMethod,
                IsSpam = x.DetectionResult.IsSpam,
                Confidence = x.DetectionResult.Confidence,
                Reason = x.DetectionResult.Reason,
                AddedBy = x.DetectionResult.AddedBy,
                UsedForTraining = x.DetectionResult.UsedForTraining,
                NetConfidence = x.DetectionResult.NetConfidence,
                CheckResultsJson = x.DetectionResult.CheckResultsJson,
                EditVersion = x.DetectionResult.EditVersion,
                UserId = x.Message.UserId,
                MessageText = x.Message.MessageText
            })
            .FirstOrDefaultAsync();

        return result;
    }

    public async Task<List<DetectionResultRecord>> GetByMessageIdAsync(long messageId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var results = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.MessageId == messageId)
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { DetectionResult = dr, Message = m })
            .OrderByDescending(x => x.DetectionResult.DetectedAt)
            .Select(x => new DetectionResultRecord
            {
                Id = x.DetectionResult.Id,
                MessageId = x.DetectionResult.MessageId,
                DetectedAt = x.DetectionResult.DetectedAt,
                DetectionSource = x.DetectionResult.DetectionSource,
                DetectionMethod = x.DetectionResult.DetectionMethod,
                IsSpam = x.DetectionResult.IsSpam,
                Confidence = x.DetectionResult.Confidence,
                Reason = x.DetectionResult.Reason,
                AddedBy = x.DetectionResult.AddedBy,
                UsedForTraining = x.DetectionResult.UsedForTraining,
                NetConfidence = x.DetectionResult.NetConfidence,
                CheckResultsJson = x.DetectionResult.CheckResultsJson,
                EditVersion = x.DetectionResult.EditVersion,
                UserId = x.Message.UserId,
                MessageText = x.Message.MessageText
            })
            .ToListAsync();

        return results;
    }

    public async Task<List<DetectionResultRecord>> GetRecentAsync(int limit = 100)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var results = await context.DetectionResults
            .AsNoTracking()
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { DetectionResult = dr, Message = m })
            .OrderByDescending(x => x.DetectionResult.DetectedAt)
            .Take(limit)
            .Select(x => new DetectionResultRecord
            {
                Id = x.DetectionResult.Id,
                MessageId = x.DetectionResult.MessageId,
                DetectedAt = x.DetectionResult.DetectedAt,
                DetectionSource = x.DetectionResult.DetectionSource,
                DetectionMethod = x.DetectionResult.DetectionMethod,
                IsSpam = x.DetectionResult.IsSpam,
                Confidence = x.DetectionResult.Confidence,
                Reason = x.DetectionResult.Reason,
                AddedBy = x.DetectionResult.AddedBy,
                UsedForTraining = x.DetectionResult.UsedForTraining,
                NetConfidence = x.DetectionResult.NetConfidence,
                CheckResultsJson = x.DetectionResult.CheckResultsJson,
                EditVersion = x.DetectionResult.EditVersion,
                UserId = x.Message.UserId,
                MessageText = x.Message.MessageText
            })
            .ToListAsync();

        return results;
    }

    public async Task<List<(string MessageText, bool IsSpam)>> GetTrainingSamplesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Phase 2.6: Only use high-quality training samples
        // - Manual admin decisions (always training-worthy)
        // - Confident OpenAI results (85%+, marked as used_for_training = true)
        // This prevents low-quality auto-detections from polluting training data
        var results = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.UsedForTraining == true)
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { Message = m, dr.IsSpam })
            .Where(x => x.Message.MessageText != null && x.Message.MessageText != "")
            .OrderByDescending(x => x.IsSpam)
            .Select(x => new { x.Message.MessageText, x.IsSpam })
            .ToListAsync();

        _logger.LogDebug(
            "Retrieved {Count} training samples for Bayes classifier (used_for_training = true)",
            results.Count);

        return results.Select(r => (r.MessageText!, r.IsSpam)).ToList();
    }

    public async Task<List<string>> GetSpamSamplesForSimilarityAsync(int limit = 1000)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Phase 2.6: Only use high-quality training samples for similarity matching
        var results = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.IsSpam == true && dr.UsedForTraining == true)
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { dr.DetectedAt, m.MessageText })
            .Where(x => x.MessageText != null && x.MessageText != "")
            .OrderByDescending(x => x.DetectedAt)
            .Take(limit)
            .Select(x => x.MessageText!)
            .ToListAsync();

        _logger.LogDebug(
            "Retrieved {Count} spam samples for similarity check (used_for_training = true)",
            results.Count);

        return results;
    }

    public async Task<bool> IsUserTrustedAsync(long userId, long? chatId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Check for active 'trust' action
        // All trusts are global now (no chat_ids column)
        var now = DateTimeOffset.UtcNow;
        var isTrusted = await context.UserActions
            .AsNoTracking()
            .AnyAsync(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Trust
                && (ua.ExpiresAt == null || ua.ExpiresAt > now));

        if (isTrusted)
        {
            _logger.LogDebug(
                "User {UserId} is trusted (chat: {ChatId})",
                userId,
                chatId?.ToString() ?? "global");
        }

        return isTrusted;
    }

    public async Task<List<DetectionResultRecord>> GetRecentNonSpamResultsForUserAsync(long userId, int limit = 3)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Get last N non-spam detection results for this user (global, not per-chat)
        // Used for auto-whitelisting: if user has N consecutive non-spam messages, trust them
        var results = await context.DetectionResults
            .AsNoTracking()
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { DetectionResult = dr, Message = m })
            .Where(x => x.Message.UserId == userId && !x.DetectionResult.IsSpam)
            .OrderByDescending(x => x.DetectionResult.DetectedAt)
            .Take(limit)
            .Select(x => new DetectionResultRecord
            {
                Id = x.DetectionResult.Id,
                MessageId = x.DetectionResult.MessageId,
                DetectedAt = x.DetectionResult.DetectedAt,
                DetectionSource = x.DetectionResult.DetectionSource,
                DetectionMethod = x.DetectionResult.DetectionMethod,
                IsSpam = x.DetectionResult.IsSpam,
                Confidence = x.DetectionResult.Confidence,
                Reason = x.DetectionResult.Reason,
                AddedBy = x.DetectionResult.AddedBy,
                UsedForTraining = x.DetectionResult.UsedForTraining,
                NetConfidence = x.DetectionResult.NetConfidence,
                CheckResultsJson = x.DetectionResult.CheckResultsJson,
                EditVersion = x.DetectionResult.EditVersion,
                UserId = x.Message.UserId,
                MessageText = x.Message.MessageText
            })
            .ToListAsync();

        _logger.LogDebug(
            "Retrieved {Count} recent non-spam results for user {UserId} (limit: {Limit})",
            results.Count,
            userId,
            limit);

        return results;
    }

    public async Task<DetectionStats> GetStatsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // MH1: Single query optimization - calculate all stats in one database round-trip
        var since24h = DateTimeOffset.UtcNow.AddDays(-1);

        var stats = await context.DetectionResults
            .AsNoTracking()
            .GroupBy(dr => 1) // Group all rows together for aggregation
            .Select(g => new
            {
                TotalDetections = g.Count(),
                SpamDetected = g.Count(dr => dr.IsSpam),
                AverageConfidence = g.Average(dr => (double)dr.Confidence),
                Last24hDetections = g.Count(dr => dr.DetectedAt >= since24h),
                Last24hSpam = g.Count(dr => dr.DetectedAt >= since24h && dr.IsSpam)
            })
            .FirstOrDefaultAsync();

        // Handle empty table case
        if (stats == null)
        {
            return new DetectionStats
            {
                TotalDetections = 0,
                SpamDetected = 0,
                SpamPercentage = 0,
                AverageConfidence = 0,
                Last24hDetections = 0,
                Last24hSpam = 0,
                Last24hSpamPercentage = 0
            };
        }

        return new DetectionStats
        {
            TotalDetections = stats.TotalDetections,
            SpamDetected = stats.SpamDetected,
            SpamPercentage = stats.TotalDetections > 0 ? (double)stats.SpamDetected / stats.TotalDetections * 100 : 0,
            AverageConfidence = stats.AverageConfidence,
            Last24hDetections = stats.Last24hDetections,
            Last24hSpam = stats.Last24hSpam,
            Last24hSpamPercentage = stats.Last24hDetections > 0 ? (double)stats.Last24hSpam / stats.Last24hDetections * 100 : 0
        };
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset timestamp)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Note: Per CLAUDE.md, detection_results should be permanent
        // This method exists for completeness but should rarely be used
        var toDelete = await context.DetectionResults
            .Where(dr => dr.DetectedAt < timestamp)
            .ToListAsync();

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            context.DetectionResults.RemoveRange(toDelete);
            await context.SaveChangesAsync();

            _logger.LogWarning(
                "Deleted {Count} old detection results (timestamp < {Timestamp})",
                deleted,
                timestamp);
        }

        return deleted;
    }

    // ====================================================================================
    // Training Data Management Methods (for TrainingData.razor UI)
    // ====================================================================================

    public async Task<List<DetectionResultRecord>> GetAllTrainingDataAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var results = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.UsedForTraining == true)
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { DetectionResult = dr, Message = m })
            .OrderByDescending(x => x.DetectionResult.DetectedAt)
            .Select(x => new DetectionResultRecord
            {
                Id = x.DetectionResult.Id,
                MessageId = x.DetectionResult.MessageId,
                DetectedAt = x.DetectionResult.DetectedAt,
                DetectionSource = x.DetectionResult.DetectionSource,
                DetectionMethod = x.DetectionResult.DetectionMethod,
                IsSpam = x.DetectionResult.IsSpam,
                Confidence = x.DetectionResult.Confidence,
                Reason = x.DetectionResult.Reason,
                AddedBy = x.DetectionResult.AddedBy,
                UsedForTraining = x.DetectionResult.UsedForTraining,
                NetConfidence = x.DetectionResult.NetConfidence,
                CheckResultsJson = x.DetectionResult.CheckResultsJson,
                EditVersion = x.DetectionResult.EditVersion,
                UserId = x.Message.UserId,
                MessageText = x.Message.MessageText
            })
            .ToListAsync();

        _logger.LogDebug("Retrieved {Count} training data records (used_for_training = true)", results.Count);
        return results;
    }

    public async Task<TrainingDataStats> GetTrainingDataStatsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var trainingData = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.UsedForTraining == true)
            .Select(dr => new { dr.IsSpam, dr.DetectionSource })
            .ToListAsync();

        var total = trainingData.Count;
        var spam = trainingData.Count(d => d.IsSpam);
        var ham = total - spam;

        var sourceGroups = trainingData
            .GroupBy(d => d.DetectionSource)
            .ToDictionary(g => g.Key, g => g.Count());

        return new TrainingDataStats
        {
            TotalSamples = total,
            SpamSamples = spam,
            HamSamples = ham,
            SpamPercentage = total > 0 ? (double)spam / total * 100 : 0,
            SamplesBySource = sourceGroups
        };
    }

    public async Task UpdateDetectionResultAsync(long id, bool isSpam, bool usedForTraining)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.DetectionResults.FindAsync(id);
        if (entity == null)
        {
            throw new InvalidOperationException($"Detection result {id} not found");
        }

        // IsSpam is computed from net_confidence, so update that instead
        entity.NetConfidence = isSpam ? 100 : -100;
        entity.UsedForTraining = usedForTraining;
        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Updated detection result {Id}: IsSpam={IsSpam} (net_confidence={NetConfidence}), UsedForTraining={UsedForTraining}",
            id, isSpam, entity.NetConfidence, usedForTraining);
    }

    public async Task DeleteDetectionResultAsync(long id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.DetectionResults.FindAsync(id);
        if (entity == null)
        {
            throw new InvalidOperationException($"Detection result {id} not found");
        }

        context.DetectionResults.Remove(entity);
        await context.SaveChangesAsync();

        _logger.LogWarning("Deleted detection result {Id}", id);
    }

    public async Task<long> AddManualTrainingSampleAsync(string messageText, bool isSpam, string source, int? confidence, string? addedBy)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Create message record (chat_id=0, user_id=0 pattern for manual samples)
        // user_id=0 maps to "system" user in telegram_users table
        var message = new DataModels.MessageRecordDto
        {
            ChatId = 0,
            UserId = 0,
            MessageText = messageText,
            Timestamp = DateTimeOffset.UtcNow,
            ContentHash = null
        };

        context.Messages.Add(message);
        await context.SaveChangesAsync(); // Save to get message_id

        // Create detection_result record linked to the message
        var detectionResult = new DataModels.DetectionResultRecordDto
        {
            MessageId = message.MessageId,
            DetectedAt = DateTimeOffset.UtcNow,
            DetectionSource = source,
            DetectionMethod = "Manual",
            // IsSpam computed from net_confidence
            Confidence = confidence ?? 100,
            Reason = "Manually added training sample",
            AddedBy = addedBy,
            UsedForTraining = true,
            NetConfidence = isSpam ? 100 : -100,  // Manual: 100 = spam, -100 = ham
            CheckResultsJson = null,
            EditVersion = 0
        };

        context.DetectionResults.Add(detectionResult);
        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Added manual training sample: message_id={MessageId}, detection_result_id={Id}, is_spam={IsSpam}, source={Source}, added_by={AddedBy}",
            message.MessageId,
            detectionResult.Id,
            isSpam,
            source,
            addedBy ?? "System");

        return detectionResult.Id;
    }
}

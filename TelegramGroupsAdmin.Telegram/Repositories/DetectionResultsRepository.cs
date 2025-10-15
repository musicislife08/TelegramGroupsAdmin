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
        var entity = result.ToDataModel();
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
        // Overall stats
        var allDetections = await context.DetectionResults
            .AsNoTracking()
            .Select(dr => new { dr.IsSpam, dr.Confidence })
            .ToListAsync();

        var total = allDetections.Count;
        var spam = allDetections.Count(d => d.IsSpam);
        var avgConfidence = allDetections.Any()
            ? allDetections.Average(d => (double)d.Confidence)
            : 0.0;

        // Last 24h stats
        var since24h = DateTimeOffset.UtcNow.AddDays(-1);
        var recentDetections = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.DetectedAt >= since24h)
            .Select(dr => dr.IsSpam)
            .ToListAsync();

        var recentTotal = recentDetections.Count;
        var recentSpam = recentDetections.Count(s => s);

        return new DetectionStats
        {
            TotalDetections = total,
            SpamDetected = spam,
            SpamPercentage = total > 0 ? (double)spam / total * 100 : 0,
            AverageConfidence = avgConfidence,
            Last24hDetections = recentTotal,
            Last24hSpam = recentSpam,
            Last24hSpamPercentage = recentTotal > 0 ? (double)recentSpam / recentTotal * 100 : 0
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
}

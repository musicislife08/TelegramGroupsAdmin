using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

public class DetectionResultsRepository : IDetectionResultsRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<DetectionResultsRepository> _logger;

    public DetectionResultsRepository(
        AppDbContext context,
        ILogger<DetectionResultsRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task InsertAsync(DetectionResultRecord result)
    {
        var entity = result.ToDataModel();
        _context.DetectionResults.Add(entity);
        await _context.SaveChangesAsync();

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
        var result = await _context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.Id == id)
            .Join(_context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { DetectionResult = dr, Message = m })
            .Select(x => new DetectionResultRecord
            {
                Id = x.DetectionResult.Id,
                MessageId = x.DetectionResult.MessageId,
                DetectedAt = x.DetectionResult.DetectedAt,
                DetectionSource = x.DetectionResult.DetectionSource,
                DetectionMethod = x.DetectionResult.DetectionMethod ?? "Unknown",
                IsSpam = x.DetectionResult.IsSpam,
                Confidence = x.DetectionResult.Confidence ?? 0,
                Reason = x.DetectionResult.Reason,
                AddedBy = x.DetectionResult.AddedBy,
                UsedForTraining = x.DetectionResult.UsedForTraining ?? true,
                NetConfidence = x.DetectionResult.NetConfidence,
                CheckResultsJson = x.DetectionResult.CheckResults,
                EditVersion = x.DetectionResult.EditVersion ?? 0,
                UserId = x.Message.UserId,
                MessageText = x.Message.MessageText
            })
            .FirstOrDefaultAsync();

        return result;
    }

    public async Task<List<DetectionResultRecord>> GetByMessageIdAsync(long messageId)
    {
        var results = await _context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.MessageId == messageId)
            .Join(_context.Messages,
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
                DetectionMethod = x.DetectionResult.DetectionMethod ?? "Unknown",
                IsSpam = x.DetectionResult.IsSpam,
                Confidence = x.DetectionResult.Confidence ?? 0,
                Reason = x.DetectionResult.Reason,
                AddedBy = x.DetectionResult.AddedBy,
                UsedForTraining = x.DetectionResult.UsedForTraining ?? true,
                NetConfidence = x.DetectionResult.NetConfidence,
                CheckResultsJson = x.DetectionResult.CheckResults,
                EditVersion = x.DetectionResult.EditVersion ?? 0,
                UserId = x.Message.UserId,
                MessageText = x.Message.MessageText
            })
            .ToListAsync();

        return results;
    }

    public async Task<List<DetectionResultRecord>> GetRecentAsync(int limit = 100)
    {
        var results = await _context.DetectionResults
            .AsNoTracking()
            .Join(_context.Messages,
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
                DetectionMethod = x.DetectionResult.DetectionMethod ?? "Unknown",
                IsSpam = x.DetectionResult.IsSpam,
                Confidence = x.DetectionResult.Confidence ?? 0,
                Reason = x.DetectionResult.Reason,
                AddedBy = x.DetectionResult.AddedBy,
                UsedForTraining = x.DetectionResult.UsedForTraining ?? true,
                NetConfidence = x.DetectionResult.NetConfidence,
                CheckResultsJson = x.DetectionResult.CheckResults,
                EditVersion = x.DetectionResult.EditVersion ?? 0,
                UserId = x.Message.UserId,
                MessageText = x.Message.MessageText
            })
            .ToListAsync();

        return results;
    }

    public async Task<List<(string MessageText, bool IsSpam)>> GetTrainingSamplesAsync()
    {
        // Phase 2.6: Only use high-quality training samples
        // - Manual admin decisions (always training-worthy)
        // - Confident OpenAI results (85%+, marked as used_for_training = true)
        // This prevents low-quality auto-detections from polluting training data
        var results = await _context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.UsedForTraining == true)
            .Join(_context.Messages,
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
        // Phase 2.6: Only use high-quality training samples for similarity matching
        var results = await _context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.IsSpam == true && dr.UsedForTraining == true)
            .Join(_context.Messages,
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
        // Check for active 'trust' action
        // All trusts are global now (no chat_ids column)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isTrusted = await _context.UserActions
            .AsNoTracking()
            .AnyAsync(ua => ua.UserId == userId
                && ua.ActionType == Models.UserActionType.Trust
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

    public async Task<DetectionStats> GetStatsAsync()
    {
        // Overall stats
        var allDetections = await _context.DetectionResults
            .AsNoTracking()
            .Select(dr => new { dr.IsSpam, dr.Confidence })
            .ToListAsync();

        var total = allDetections.Count;
        var spam = allDetections.Count(d => d.IsSpam);
        var avgConfidence = allDetections.Any()
            ? allDetections.Average(d => (double)(d.Confidence ?? 0))
            : 0.0;

        // Last 24h stats
        var since24h = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var recentDetections = await _context.DetectionResults
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

    public async Task<int> DeleteOlderThanAsync(long timestamp)
    {
        // Note: Per CLAUDE.md, detection_results should be permanent
        // This method exists for completeness but should rarely be used
        var toDelete = await _context.DetectionResults
            .Where(dr => dr.DetectedAt < timestamp)
            .ToListAsync();

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            _context.DetectionResults.RemoveRange(toDelete);
            await _context.SaveChangesAsync();

            _logger.LogWarning(
                "Deleted {Count} old detection results (timestamp < {Timestamp})",
                deleted,
                timestamp);
        }

        return deleted;
    }
}

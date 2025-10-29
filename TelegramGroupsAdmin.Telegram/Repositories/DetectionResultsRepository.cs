using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
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

    /// <summary>
    /// Strongly-typed record for detection result + message JOIN
    /// Used to avoid duplicate JOIN patterns in training sample queries (H10)
    /// </summary>
    private record DetectionResultWithMessage(
        DataModels.DetectionResultRecordDto DetectionResult,
        DataModels.MessageRecordDto Message);

    /// <summary>
    /// Helper to JOIN detection_results with messages table (H10)
    /// Returns queryable with strongly-typed DetectionResultWithMessage records
    /// </summary>
    private static IQueryable<DetectionResultWithMessage> WithMessageJoin(
        IQueryable<DataModels.DetectionResultRecordDto> detectionResults,
        AppDbContext context)
    {
        return detectionResults
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new DetectionResultWithMessage(dr, m));
    }

    /// <summary>
    /// Helper to add actor JOINs to detection results query (Phase 4.19)
    /// Returns queryable with full actor information for display names
    /// </summary>
    private static IQueryable<DetectionResultRecord> WithActorJoins(
        IQueryable<DataModels.DetectionResultRecordDto> detectionResults,
        AppDbContext context)
    {
        return detectionResults
            .Join(context.Messages, dr => dr.MessageId, m => m.MessageId, (dr, m) => new { dr, m })
            .GroupJoin(context.Users, x => x.dr.WebUserId, u => u.Id, (x, users) => new { x.dr, x.m, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, user) => new { x.dr, x.m, user })
            .GroupJoin(context.TelegramUsers, x => x.dr.TelegramUserId, tu => tu.TelegramUserId, (x, tgUsers) => new { x.dr, x.m, x.user, tgUsers })
            .SelectMany(x => x.tgUsers.DefaultIfEmpty(), (x, tgUser) => new
            {
                x.dr,
                x.m,
                ActorWebEmail = x.user != null ? x.user.Email : null,
                ActorTelegramUsername = tgUser != null ? tgUser.Username : null,
                ActorTelegramFirstName = tgUser != null ? tgUser.FirstName : null
            })
            .Select(x => new DetectionResultRecord
            {
                Id = x.dr.Id,
                MessageId = x.dr.MessageId,
                DetectedAt = x.dr.DetectedAt,
                DetectionSource = x.dr.DetectionSource,
                DetectionMethod = x.dr.DetectionMethod,
                IsSpam = x.dr.IsSpam,
                Confidence = x.dr.Confidence,
                Reason = x.dr.Reason,
                AddedBy = ActorMappings.ToActor(x.dr.WebUserId, x.dr.TelegramUserId, x.dr.SystemIdentifier, x.ActorWebEmail, x.ActorTelegramUsername, x.ActorTelegramFirstName),
                UsedForTraining = x.dr.UsedForTraining,
                NetConfidence = x.dr.NetConfidence,
                CheckResultsJson = x.dr.CheckResultsJson,
                EditVersion = x.dr.EditVersion,
                UserId = x.m.UserId,
                MessageText = x.m.MessageText
            });
    }

    public async Task InsertAsync(DetectionResultRecord result, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = result.ToDto();
        context.DetectionResults.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Inserted detection result for message {MessageId}: {IsSpam} (confidence: {Confidence}, net: {NetConfidence}, training: {UsedForTraining}, edit_version: {EditVersion})",
            result.MessageId,
            result.IsSpam ? "spam" : "ham",
            result.Confidence,
            result.NetConfidence,
            result.UsedForTraining,
            result.EditVersion);
    }

    public async Task<DetectionResultRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var result = await WithActorJoins(
                context.DetectionResults.AsNoTracking().Where(dr => dr.Id == id),
                context)
            .FirstOrDefaultAsync(cancellationToken);

        return result;
    }

    public async Task<List<DetectionResultRecord>> GetByMessageIdAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var results = await WithActorJoins(
                context.DetectionResults.AsNoTracking().Where(dr => dr.MessageId == messageId),
                context)
            .OrderByDescending(x => x.DetectedAt)
            .ToListAsync(cancellationToken);

        return results;
    }

    public async Task<Dictionary<long, List<DetectionResultRecord>>> GetDetectionHistoryBatchAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Convert to list to avoid multiple enumeration
        var messageIdList = messageIds.ToList();

        // Single query with WHERE message_id IN (...)
        var results = await WithActorJoins(
                context.DetectionResults.AsNoTracking().Where(dr => messageIdList.Contains(dr.MessageId)),
                context)
            .OrderByDescending(x => x.DetectedAt)
            .ToListAsync(cancellationToken);

        // Group by message_id for efficient lookup
        return results
            .GroupBy(r => r.MessageId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<List<DetectionResultRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var results = await WithActorJoins(
                context.DetectionResults.AsNoTracking(),
                context)
            .OrderByDescending(x => x.DetectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results;
    }

    public async Task<List<(string MessageText, bool IsSpam)>> GetTrainingSamplesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Phase 2.6: Only use high-quality training samples
        // - Manual admin decisions (always training-worthy)
        // - Confident OpenAI results (85%+, marked as used_for_training = true)
        // This prevents low-quality auto-detections from polluting training data
        var results = await WithMessageJoin(
                context.DetectionResults.AsNoTracking(),
                context)
            .Where(x => x.DetectionResult.UsedForTraining == true)
            .Where(x => x.Message.MessageText != null && x.Message.MessageText != "")
            .OrderByDescending(x => x.DetectionResult.IsSpam)
            .Select(x => new { x.Message.MessageText, x.DetectionResult.IsSpam })
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "Retrieved {Count} training samples for Bayes classifier (used_for_training = true)",
            results.Count);

        return results.Select(r => (r.MessageText!, r.IsSpam)).ToList();
    }

    public async Task<List<string>> GetSpamSamplesForSimilarityAsync(int limit = 1000, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Phase 2.6: Only use high-quality training samples for similarity matching
        var results = await WithMessageJoin(
                context.DetectionResults.AsNoTracking(),
                context)
            .Where(x => x.DetectionResult.IsSpam == true && x.DetectionResult.UsedForTraining == true)
            .Where(x => x.Message.MessageText != null && x.Message.MessageText != "")
            .OrderByDescending(x => x.DetectionResult.DetectedAt)
            .Take(limit)
            .Select(x => x.Message.MessageText!)
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "Retrieved {Count} spam samples for similarity check (used_for_training = true)",
            results.Count);

        return results;
    }

    public async Task<bool> IsUserTrustedAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Check for active 'trust' action
        // All trusts are global now (no chat_ids column)
        var now = DateTimeOffset.UtcNow;
        var isTrusted = await context.UserActions
            .AsNoTracking()
            .AnyAsync(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Trust
                && (ua.ExpiresAt == null || ua.ExpiresAt > now), cancellationToken);

        if (isTrusted)
        {
            _logger.LogDebug(
                "User {UserId} is trusted (chat: {ChatId})",
                userId,
                chatId?.ToString() ?? "global");
        }

        return isTrusted;
    }

    public async Task<List<DetectionResultRecord>> GetRecentNonSpamResultsForUserAsync(long userId, int limit = 3, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Get last N non-spam detection results for this user (global, not per-chat)
        // Used for auto-whitelisting: if user has N consecutive non-spam messages, trust them
        var results = await WithActorJoins(
                context.DetectionResults.AsNoTracking(),
                context)
            .Where(x => x.UserId == userId && !x.IsSpam)
            .OrderByDescending(x => x.DetectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "Retrieved {Count} recent non-spam results for user {UserId} (limit: {Limit})",
            results.Count,
            userId,
            limit);

        return results;
    }

    public async Task<DetectionStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

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
            .FirstOrDefaultAsync(cancellationToken);

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

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Note: Per CLAUDE.md, detection_results should be permanent
        // This method exists for completeness but should rarely be used
        var toDelete = await context.DetectionResults
            .Where(dr => dr.DetectedAt < timestamp)
            .ToListAsync(cancellationToken);

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            context.DetectionResults.RemoveRange(toDelete);
            await context.SaveChangesAsync(cancellationToken);

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

    public async Task<List<DetectionResultRecord>> GetAllTrainingDataAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var results = await WithActorJoins(
                context.DetectionResults.AsNoTracking().Where(dr => dr.UsedForTraining == true),
                context)
            .OrderByDescending(x => x.DetectedAt)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Retrieved {Count} training data records (used_for_training = true)", results.Count);
        return results;
    }

    public async Task<TrainingDataStats> GetTrainingDataStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var trainingData = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.UsedForTraining == true)
            .Select(dr => new { dr.IsSpam, dr.DetectionSource })
            .ToListAsync(cancellationToken);

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

    public async Task UpdateDetectionResultAsync(long id, bool isSpam, bool usedForTraining, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.DetectionResults.FindAsync([id], cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException($"Detection result {id} not found");
        }

        // IsSpam is computed from net_confidence, so update that instead
        entity.NetConfidence = isSpam ? 100 : -100;
        entity.UsedForTraining = usedForTraining;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated detection result {Id}: IsSpam={IsSpam} (net_confidence={NetConfidence}), UsedForTraining={UsedForTraining}",
            id, isSpam, entity.NetConfidence, usedForTraining);
    }

    public async Task DeleteDetectionResultAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.DetectionResults.FindAsync([id], cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException($"Detection result {id} not found");
        }

        context.DetectionResults.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Deleted detection result {Id}", id);
    }

    public async Task<long> AddManualTrainingSampleAsync(string messageText, bool isSpam, string source, int? confidence, string? addedBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get next negative message_id for manual samples
        // Negative IDs distinguish manual samples from real Telegram messages (which have positive IDs)
        var nextManualId = await context.Messages
            .Where(m => m.ChatId == 0 && m.MessageId < 0)
            .Select(m => m.MessageId)
            .OrderBy(m => m)
            .FirstOrDefaultAsync(cancellationToken);

        var messageId = nextManualId == 0 ? -1 : nextManualId - 1; // Start at -1, then -2, -3, etc.

        // Create message record (chat_id=0, user_id=0 pattern for manual samples)
        // user_id=0 maps to "system" user in telegram_users table
        var message = new DataModels.MessageRecordDto
        {
            ChatId = 0,
            MessageId = messageId, // Use negative ID for manual samples
            UserId = 0,
            MessageText = messageText,
            Timestamp = DateTimeOffset.UtcNow,
            ContentHash = null
        };

        context.Messages.Add(message);
        await context.SaveChangesAsync(cancellationToken);

        // Create detection_result record linked to the message
        // Phase 4.19: Actor system - manual samples use SystemIdentifier
        var detectionResult = new DataModels.DetectionResultRecordDto
        {
            MessageId = message.MessageId,
            DetectedAt = DateTimeOffset.UtcNow,
            DetectionSource = source,
            DetectionMethod = "Manual",
            // IsSpam computed from net_confidence
            Confidence = confidence ?? 100,
            Reason = "Manually added training sample",
            SystemIdentifier = addedBy ?? "System",  // Phase 4.19: Actor system
            UsedForTraining = true,
            NetConfidence = isSpam ? 100 : -100,  // Manual: 100 = spam, -100 = ham
            CheckResultsJson = null,
            EditVersion = 0
        };

        context.DetectionResults.Add(detectionResult);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Added manual training sample: message_id={MessageId}, detection_result_id={Id}, is_spam={IsSpam}, source={Source}, added_by={AddedBy}",
            message.MessageId,
            detectionResult.Id,
            isSpam,
            source,
            addedBy ?? "System");

        return detectionResult.Id;
    }

    // ====================================================================================
    // File Scanning UI Methods (Phase 4.22)
    // ====================================================================================

    public async Task<List<DetectionResultRecord>> GetFileScanResultsAsync(
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var results = await WithActorJoins(
                context.DetectionResults.AsNoTracking().Where(dr => dr.DetectionSource == "file_scan"),
                context)
            .OrderByDescending(dr => dr.DetectedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results;
    }

    public async Task<Dictionary<string, int>> GetFileScanStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);

        var stats = await context.DetectionResults
            .Where(dr => dr.DetectionSource == "file_scan" &&
                        dr.IsSpam == true && // Only infected files
                        dr.DetectedAt >= sevenDaysAgo)
            .GroupBy(dr => dr.DetectionMethod)
            .Select(g => new { Scanner = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return stats.ToDictionary(s => s.Scanner ?? "Unknown", s => s.Count);
    }

    public async Task<int> GetFileScanResultsCountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.DetectionResults
            .Where(dr => dr.DetectionSource == "file_scan")
            .CountAsync(cancellationToken);
    }

    public async Task<OpenAIVetoAnalytics> GetOpenAIVetoAnalyticsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get all detections where is_spam = false (OpenAI may have vetoed)
        // and check_results_json contains both OpenAI "clean" result and other "spam" results
        var vetoedDetections = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= since && !dr.IsSpam && dr.CheckResultsJson != null)
            .Select(dr => new { dr.Id, dr.CheckResultsJson })
            .ToListAsync(cancellationToken);

        // Parse JSON to find actual vetoes (OpenAI clean + other checks spam)
        var actualVetoes = vetoedDetections
            .Select(d =>
            {
                try
                {
                    var json = JsonDocument.Parse(d.CheckResultsJson!);
                    var checks = json.RootElement.GetProperty("checks");
                    var checkList = new List<(string name, string result)>();

                    foreach (var check in checks.EnumerateArray())
                    {
                        var name = check.GetProperty("name").GetString() ?? "";
                        var result = check.GetProperty("result").GetString() ?? "";
                        checkList.Add((name, result));
                    }

                    // Check if OpenAI returned "clean" and at least one other check returned "spam"
                    var hasOpenAIClean = checkList.Any(c => c.name == "OpenAI" && c.result == "clean");
                    var hasOtherSpam = checkList.Any(c => c.name != "OpenAI" && c.result == "spam");

                    if (hasOpenAIClean && hasOtherSpam)
                    {
                        return new { d.Id, Checks = checkList };
                    }
                }
                catch
                {
                    // Skip malformed JSON
                }

                return null;
            })
            .Where(v => v != null)
            .ToList();

        // Calculate per-algorithm statistics
        var algorithmStats = actualVetoes
            .SelectMany(v => v!.Checks.Where(c => c.name != "OpenAI" && c.result == "spam").Select(c => c.name))
            .GroupBy(name => name)
            .Select(g => new AlgorithmVetoStats
            {
                AlgorithmName = g.Key,
                SpamFlagsCount = g.Count(),
                VetoedCount = g.Count(), // All these were vetoed
                VetoRate = 100.0m // Will recalculate with total spam flags below
            })
            .ToList();

        // Get total spam flags per algorithm for accurate veto rate
        var allDetections = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= since && dr.CheckResultsJson != null)
            .Select(dr => dr.CheckResultsJson)
            .ToListAsync(cancellationToken);

        var totalSpamFlagsByAlgorithm = new Dictionary<string, int>();
        foreach (var json in allDetections)
        {
            try
            {
                var doc = JsonDocument.Parse(json!);
                var checks = doc.RootElement.GetProperty("checks");

                foreach (var check in checks.EnumerateArray())
                {
                    var name = check.GetProperty("name").GetString();
                    var result = check.GetProperty("result").GetString();

                    if (name != "OpenAI" && result == "spam")
                    {
                        totalSpamFlagsByAlgorithm[name!] = totalSpamFlagsByAlgorithm.GetValueOrDefault(name!, 0) + 1;
                    }
                }
            }
            catch
            {
                // Skip malformed JSON
            }
        }

        // Recalculate veto rates with actual totals
        foreach (var stat in algorithmStats)
        {
            if (totalSpamFlagsByAlgorithm.TryGetValue(stat.AlgorithmName, out var total) && total > 0)
            {
                stat.VetoRate = (decimal)stat.VetoedCount / total * 100;
                stat.SpamFlagsCount = total;
            }
        }

        var totalDetections = allDetections.Count;
        var vetoedCount = actualVetoes.Count;

        return new OpenAIVetoAnalytics
        {
            TotalDetections = totalDetections,
            VetoedCount = vetoedCount,
            VetoRate = totalDetections > 0 ? (decimal)vetoedCount / totalDetections * 100 : 0,
            AlgorithmStats = algorithmStats.OrderByDescending(s => s.VetoRate).ToList()
        };
    }

    public async Task<List<VetoedMessage>> GetRecentVetoedMessagesAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get recent non-spam detections with their message text
        var detections = await context.DetectionResults
            .Where(dr => !dr.IsSpam && dr.CheckResultsJson != null)
            .OrderByDescending(dr => dr.DetectedAt)
            .Take(limit * 2) // Get extra to filter after JSON parsing
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new { dr.Id, dr.MessageId, dr.DetectedAt, dr.CheckResultsJson, m.MessageText })
            .ToListAsync(cancellationToken);

        var vetoedMessages = new List<VetoedMessage>();

        foreach (var detection in detections)
        {
            try
            {
                var json = JsonDocument.Parse(detection.CheckResultsJson!);
                var checks = json.RootElement.GetProperty("checks");

                var openAICheck = checks.EnumerateArray()
                    .FirstOrDefault(c => c.GetProperty("name").GetString() == "OpenAI");

                var spamChecks = checks.EnumerateArray()
                    .Where(c => c.GetProperty("name").GetString() != "OpenAI" &&
                               c.GetProperty("result").GetString() == "spam")
                    .Select(c => c.GetProperty("name").GetString() ?? "")
                    .ToList();

                // Only include if OpenAI vetoed (clean) and other checks flagged spam
                if (openAICheck.ValueKind != JsonValueKind.Undefined &&
                    openAICheck.GetProperty("result").GetString() == "clean" &&
                    spamChecks.Any())
                {
                    vetoedMessages.Add(new VetoedMessage
                    {
                        MessageId = detection.MessageId,
                        DetectedAt = detection.DetectedAt,
                        MessagePreview = detection.MessageText?.Length > 100
                            ? detection.MessageText.Substring(0, 100) + "..."
                            : detection.MessageText,
                        SpamCheckNames = spamChecks,
                        OpenAIConfidence = (int)openAICheck.GetProperty("conf").GetDouble(),
                        OpenAIReason = openAICheck.GetProperty("reason").GetString()
                    });
                }

                if (vetoedMessages.Count >= limit)
                    break;
            }
            catch
            {
                // Skip malformed JSON
            }
        }

        return vetoedMessages;
    }

}

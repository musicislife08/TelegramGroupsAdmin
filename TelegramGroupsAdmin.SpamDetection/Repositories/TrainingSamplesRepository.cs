using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.SpamDetection.Repositories;

/// <summary>
/// Repository implementation for spam detection results (formerly training samples)
/// Queries detection_results table joined with messages table
/// </summary>
public class TrainingSamplesRepository : ITrainingSamplesRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<TrainingSamplesRepository> _logger;

    public TrainingSamplesRepository(AppDbContext context, ILogger<TrainingSamplesRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all detection results (spam and ham samples)
    /// Phase 2.6: Only returns training-worthy samples (used_for_training = true)
    /// </summary>
    public async Task<IEnumerable<TrainingSample>> GetAllSamplesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _context.DetectionResults
                .AsNoTracking()
                .Include(dr => dr.Message)
                .Where(dr => dr.UsedForTraining)
                .OrderByDescending(dr => dr.DetectedAt)
                .Select(dr => new TrainingSample
                {
                    Id = dr.Id,
                    MessageText = dr.Message!.MessageText!,
                    IsSpam = dr.IsSpam,
                    AddedDate = dr.DetectedAt,
                    Source = dr.DetectionSource,
                    ConfidenceWhenAdded = dr.Confidence,
                    ChatIds = new long[] { dr.Message!.ChatId },
                    AddedBy = dr.AddedBy,
                    DetectionCount = 0,
                    LastDetectedDate = null
                })
                .ToListAsync(cancellationToken);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve detection results");
            return Enumerable.Empty<TrainingSample>();
        }
    }

    /// <summary>
    /// Get only spam detection results (is_spam = true)
    /// Used by Similarity spam check for TF-IDF matching
    /// All spam is global - no per-chat filtering needed
    /// Phase 2.6: Only returns training-worthy samples (used_for_training = true)
    /// </summary>
    public async Task<IEnumerable<TrainingSample>> GetSpamSamplesAsync(string? chatId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _context.DetectionResults
                .AsNoTracking()
                .Include(dr => dr.Message)
                .Where(dr => dr.IsSpam && dr.UsedForTraining)
                .OrderByDescending(dr => dr.DetectedAt)
                .Select(dr => new TrainingSample
                {
                    Id = dr.Id,
                    MessageText = dr.Message!.MessageText!,
                    IsSpam = dr.IsSpam,
                    AddedDate = dr.DetectedAt,
                    Source = dr.DetectionSource,
                    ConfidenceWhenAdded = dr.Confidence,
                    ChatIds = new long[] { dr.Message!.ChatId },
                    AddedBy = dr.AddedBy,
                    DetectionCount = 0,
                    LastDetectedDate = null
                })
                .ToListAsync(cancellationToken);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve spam detection results");
            return Enumerable.Empty<TrainingSample>();
        }
    }

    /// <summary>
    /// Increment detection count for a sample when it successfully detects spam/ham
    /// NOTE: Detection count tracking removed in normalized schema
    /// This method is now a no-op for backward compatibility
    /// </summary>
    public async Task IncrementDetectionCountAsync(long sampleId, CancellationToken cancellationToken = default)
    {
        // No-op: detection_count column removed in normalized schema
        // Analytics will be done via COUNT(*) on detection_results table
        await Task.CompletedTask;
        _logger.LogDebug("IncrementDetectionCountAsync called for sample {SampleId} (no-op in new schema)", sampleId);
    }

    /// <summary>
    /// Add a new detection result (spam/ham sample)
    /// Creates or finds message, then adds detection_result record
    /// </summary>
    public async Task<long> AddSampleAsync(string messageText, bool isSpam, string source, int? confidenceWhenAdded = null, string? chatId = null, string? addedBy = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var chatIdLong = chatId != null ? long.Parse(chatId) : -1; // -1 for unknown chat

                // Step 1: Try to find existing message by text and timestamp
                var existingMessage = await _context.Messages
                    .FirstOrDefaultAsync(m => m.MessageText == messageText && m.Timestamp == timestamp, cancellationToken);

                long messageId;
                if (existingMessage != null)
                {
                    messageId = existingMessage.MessageId;
                }
                else
                {
                    // Generate next negative ID for synthetic messages
                    var minMessageId = await _context.Messages
                        .Where(m => m.MessageId < 0)
                        .MinAsync(m => (long?)m.MessageId, cancellationToken) ?? 0;

                    messageId = minMessageId - 1;

                    // Insert new message
                    var newMessage = new MessageRecord
                    {
                        MessageId = messageId,
                        ChatId = chatIdLong,
                        UserId = -1,
                        UserName = "Manual",
                        Timestamp = timestamp,
                        MessageText = messageText,
                        ContentHash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(messageText))
                            .Aggregate("", (current, b) => current + b.ToString("x2"))
                    };

                    _context.Messages.Add(newMessage);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                // Step 2: Insert detection_result
                var detectionResult = new DetectionResultRecord
                {
                    MessageId = messageId,
                    DetectedAt = timestamp,
                    DetectionSource = source,
                    IsSpam = isSpam,
                    Confidence = confidenceWhenAdded ?? 0,
                    AddedBy = addedBy,
                    DetectionMethod = "Manual",
                    UsedForTraining = true
                };

                _context.DetectionResults.Add(detectionResult);
                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Added detection result: {Type} from {Source} (Detection ID: {Id}, Message ID: {MessageId})",
                    isSpam ? "SPAM" : "HAM", source, detectionResult.Id, messageId);

                return detectionResult.Id;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add detection result: {Text}", messageText);
            throw;
        }
    }

    /// <summary>
    /// Get detection results by source
    /// </summary>
    public async Task<IEnumerable<TrainingSample>> GetSamplesBySourceAsync(string source, CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _context.DetectionResults
                .AsNoTracking()
                .Include(dr => dr.Message)
                .Where(dr => dr.DetectionSource == source)
                .OrderByDescending(dr => dr.DetectedAt)
                .Select(dr => new TrainingSample
                {
                    Id = dr.Id,
                    MessageText = dr.Message!.MessageText!,
                    IsSpam = dr.IsSpam,
                    AddedDate = dr.DetectedAt,
                    Source = dr.DetectionSource,
                    ConfidenceWhenAdded = dr.Confidence,
                    ChatIds = new long[] { dr.Message!.ChatId },
                    AddedBy = dr.AddedBy,
                    DetectionCount = 0,
                    LastDetectedDate = null
                })
                .ToListAsync(cancellationToken);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve detection results for source {Source}", source);
            return Enumerable.Empty<TrainingSample>();
        }
    }

    /// <summary>
    /// Delete detection results older than specified date
    /// NOTE: In normalized schema, we generally keep all detection_results for analytics
    /// This should rarely be used
    /// </summary>
    public async Task<int> DeleteOldSamplesAsync(long olderThanUnixTime, CancellationToken cancellationToken = default)
    {
        try
        {
            var oldResults = await _context.DetectionResults
                .Where(dr => dr.DetectedAt < olderThanUnixTime)
                .ToListAsync(cancellationToken);

            var deletedCount = oldResults.Count;
            _context.DetectionResults.RemoveRange(oldResults);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Deleted {Count} old detection results (this removes training data!)", deletedCount);
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete old detection results");
            throw;
        }
    }

    /// <summary>
    /// Update a detection result and its associated message
    /// </summary>
    public async Task<bool> UpdateSampleAsync(long id, string messageText, bool isSpam, string source, int? confidenceWhenAdded = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Find detection result
                var detectionResult = await _context.DetectionResults
                    .FirstOrDefaultAsync(dr => dr.Id == id, cancellationToken);

                if (detectionResult == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                // Update detection result
                detectionResult.IsSpam = isSpam;
                detectionResult.DetectionSource = source;
                detectionResult.Confidence = confidenceWhenAdded ?? 0;

                // Find and update message
                var message = await _context.Messages
                    .FindAsync(new object[] { detectionResult.MessageId }, cancellationToken);

                if (message != null)
                {
                    message.MessageText = messageText;
                    message.ContentHash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(messageText))
                        .Aggregate("", (current, b) => current + b.ToString("x2"));
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Updated detection result {Id} to {Type}", id, isSpam ? "SPAM" : "HAM");
                return true;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update detection result {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Delete a specific detection result
    /// NOTE: This deletes training data - use with caution!
    /// </summary>
    public async Task<bool> DeleteSampleAsync(long id, CancellationToken cancellationToken = default)
    {
        try
        {
            var detectionResult = await _context.DetectionResults
                .FirstOrDefaultAsync(dr => dr.Id == id, cancellationToken);

            if (detectionResult == null)
                return false;

            _context.DetectionResults.Remove(detectionResult);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Deleted detection result {Id} (training data removed!)", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete detection result {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Get detection statistics
    /// </summary>
    public async Task<TrainingStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allResults = await _context.DetectionResults
                .AsNoTracking()
                .Select(dr => new { dr.IsSpam, dr.DetectionSource })
                .ToListAsync(cancellationToken);

            var total = allResults.Count;
            var spam = allResults.Count(r => r.IsSpam);
            var ham = total - spam;
            var spamPercentage = total > 0 ? (double)spam / total * 100 : 0;

            var sourceDict = allResults
                .GroupBy(r => r.DetectionSource)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());

            return new TrainingStats
            {
                TotalSamples = total,
                SpamSamples = spam,
                HamSamples = ham,
                SpamPercentage = spamPercentage,
                SamplesBySource = sourceDict
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get training statistics");
            return new TrainingStats
            {
                TotalSamples = 0,
                SpamSamples = 0,
                HamSamples = 0,
                SpamPercentage = 0,
                SamplesBySource = new Dictionary<string, int>()
            };
        }
    }
}

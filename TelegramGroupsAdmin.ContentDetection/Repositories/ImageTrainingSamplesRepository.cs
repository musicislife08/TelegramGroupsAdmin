using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing image training samples (ML-5)
/// Consolidated read/write operations for spam detection training data
/// </summary>
public class ImageTrainingSamplesRepository : IImageTrainingSamplesRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IPhotoHashService _photoHashService;
    private readonly ILogger<ImageTrainingSamplesRepository> _logger;

    public ImageTrainingSamplesRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        IPhotoHashService photoHashService,
        ILogger<ImageTrainingSamplesRepository> logger)
    {
        _contextFactory = contextFactory;
        _photoHashService = photoHashService;
        _logger = logger;
    }

    /// <summary>
    /// Get recent image training samples with their photo hashes
    /// Returns samples ordered by most recent first
    /// </summary>
    public async Task<List<ImageTrainingSample>> GetRecentSamplesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var samples = await context.ImageTrainingSamples
            .AsNoTracking()
            // A NULL or wrong-length hash means the source image is gone (or, after a
            // migration rollback, was backfilled with a zero-length placeholder) and cannot be
            // compared. Comparing against it would throw in CompareHashes.
            .Where(its => its.PhotoHash != null && its.PhotoHash.Length == HashingConstants.PhotoHashByteCount)
            .OrderByDescending(its => its.MarkedAt)
            .Take(limit)
            .Select(its => new { its.PhotoHash, its.IsSpam })
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Retrieved {Count} image training samples for hash comparison", samples.Count);

        return samples.Select(s => new ImageTrainingSample(s.PhotoHash!, s.IsSpam)).ToList();
    }

    /// <summary>
    /// Save an image training sample from a labeled message
    /// Computes photo hash and stores with spam/ham label
    /// </summary>
    public async Task<bool> SaveTrainingSampleAsync(
        int messageId,
        long chatId,
        bool isSpam,
        Actor markedBy,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            // Get message to check for photo
            var message = await context.Messages
                .AsNoTracking()
                .Where(m => m.MessageId == messageId && m.ChatId == chatId)
                .Select(m => new
                {
                    m.MessageId,
                    m.PhotoFileId,
                    m.MediaLocalPath
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (message == null)
            {
                _logger.LogWarning("Cannot save image training sample: Message {MessageId} not found", messageId);
                return false;
            }

            // Check if message has a photo with local path
            // Photos are stored in PhotoFileId field, with downloaded file at MediaLocalPath
            if (string.IsNullOrEmpty(message.PhotoFileId) || string.IsNullOrEmpty(message.MediaLocalPath))
            {
                _logger.LogDebug("Message {MessageId} has no photo or local path, skipping image training sample", messageId);
                return false;
            }

            if (!File.Exists(message.MediaLocalPath))
            {
                _logger.LogDebug("Photo file not found at {PhotoPath} for message {MessageId}, cannot save training sample", message.MediaLocalPath, messageId);
                return false;
            }

            var photoPath = message.MediaLocalPath;

            // Compute photo hash
            var photoHash = await _photoHashService.ComputePhotoHashAsync(photoPath);
            if (photoHash == null)
            {
                _logger.LogWarning("Failed to compute photo hash for message {MessageId}, cannot save training sample", messageId);
                return false;
            }

            // Check if training sample already exists for this message
            var existingSample = await context.ImageTrainingSamples
                .Where(its => its.MessageId == messageId && its.ChatId == chatId)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingSample != null)
            {
                _logger.LogDebug("Image training sample already exists for message {MessageId}, skipping duplicate", messageId);
                return false;
            }

            // Create training sample
            var trainingSample = new ImageTrainingSampleDto
            {
                MessageId = messageId,
                ChatId = chatId,
                PhotoHash = photoHash,
                IsSpam = isSpam,
                MarkedAt = DateTimeOffset.UtcNow,
                // Actor System: Set exactly one actor field
                MarkedByWebUserId = markedBy.Type == ActorType.WebUser ? markedBy.WebUserId : null,
                MarkedByTelegramUserId = markedBy.Type == ActorType.TelegramUser ? markedBy.TelegramUserId : null,
                MarkedBySystemIdentifier = markedBy.Type == ActorType.System ? markedBy.SystemIdentifier : null
            };

            context.ImageTrainingSamples.Add(trainingSample);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Saved image training sample for message {MessageId}: {Label} (marked by {ActorType})",
                messageId, isSpam ? "SPAM" : "HAM", markedBy.Type);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save image training sample for message {MessageId}", messageId);
            return false;
        }
    }
}

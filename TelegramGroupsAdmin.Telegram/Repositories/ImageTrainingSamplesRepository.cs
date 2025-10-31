using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing image training samples (ML-5)
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
    /// Save an image training sample from a labeled message
    /// Computes photo hash and stores with spam/ham label
    /// </summary>
    public async Task<bool> SaveTrainingSampleAsync(
        long messageId,
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
                .Where(m => m.MessageId == messageId)
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
                _logger.LogWarning("Photo file not found at {PhotoPath} for message {MessageId}, cannot save training sample", message.MediaLocalPath, messageId);
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
                .Where(its => its.MessageId == messageId)
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

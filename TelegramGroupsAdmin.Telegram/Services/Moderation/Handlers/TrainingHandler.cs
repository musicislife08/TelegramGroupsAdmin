using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Creates ML training data from spam classifications.
/// Called by orchestrator for spam cases only.
/// </summary>
public class TrainingHandler : ITrainingHandler
{
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly IImageTrainingSamplesRepository _imageTrainingSamplesRepository;
    private readonly ILogger<TrainingHandler> _logger;

    public TrainingHandler(
        IMessageHistoryRepository messageHistoryRepository,
        IDetectionResultsRepository detectionResultsRepository,
        IImageTrainingSamplesRepository imageTrainingSamplesRepository,
        ILogger<TrainingHandler> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _detectionResultsRepository = detectionResultsRepository;
        _imageTrainingSamplesRepository = imageTrainingSamplesRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateSpamSampleAsync(
        long messageId,
        Actor executor,
        CancellationToken ct = default)
    {
        // Try to get message from database
        var message = await _messageHistoryRepository.GetMessageAsync(messageId, ct);

        if (message == null)
        {
            _logger.LogWarning(
                "Message {MessageId} not in database. Skipping training data creation.",
                messageId);
            return;
        }

        // Create detection result (manual spam classification)
        var hasText = !string.IsNullOrWhiteSpace(message.MessageText);
        var detectionResult = new DetectionResultRecord
        {
            MessageId = messageId,
            DetectedAt = DateTimeOffset.UtcNow,
            DetectionSource = "manual",
            DetectionMethod = "Manual",
            Confidence = 100,
            Reason = "Marked as spam by moderator",
            AddedBy = executor,
            UserId = message.UserId,
            UsedForTraining = hasText, // Only use text messages for training
            NetConfidence = 100,
            CheckResultsJson = null,
            EditVersion = 0
        };

        await _detectionResultsRepository.InsertWithTrainingInvalidationAsync(messageId, detectionResult, ct);

        _logger.LogInformation(
            "Created training data for message {MessageId} (hasText={HasText}) marked as spam by {Executor}",
            messageId, hasText, executor.GetDisplayText());

        // Save image training sample if message has a photo
        var imageSaved = await _imageTrainingSamplesRepository.SaveTrainingSampleAsync(
            messageId,
            isSpam: true,
            executor,
            ct);

        if (imageSaved)
        {
            _logger.LogInformation(
                "Saved image training sample for message {MessageId}",
                messageId);
        }
    }
}

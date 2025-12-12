using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Events;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Creates ML training data from spam classifications.
/// Order: 50 (runs after threshold checks, before audit log)
/// </summary>
public class TrainingDataHandler : IModerationHandler
{
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly IImageTrainingSamplesRepository _imageTrainingSamplesRepository;
    private readonly IMessageBackfillService _messageBackfillService;
    private readonly ILogger<TrainingDataHandler> _logger;

    public int Order => 50;

    public ModerationActionType[] AppliesTo => [ModerationActionType.MarkAsSpamAndBan];

    public TrainingDataHandler(
        IMessageHistoryRepository messageHistoryRepository,
        IDetectionResultsRepository detectionResultsRepository,
        IImageTrainingSamplesRepository imageTrainingSamplesRepository,
        IMessageBackfillService messageBackfillService,
        ILogger<TrainingDataHandler> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _detectionResultsRepository = detectionResultsRepository;
        _imageTrainingSamplesRepository = imageTrainingSamplesRepository;
        _messageBackfillService = messageBackfillService;
        _logger = logger;
    }

    public async Task<ModerationFollowUp> HandleAsync(ModerationEvent evt, CancellationToken ct = default)
    {
        if (!evt.MessageId.HasValue)
        {
            _logger.LogDebug("No message ID provided for training data, skipping");
            return ModerationFollowUp.None;
        }

        var messageId = evt.MessageId.Value;

        // Try to get message from database
        var message = await _messageHistoryRepository.GetMessageAsync(messageId, ct);

        if (message == null && evt.TelegramMessage != null && evt.ChatId.HasValue)
        {
            // Message not in database - try to backfill from Telegram object
            var backfilled = await _messageBackfillService.BackfillIfMissingAsync(
                messageId,
                evt.ChatId.Value,
                evt.TelegramMessage,
                ct);

            if (backfilled)
            {
                message = await _messageHistoryRepository.GetMessageAsync(messageId, ct);
            }
        }

        if (message == null)
        {
            _logger.LogWarning(
                "Message {MessageId} not in database and could not be backfilled. Skipping training data for user {UserId}",
                messageId, evt.UserId);
            return ModerationFollowUp.None;
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
            Reason = evt.Reason,
            AddedBy = evt.Executor,
            UserId = evt.UserId,
            UsedForTraining = hasText, // Only use text messages for training
            NetConfidence = 100,
            CheckResultsJson = null,
            EditVersion = 0
        };

        await _detectionResultsRepository.InsertWithTrainingInvalidationAsync(messageId, detectionResult, ct);

        _logger.LogInformation(
            "Created training data for message {MessageId} (hasText={HasText}) marked as spam by {Executor}",
            messageId, hasText, evt.Executor.GetDisplayText());

        // Save image training sample if message has a photo
        var imageSaved = await _imageTrainingSamplesRepository.SaveTrainingSampleAsync(
            messageId,
            isSpam: true,
            evt.Executor,
            ct);

        if (imageSaved)
        {
            _logger.LogInformation(
                "Saved image training sample for message {MessageId}",
                messageId);
        }

        return ModerationFollowUp.None;
    }
}

using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
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
    private readonly ITrainingLabelsRepository _trainingLabelsRepository;
    private readonly IImageTrainingSamplesRepository _imageTrainingSamplesRepository;
    private readonly IJobTriggerService _jobTriggerService;
    private readonly ILogger<TrainingHandler> _logger;

    public TrainingHandler(
        IMessageHistoryRepository messageHistoryRepository,
        IDetectionResultsRepository detectionResultsRepository,
        ITrainingLabelsRepository trainingLabelsRepository,
        IImageTrainingSamplesRepository imageTrainingSamplesRepository,
        IJobTriggerService jobTriggerService,
        ILogger<TrainingHandler> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _detectionResultsRepository = detectionResultsRepository;
        _trainingLabelsRepository = trainingLabelsRepository;
        _imageTrainingSamplesRepository = imageTrainingSamplesRepository;
        _jobTriggerService = jobTriggerService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateSpamSampleAsync(
        long messageId,
        Actor executor,
        CancellationToken cancellationToken = default)
    {
        // Try to get message from database
        var message = await _messageHistoryRepository.GetMessageAsync(messageId, cancellationToken);

        if (message == null)
        {
            _logger.LogWarning(
                "Message {MessageId} not in database. Skipping training data creation.",
                messageId);
            return;
        }

        // Create detection result for history (NOT for training - use training_labels instead)
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
            UsedForTraining = false, // History only - training handled by training_labels table
            NetConfidence = 100,
            CheckResultsJson = null,
            EditVersion = 0
        };

        await _detectionResultsRepository.InsertAsync(detectionResult, cancellationToken);

        // Create explicit training label for ML (spam)
        if (hasText)
        {
            await _trainingLabelsRepository.UpsertLabelAsync(
                messageId,
                label: TrainingLabel.Spam,
                labeledByUserId: executor.GetTelegramUserId(), // Null if executor is web user or system
                reason: "Marked as spam by moderator",
                auditLogId: null,
                cancellationToken: cancellationToken);

            // Trigger ML text classifier retraining (immediate, no payload)
            await _jobTriggerService.TriggerNowAsync(
                BackgroundJobNames.TextClassifierRetraining,
                payload: new { }, // Empty payload - job doesn't need parameters
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Created training label and triggered retraining for message {MessageId} marked as spam by {Executor}",
                messageId, executor.GetDisplayText());
        }
        else
        {
            _logger.LogInformation(
                "Created detection result for message {MessageId} marked as spam by {Executor} (no text, skipped training)",
                messageId, executor.GetDisplayText());
        }

        // Save image training sample if message has a photo
        var imageSaved = await _imageTrainingSamplesRepository.SaveTrainingSampleAsync(
            messageId,
            isSpam: true,
            executor,
            cancellationToken);

        if (imageSaved)
        {
            _logger.LogInformation(
                "Saved image training sample for message {MessageId}",
                messageId);
        }
    }
}

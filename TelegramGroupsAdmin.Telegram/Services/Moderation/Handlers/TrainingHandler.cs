using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

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
    private readonly IVideoTrainingSamplesRepository _videoTrainingSamplesRepository;
    private readonly ITelegramMediaService _telegramMediaService;
    private readonly IJobTriggerService _jobTriggerService;
    private readonly ILogger<TrainingHandler> _logger;

    public TrainingHandler(
        IMessageHistoryRepository messageHistoryRepository,
        IDetectionResultsRepository detectionResultsRepository,
        ITrainingLabelsRepository trainingLabelsRepository,
        IImageTrainingSamplesRepository imageTrainingSamplesRepository,
        IVideoTrainingSamplesRepository videoTrainingSamplesRepository,
        ITelegramMediaService telegramMediaService,
        IJobTriggerService jobTriggerService,
        ILogger<TrainingHandler> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _detectionResultsRepository = detectionResultsRepository;
        _trainingLabelsRepository = trainingLabelsRepository;
        _imageTrainingSamplesRepository = imageTrainingSamplesRepository;
        _videoTrainingSamplesRepository = videoTrainingSamplesRepository;
        _telegramMediaService = telegramMediaService;
        _jobTriggerService = jobTriggerService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateSpamSampleAsync(
        int messageId,
        ChatIdentity chat,
        Actor executor,
        CancellationToken cancellationToken = default)
    {
        // Try to get message from database
        var message = await _messageHistoryRepository.GetMessageAsync(messageId, chat.Id, cancellationToken);

        if (message == null)
        {
            _logger.LogWarning(
                "Message {MessageId} not in database. Skipping training data creation.",
                messageId);
            return;
        }

        // Create detection result for history — only for manual moderator actions.
        // Auto-detected spam already has a detection_result from the content detection pipeline;
        // inserting a second "manual" entry would corrupt the notification reason and timeline.
        var hasText = !string.IsNullOrWhiteSpace(message.MessageText);
        if (executor.Type != ActorType.System)
        {
            var detectionResult = new DetectionResultRecord
            {
                MessageId = messageId,
                ChatId = chat.Id,
                DetectedAt = DateTimeOffset.UtcNow,
                DetectionSource = SpamDetectionConstants.ManualDetectionSource,
                DetectionMethod = SpamDetectionConstants.ManualDetectionMethod,
                Score = 5.0,
                Reason = SpamDetectionConstants.ManualSpamReason,
                AddedBy = executor,
                UserId = message.User.Id,
                UsedForTraining = false, // History only - training handled by training_labels table
                NetScore = 5.0,
                CheckResultsJson = null,
                EditVersion = 0
            };

            await _detectionResultsRepository.InsertAsync(detectionResult, cancellationToken);
        }

        // Create explicit training label for ML (spam)
        if (hasText)
        {
            var labelReason = executor.Type == ActorType.System
                ? SpamDetectionConstants.AutoDetectedSpamReason
                : SpamDetectionConstants.ManualSpamReason;

            await _trainingLabelsRepository.UpsertLabelAsync(
                messageId,
                chat.Id,
                label: TrainingLabel.Spam,
                labeledByUserId: executor.GetTelegramUserId(), // Null if executor is web user or system
                reason: labelReason,
                auditLogId: null,
                cancellationToken: cancellationToken);

            // Trigger combined classifier retraining (SDCA + Bayes, immediate, no payload)
            await _jobTriggerService.TriggerNowAsync(
                BackgroundJobNames.ClassifierRetraining,
                payload: new { },
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Created training label and triggered retraining for message {MessageId} marked as spam by {Executor}",
                messageId, executor.GetDisplayText());
        }
        else
        {
            _logger.LogInformation(
                "Message {MessageId} has no text; skipped training label for {Executor}",
                messageId, executor.GetDisplayText());
        }

        // Defensive download: if message has a file ID but no local path, download now.
        // This handles edge cases where the original download failed, file was cleaned, or expired.
        if (message.MediaLocalPath == null)
        {
            var fileId = message.PhotoFileId ?? message.MediaFileId;
            var mediaType = message.PhotoFileId != null ? MediaType.Photo : message.MediaType;

            if (!string.IsNullOrEmpty(fileId) && mediaType.HasValue)
            {
                try
                {
                    var localPath = await _telegramMediaService.DownloadAndSaveMediaAsync(
                        fileId,
                        mediaType.Value,
                        message.MediaFileName,
                        chat.Id,
                        messageId,
                        cancellationToken);

                    if (localPath != null)
                    {
                        _logger.LogInformation(
                            "Downloaded missing media for message {MessageId} before training sample creation",
                            messageId);
                        await _messageHistoryRepository.UpdateMediaLocalPathAsync(
                            messageId,
                            chat.Id,
                            localPath,
                            cancellationToken);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Could not download media for message {MessageId} (file may have expired on Telegram servers)",
                            messageId);
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal: log at Debug and continue. Training sample save will gracefully return false.
                    _logger.LogDebug(ex,
                        "Failed to download media for message {MessageId}, continuing without media",
                        messageId);
                }
            }
        }

        // Save image training sample if message has a photo
        var imageSaved = await _imageTrainingSamplesRepository.SaveTrainingSampleAsync(
            messageId,
            chat.Id,
            isSpam: true,
            executor,
            cancellationToken);

        if (imageSaved)
        {
            _logger.LogInformation(
                "Saved image training sample for message {MessageId}",
                messageId);
        }

        // Save video training sample if message has a video
        var videoSaved = await _videoTrainingSamplesRepository.SaveTrainingSampleAsync(
            messageId,
            chat.Id,
            isSpam: true,
            executor,
            cancellationToken);

        if (videoSaved)
        {
            _logger.LogInformation(
                "Saved video training sample for message {MessageId}",
                messageId);
        }
    }
}

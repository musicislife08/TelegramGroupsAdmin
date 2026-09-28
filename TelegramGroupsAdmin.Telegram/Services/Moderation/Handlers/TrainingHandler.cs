using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Records spam and ham decisions as verdict events and creates ML training data.
/// Media similarity samples need no write here: Layer 1 reads messages' media features joined to
/// their current verdict.
/// </summary>
public class TrainingHandler : ITrainingHandler
{
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly ITelegramMediaService _telegramMediaService;
    private readonly IJobTriggerService _jobTriggerService;
    private readonly ILogger<TrainingHandler> _logger;

    public TrainingHandler(
        IMessageHistoryRepository messageHistoryRepository,
        IDetectionResultsRepository detectionResultsRepository,
        ITelegramMediaService telegramMediaService,
        IJobTriggerService jobTriggerService,
        ILogger<TrainingHandler> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _detectionResultsRepository = detectionResultsRepository;
        _telegramMediaService = telegramMediaService;
        _jobTriggerService = jobTriggerService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateSpamSampleAsync(
        int messageId,
        ChatIdentity chat,
        Actor executor,
        VerdictSource source,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (source is not (VerdictSource.AutoBan or VerdictSource.WebMarkSpam or VerdictSource.SpamCommand or VerdictSource.ReviewSpam))
            throw new ArgumentOutOfRangeException(nameof(source), source,
                "Spam decisions are AutoBan, WebMarkSpam, SpamCommand or ReviewSpam");

        // Try to get message from database
        var message = await _messageHistoryRepository.GetMessageAsync(messageId, chat.Id, cancellationToken);

        if (message == null)
        {
            _logger.LogWarning(
                "Message {MessageId} not in database. Skipping training data creation.",
                messageId);
            return;
        }

        // Every spam decision is a verdict event, auto-bans (System executor) included.
        var hasText = !string.IsNullOrWhiteSpace(message.MessageText);
        await _detectionResultsRepository.RecordDecisionAsync(
            messageId, chat.Id, source, executor, reason, cancellationToken: cancellationToken);

        // The text classifiers train on message_verdicts; retrain when the message has text to learn from.
        if (hasText)
        {
            // Trigger combined classifier retraining (SDCA + Bayes, immediate, no payload)
            await _jobTriggerService.TriggerNowAsync(
                BackgroundJobNames.ClassifierRetraining,
                payload: new { },
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Triggered retraining for message {MessageId} marked as spam by {Executor}",
                messageId, executor.GetDisplayText());
        }
        else
        {
            _logger.LogInformation(
                "Message {MessageId} has no text; skipped retraining for {Executor}",
                messageId, executor.GetDisplayText());
        }

        // Defensive download: if message has a file ID but no local path, download now.
        // This handles edge cases where the original download failed, file was cleaned, or expired.
        // The message now carries a curated verdict, so the startup media-feature backfill
        // (PhotoHashRehashService) hashes the downloaded media on the next start.
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
                            "Downloaded missing media for message {MessageId} for media-feature backfill",
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
                    // Non-fatal: log at Debug and continue; the decision is already recorded.
                    _logger.LogDebug(ex,
                        "Failed to download media for message {MessageId}, continuing without media",
                        messageId);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task CreateHamSampleAsync(int messageId, ChatIdentity chat, Actor executor, VerdictSource source,
        string reason, CancellationToken cancellationToken = default)
    {
        if (source is not (VerdictSource.WebMarkHam or VerdictSource.ReviewDismiss))
            throw new ArgumentOutOfRangeException(nameof(source), source, "Ham decisions are WebMarkHam or ReviewDismiss");

        // detection_results has an FK to messages: an unstored or retention-deleted message cannot carry a verdict.
        var message = await _messageHistoryRepository.GetMessageAsync(messageId, chat.Id, cancellationToken);
        if (message == null)
        {
            _logger.LogWarning(
                "Message {MessageId} not in database. Skipping {Source} ham decision.",
                messageId, source);
            return;
        }

        // A dismissed review is only an implicit judgement: it must not overturn an explicit label
        // (e.g. a stale report dismissed after the message was already banned as spam).
        if (source == VerdictSource.ReviewDismiss)
        {
            var current = await _detectionResultsRepository.GetCurrentVerdictAsync(messageId, chat.Id, cancellationToken);
            if (current != null && current.Classification.IsExplicit())
            {
                _logger.LogInformation(
                    "Skipped {Source} ham decision for message {MessageId} by {Executor}: current verdict {Classification} is explicit and a dismissal does not override it",
                    source, messageId, executor.GetDisplayText(), current.Classification);
                return;
            }
        }

        await _detectionResultsRepository.RecordDecisionAsync(
            messageId, chat.Id, source, executor, reason, cancellationToken: cancellationToken);

        await _jobTriggerService.TriggerNowAsync(
            BackgroundJobNames.ClassifierRetraining, payload: new { }, cancellationToken: cancellationToken);

        _logger.LogInformation("Recorded {Source} ham decision for message {MessageId} by {Executor}",
            source, messageId, executor.GetDisplayText());
    }
}

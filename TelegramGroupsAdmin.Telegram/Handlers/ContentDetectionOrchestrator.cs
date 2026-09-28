using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Media;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Orchestrates content detection workflow for messages.
/// Coordinates content checks, critical violations, detection result storage, auto-trust, and spam actions.
/// </summary>
public class ContentDetectionOrchestrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DetectionActionService _spamActionService;
    private readonly ILogger<ContentDetectionOrchestrator> _logger;

    public ContentDetectionOrchestrator(
        IServiceProvider serviceProvider,
        DetectionActionService spamActionService,
        ILogger<ContentDetectionOrchestrator> logger)
    {
        _serviceProvider = serviceProvider;
        _spamActionService = spamActionService;
        _logger = logger;
    }

    /// <summary>
    /// Run spam detection on a message and take appropriate actions.
    /// Handles: critical violations, detection result storage, auto-trust, language warnings, spam actions.
    /// </summary>
    public async Task RunDetectionAsync(
        Message message,
        string? text,
        string? photoLocalPath,
        int editVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Starting content detection for message {MessageId} from {User} in {Chat} (hasText: {HasText}, hasPhoto: {HasPhoto}, edit: {EditVersion})",
                message.MessageId,
                message.From.ToLogDebug(),
                message.Chat.ToLogDebug(),
                !string.IsNullOrWhiteSpace(text),
                !string.IsNullOrEmpty(photoLocalPath),
                editVersion);

            using var scope = _serviceProvider.CreateScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<IContentCheckCoordinator>();
            var detectionResultsRepo = scope.ServiceProvider.GetRequiredService<IDetectionResultsRepository>();
            var appOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.AppOptions>>();

            // Build spam detection request with photo local path if available
            // Note: ImageSpamCheck uses PhotoLocalPath for all 3 layers (hash, OCR, Vision)
            // No need to open a stream here - each layer reads the file as needed
            string? photoFullPath = null;
            if (!string.IsNullOrEmpty(photoLocalPath))
            {
                photoFullPath = Path.Combine(appOptions.Value.DataPath, "media", photoLocalPath);
                if (!File.Exists(photoFullPath))
                {
                    _logger.LogDebug("Photo file not found for spam detection: {PhotoPath}", photoFullPath);
                    photoFullPath = null; // Reset if file doesn't exist

                    // Queue missing photo for re-download (non-fatal if enqueue fails)
                    try
                    {
                        var refetchQueue = scope.ServiceProvider.GetRequiredService<IMediaRefetchQueueService>();
                        await refetchQueue.EnqueueMediaAsync(message.MessageId, message.Chat.Id, MediaType.Photo);
                    }
                    catch (Exception refetchEx)
                    {
                        _logger.LogDebug(refetchEx, "Failed to enqueue photo refetch for message {MessageId}", message.MessageId);
                    }
                }
            }

            var request = new ContentCheckRequest
            {
                Message = text ?? "", // Empty string for image-only messages
                User = UserIdentity.From(message.From!),
                Chat = ChatIdentity.From(message.Chat),
                PhotoLocalPath = photoFullPath, // Pass full for ImageSpamCheck layers
                Metadata = new ContentCheckMetadata
                {
                    IsReplyToChannelPost = message.ReplyToMessage?.SenderChat is not null
                }
            };

            var result = await coordinator.CheckAsync(request, cancellationToken);

            // Phase 4.14: Handle critical check violations FIRST (before regular spam)
            // Critical violations apply to ALL users (trusted/admin included)
            if (result.HasCriticalViolations)
            {
                _logger.LogWarning(
                    "Critical check violations detected for message {MessageId} from {User}: {Violations}",
                    message.MessageId,
                    message.From.ToLogDebug(),
                    string.Join("; ", result.CriticalCheckViolations));

                // Use DetectionActionService to handle critical violations
                // Policy: Delete + DM notice, NO ban/warn for trusted/admin users
                await _spamActionService.HandleCriticalCheckViolationAsync(
                    message,
                    result.CriticalCheckViolations,
                    cancellationToken);

                // If critical violations found, don't process regular spam (already handled)
                return;
            }

            // Store detection result (spam or ham) for analytics and training
            // Only store if spam detection actually ran (not skipped for trusted/admin users)
            if (!result.SpamCheckSkipped && result.SpamResult != null)
            {
                var detectionResult = await StoreDetectionResultAsync(
                    detectionResultsRepo,
                    message,
                    result.SpamResult,
                    editVersion,
                    cancellationToken);

                // Media features computed by the image/video checks live on the message, so Layer 1
                // can compare later media against it once it carries a training verdict. A missing
                // file means no check computed features, and nothing is stored.
                var mediaFeatures = result.SpamResult.CheckResults
                    .Select(c => c.MediaFeatures)
                    .FirstOrDefault(f => f is not null);
                if (mediaFeatures is not null)
                {
                    var messageHistory = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
                    await messageHistory.SetMediaFeaturesAsync(message.MessageId, message.Chat.Id, mediaFeatures, cancellationToken);
                }

                // Check for auto-trust after storing non-spam detection result
                if (!result.SpamResult.IsSpam && message.From != null)
                {
                    var autoTrustService = scope.ServiceProvider.GetRequiredService<UserAutoTrustService>();
                    await autoTrustService.CheckAndApplyAutoTrustAsync(message.From, message.Chat, cancellationToken);
                }

                // Phase 4.21: Language warning for non-English non-spam messages from untrusted users
                // Note: Language detection happens earlier in ProcessNewMessageAsync, check translation there
                if (!result.SpamResult.IsSpam && message.From?.Id != null)
                {
                    // Language warning is handled by LanguageWarningHandler (REFACTOR-2 Phase 2.2)
                    // This will be extracted to handler in next phase
                    var languageWarningHandler = scope.ServiceProvider.GetRequiredService<LanguageWarningHandler>();
                    await languageWarningHandler.HandleWarningAsync(message, scope, cancellationToken);
                }

                // Phase 2.7: Handle spam actions based on net confidence
                await _spamActionService.HandleSpamDetectionActionsAsync(
                    message,
                    result.SpamResult,
                    detectionResult,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run content detection for message {MessageId}", message.MessageId);
        }
    }

    /// <summary>
    /// Store the scan as a verdict event. The classification is decided once, by VerdictClassifier,
    /// inside the repository.
    /// </summary>
    private async Task<DetectionResultRecord> StoreDetectionResultAsync(
        IDetectionResultsRepository detectionResultsRepo,
        Message message,
        ContentDetectionResult spamResult,
        int editVersion,
        CancellationToken cancellationToken)
    {
        var detectionResult = await detectionResultsRepo.RecordScanAsync(
            message.MessageId, message.Chat.Id, spamResult, editVersion, cancellationToken);

        _logger.LogDebug(
            "Stored {Classification} verdict for message {MessageId} (edit {EditVersion}, score {Score:F2})",
            detectionResult.Classification, message.MessageId, editVersion, spamResult.TotalScore);

        return detectionResult;
    }
}

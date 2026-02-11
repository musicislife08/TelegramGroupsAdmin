using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.ContentDetection.Utilities;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Orchestrates content detection workflow for messages.
/// Coordinates content checks, critical violations, detection result storage, auto-trust, and spam actions.
/// </summary>
public class ContentDetectionOrchestrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DetectionActionService _spamActionService;
    private readonly SimHashService _simHashService;
    private readonly ILogger<ContentDetectionOrchestrator> _logger;

    public ContentDetectionOrchestrator(
        IServiceProvider serviceProvider,
        DetectionActionService spamActionService,
        SimHashService simHashService,
        ILogger<ContentDetectionOrchestrator> logger)
    {
        _serviceProvider = serviceProvider;
        _spamActionService = spamActionService;
        _simHashService = simHashService;
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
            var historyOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.MessageHistoryOptions>>();

            // Build spam detection request with photo local path if available
            // Note: ImageSpamCheck uses PhotoLocalPath for all 3 layers (hash, OCR, Vision)
            // No need to open a stream here - each layer reads the file as needed
            string? photoFullPath = null;
            if (!string.IsNullOrEmpty(photoLocalPath))
            {
                photoFullPath = Path.Combine(historyOptions.Value.ImageStoragePath, "media", photoLocalPath);
                if (!File.Exists(photoFullPath))
                {
                    _logger.LogWarning("Photo file not found for spam detection: {PhotoPath}", photoFullPath);
                    photoFullPath = null; // Reset if file doesn't exist
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
                    text, // Pass analyzed text for deduplication check
                    editVersion,
                    cancellationToken);

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
    /// Store spam detection result to database for analytics and training.
    /// Phase 4.23 (#168): Auto-deduplicates training samples at insert time using SimHash.
    /// </summary>
    private async Task<DetectionResultRecord> StoreDetectionResultAsync(
        IDetectionResultsRepository detectionResultsRepo,
        Message message,
        ContentDetectionResult spamResult,
        string? analyzedText,
        int editVersion,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageHistoryRepo = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();

        var reasonPrefix = editVersion > 0 ? $"[Edit #{editVersion}] " : "";
        var isTrainingWorthy = DetermineIfTrainingWorthy(spamResult);

        // Phase 4.23 (#168): Auto-deduplicate training samples at insert time using SimHash
        // Only check for auto-detected samples that would be used for training
        // Manual samples (/spam command) bypass deduplication (admin intent)
        if (isTrainingWorthy && !string.IsNullOrWhiteSpace(analyzedText))
        {
            var hash = _simHashService.ComputeHash(analyzedText);
            var isDuplicate = await messageHistoryRepo.HasSimilarTrainingHashAsync(
                hash,
                spamResult.IsSpam,
                SimHashService.DefaultMaxDistance,
                cancellationToken);

            if (isDuplicate)
            {
                isTrainingWorthy = false;
                _logger.LogDebug(
                    "Skipping training for message {MessageId}: SimHash within {MaxDistance} bits of existing {Class} sample",
                    message.MessageId,
                    SimHashService.DefaultMaxDistance,
                    spamResult.IsSpam ? "spam" : "ham");
            }
        }

        var detectionResult = new DetectionResultRecord
        {
            MessageId = message.MessageId,
            DetectedAt = DateTimeOffset.UtcNow,
            DetectionSource = "auto",
            DetectionMethod = spamResult.CheckResults.Count > 0
                ? string.Join(", ", spamResult.CheckResults.Select(c => c.CheckName))
                : "Unknown",
            // IsSpam is computed from net_confidence (don't set it here)
            Confidence = spamResult.MaxConfidence,
            Reason = $"{reasonPrefix}{spamResult.PrimaryReason}",
            AddedBy = Actor.AutoDetection, // Phase 4.19: Actor system
            UsedForTraining = isTrainingWorthy,
            NetConfidence = spamResult.NetConfidence,
            CheckResultsJson = CheckResultsSerializer.Serialize(spamResult.CheckResults),
            EditVersion = editVersion
        };

        await detectionResultsRepo.InsertAsync(detectionResult, cancellationToken);

        var editInfo = editVersion > 0 ? $" (edit #{editVersion})" : "";
        _logger.LogDebug(
            "Stored detection result for message {MessageId}{EditInfo}: {IsSpam} (net: {NetConfidence}, training: {UsedForTraining})",
            message.MessageId,
            editInfo,
            spamResult.IsSpam ? "spam" : "ham",
            spamResult.NetConfidence,
            detectionResult.UsedForTraining);

        return detectionResult;
    }

    /// <summary>
    /// Determine if detection result should be used for training.
    /// High-quality samples only: Confident OpenAI results (85%+) or manual admin decisions.
    /// Low-confidence auto-detections are NOT training-worthy.
    /// </summary>
    private static bool DetermineIfTrainingWorthy(ContentDetectionResult result)
    {
        // Manual admin decisions are always training-worthy (will be set when admin uses Mark as Spam/Ham)
        // For auto-detections, only confident results are training-worthy

        // Check if OpenAI was involved and was confident (85%+ confidence)
        var openAIResult = result.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
        if (openAIResult != null)
        {
            // OpenAI confident (85%+) = training-worthy
            return openAIResult.Confidence >= SpamDetectionConstants.OpenAIConfidentThreshold;
        }

        // No OpenAI veto = borderline/uncertain detection
        // Only use for training if net confidence is very high (>80)
        // This prevents low-quality auto-detections from polluting training data
        return result.NetConfidence > SpamDetectionConstants.TrainingConfidenceThreshold;
    }
}

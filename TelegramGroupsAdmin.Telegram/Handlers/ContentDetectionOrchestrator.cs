using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.ContentDetection.Utilities;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
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
    private readonly SpamActionService _spamActionService;
    private readonly ILogger<ContentDetectionOrchestrator> _logger;

    public ContentDetectionOrchestrator(
        IServiceProvider serviceProvider,
        SpamActionService spamActionService,
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
        ITelegramBotClient botClient,
        Message message,
        string? text,
        string? photoLocalPath,
        int editVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<IContentCheckCoordinator>();
            var detectionResultsRepo = scope.ServiceProvider.GetRequiredService<IDetectionResultsRepository>();
            var historyOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramGroupsAdmin.Configuration.MessageHistoryOptions>>();

            // Build spam detection request
            Stream? imageStream = null;
            try
            {
                // If photo exists, read from disk for OpenAI Vision analysis
                if (!string.IsNullOrEmpty(photoLocalPath))
                {
                    var fullPath = Path.Combine(historyOptions.Value.ImageStoragePath, "media", photoLocalPath);
                    if (File.Exists(fullPath))
                    {
                        imageStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    }
                    else
                    {
                        _logger.LogWarning("Photo file not found for spam detection: {PhotoPath}", fullPath);
                    }
                }

                var request = new ContentCheckRequest
                {
                    Message = text ?? "", // Empty string for image-only messages
                    UserId = message.From?.Id ?? 0,
                    UserName = message.From?.Username,
                    ChatId = message.Chat.Id,
                    ImageData = imageStream
                };

                var result = await coordinator.CheckAsync(request, cancellationToken);

                // Phase 4.14: Handle critical check violations FIRST (before regular spam)
                // Critical violations apply to ALL users (trusted/admin included)
                if (result.HasCriticalViolations)
                {
                    _logger.LogWarning(
                        "Critical check violations detected for message {MessageId} from user {UserId}: {Violations}",
                        message.MessageId,
                        message.From?.Id,
                        string.Join("; ", result.CriticalCheckViolations));

                    // Use SpamActionService to handle critical violations
                    // Policy: Delete + DM notice, NO ban/warn for trusted/admin users
                    await _spamActionService.HandleCriticalCheckViolationAsync(
                        botClient,
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

                    // Check for auto-trust after storing non-spam detection result
                    if (!result.SpamResult.IsSpam && message.From?.Id != null)
                    {
                        var autoTrustService = scope.ServiceProvider.GetRequiredService<UserAutoTrustService>();
                        await autoTrustService.CheckAndApplyAutoTrustAsync(message.From.Id, message.Chat.Id, cancellationToken);
                    }

                    // Phase 4.21: Language warning for non-English non-spam messages from untrusted users
                    // Note: Language detection happens earlier in ProcessNewMessageAsync, check translation there
                    if (!result.SpamResult.IsSpam && message.From?.Id != null)
                    {
                        // Language warning is handled by LanguageWarningHandler (REFACTOR-2 Phase 2.2)
                        // This will be extracted to handler in next phase
                        var languageWarningHandler = scope.ServiceProvider.GetRequiredService<LanguageWarningHandler>();
                        await languageWarningHandler.HandleWarningAsync(botClient, message, scope, cancellationToken);
                    }

                    // Phase 2.7: Handle spam actions based on net confidence
                    await _spamActionService.HandleSpamDetectionActionsAsync(
                        message,
                        result.SpamResult,
                        detectionResult,
                        cancellationToken);
                }
            }
            finally
            {
                // Clean up image stream
                if (imageStream != null)
                {
                    await imageStream.DisposeAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run content detection for message {MessageId}", message.MessageId);
        }
    }

    /// <summary>
    /// Store spam detection result to database for analytics and training.
    /// </summary>
    private async Task<DetectionResultRecord> StoreDetectionResultAsync(
        IDetectionResultsRepository detectionResultsRepo,
        Message message,
        ContentDetectionResult spamResult,
        int editVersion,
        CancellationToken cancellationToken)
    {
        var reasonPrefix = editVersion > 0 ? $"[Edit #{editVersion}] " : "";

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
            UsedForTraining = SpamActionService.DetermineIfTrainingWorthy(spamResult),
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
}

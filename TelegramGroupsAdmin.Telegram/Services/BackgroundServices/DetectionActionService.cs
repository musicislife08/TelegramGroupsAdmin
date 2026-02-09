using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Handles content detection actions: routes moderation through BotModerationService.
/// Responsible for:
/// - Loading detection config thresholds
/// - Routing hard block, malware, spam, and critical violations to orchestrator
/// - Creating borderline reports for admin review
/// </summary>
public class DetectionActionService(
    IServiceProvider serviceProvider,
    ILogger<DetectionActionService> logger)
{
    /// <summary>
    /// Load effective content detection config for a chat (with fallback to defaults).
    /// Resolves ConfigService from scope since this is a Singleton service and services are Scoped.
    /// </summary>
    private async Task<ContentDetectionConfig> GetConfigAsync(Chat chat, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
            return await configService.GetEffectiveAsync<ContentDetectionConfig>(ConfigType.ContentDetection, chat.Id)
                   ?? new ContentDetectionConfig();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load content detection config for {Chat}, using defaults", chat.ToLogDebug());
            return new ContentDetectionConfig();
        }
    }

    /// <summary>
    /// Handle content detection actions based on violation type and confidence levels.
    /// Routes all moderation actions through BotModerationService for consistent handling.
    /// - HardBlock → MarkAsSpamAndBanAsync (instant policy violation)
    /// - Malware → HandleMalwareViolationAsync (delete + alert, no ban)
    /// - Spam (high confidence + OpenAI confirmed) → MarkAsSpamAndBanAsync
    /// - Spam (borderline or uncertain) → Create report for admin review
    /// Thresholds are loaded from database config (ContentDetectionConfig).
    /// </summary>
    public async Task HandleSpamDetectionActionsAsync(
        Message message,
        TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult spamResult,
        DetectionResultRecord detectionResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Load thresholds from database config (admin-configurable per chat)
            var config = await GetConfigAsync(message.Chat, cancellationToken);

            // Only take action if spam was detected
            if (!spamResult.IsSpam || spamResult.NetConfidence <= config.ReviewQueueThreshold)
            {
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
            var moderationOrchestrator = scope.ServiceProvider.GetRequiredService<IBotModerationService>();

            // Check for hard block or malware (different handling than spam)
            var hardBlockResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.UrlBlocklist);
            var malwareResult = spamResult.CheckResults.FirstOrDefault(c => c.Result == ContentDetection.Models.CheckResultType.Malware);

            if (hardBlockResult != null)
            {
                // Hard block = instant policy violation (no OpenAI veto needed)
                logger.LogWarning(
                    "Hard block for message {MessageId} from {User} in {Chat}: {Reason}",
                    message.MessageId,
                    LogDisplayName.UserDebug(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                    LogDisplayName.ChatDebug(message.Chat.Title, message.Chat.Id),
                    hardBlockResult.Details);

                await moderationOrchestrator.MarkAsSpamAndBanAsync(
                    new SpamBanIntent
                    {
                        User = UserIdentity.From(message.From!),
                        Chat = ChatIdentity.From(message.Chat),
                        MessageId = message.MessageId,
                        Executor = Actor.AutoDetection,
                        Reason = $"Hard block policy violation: {hardBlockResult.Details}",
                        TelegramMessage = message
                    },
                    cancellationToken);

                return;
            }

            if (malwareResult != null)
            {
                // Malware = delete message + alert admin (no ban - might be accidental)
                logger.LogWarning(
                    "Malware detected in message {MessageId} from {User} in {Chat}: {Details}",
                    message.MessageId,
                    LogDisplayName.UserDebug(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                    LogDisplayName.ChatDebug(message.Chat.Title, message.Chat.Id),
                    malwareResult.Details);

                await moderationOrchestrator.HandleMalwareViolationAsync(
                    new MalwareViolationIntent
                    {
                        User = UserIdentity.From(message.From!),
                        Chat = ChatIdentity.From(message.Chat),
                        MessageId = message.MessageId,
                        Executor = Actor.AutoDetection,
                        Reason = "Malware detected in file",
                        MalwareDetails = malwareResult.Details ?? "Malware detected",
                        TelegramMessage = message
                    },
                    cancellationToken);

                return;
            }

            // Standard spam detection handling
            var openAIResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
            var openAIConfident = openAIResult != null && openAIResult.Confidence >= config.MaxConfidenceVetoThreshold;

            // Skip auto-ban if OpenAI flagged for review
            if (openAIResult?.Result == ContentDetection.Models.CheckResultType.Review)
            {
                var reviewReport = BuildAutoDetectionReport(
                    message,
                    spamResult,
                    detectionResult,
                    $"OpenAI flagged for review - Net: {spamResult.NetConfidence}");

                await reportService.CreateReportAsync(
                    reviewReport,
                    message,
                    isAutomated: true,
                    cancellationToken);

                logger.LogInformation(
                    "Created admin review report for message {MessageId} in {Chat}: OpenAI flagged for human review",
                    message.MessageId,
                    message.Chat.ToLogInfo());
                return;
            }

            if (spamResult.NetConfidence > config.AutoBanThreshold && openAIConfident && openAIResult!.Result == ContentDetection.Models.CheckResultType.Spam)
            {
                // High confidence + OpenAI confirmed = auto-ban
                logger.LogInformation(
                    "Message {MessageId} from {User} in {Chat} triggers auto-ban (net: {NetConfidence}, OpenAI: {OpenAIConf}%)",
                    message.MessageId,
                    LogDisplayName.UserInfo(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                    LogDisplayName.ChatInfo(message.Chat.Title, message.Chat.Id),
                    spamResult.NetConfidence,
                    openAIResult.Confidence);

                await moderationOrchestrator.MarkAsSpamAndBanAsync(
                    new SpamBanIntent
                    {
                        User = UserIdentity.From(message.From!),
                        Chat = ChatIdentity.From(message.Chat),
                        MessageId = message.MessageId,
                        Executor = Actor.AutoDetection,
                        Reason = $"Auto-ban: High confidence spam (Net: {spamResult.NetConfidence}%, OpenAI: {openAIResult.Confidence}%)",
                        TelegramMessage = message
                    },
                    cancellationToken);
            }
            else if (spamResult.NetConfidence > config.ReviewQueueThreshold)
            {
                // Borderline detection OR OpenAI uncertain → Admin review
                var reason = spamResult.NetConfidence > config.AutoBanThreshold
                    ? $"OpenAI uncertain (<{config.MaxConfidenceVetoThreshold}%) - Net: {spamResult.NetConfidence}, OpenAI: {openAIResult?.Confidence ?? 0}%"
                    : $"Borderline detection - Net: {spamResult.NetConfidence}";

                var borderlineReport = BuildAutoDetectionReport(
                    message,
                    spamResult,
                    detectionResult,
                    reason);

                await reportService.CreateReportAsync(
                    borderlineReport,
                    message,
                    isAutomated: true,
                    cancellationToken);

                logger.LogInformation(
                    "Created admin review report for message {MessageId} in {Chat}: {Reason}",
                    message.MessageId,
                    message.Chat.ToLogInfo(),
                    reason);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to handle spam detection actions for message {MessageId}",
                message.MessageId);
        }
    }

    /// <summary>
    /// Build a Report record for auto-detected content with admin notes containing detection details.
    /// </summary>
    private static Report BuildAutoDetectionReport(
        Message message,
        TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult spamResult,
        DetectionResultRecord detectionResult,
        string reason)
    {
        return new Report(
            Id: 0, // Will be assigned by database
            MessageId: message.MessageId,
            Chat: ChatIdentity.From(message.Chat),
            ReportCommandMessageId: null, // Auto-generated report (not from /report command)
            ReportedByUserId: null, // System-generated (not user-reported)
            ReportedByUserName: "Auto-Detection",
            ReportedAt: DateTimeOffset.UtcNow,
            Status: ReportStatus.Pending,
            ReviewedBy: null,
            ReviewedAt: null,
            ActionTaken: null,
            AdminNotes: $$"""
                {{reason}}

                Detection Details:
                {{detectionResult.Reason}}

                Net Confidence: {{spamResult.NetConfidence}}
                Max Confidence: {{spamResult.MaxConfidence}}
                """,
            WebUserId: null // System-generated
        );
    }

    /// <summary>
    /// Handle critical check violations for trusted/admin users.
    /// Policy: Delete message + DM notice, NO ban/warn for trusted/admin users.
    /// Critical checks (URL filtering, file scanning) bypass trust status.
    /// Routes through BotModerationService for consistent handling.
    /// </summary>
    public async Task HandleCriticalCheckViolationAsync(
        Message message,
        List<string> violations,
        CancellationToken cancellationToken = default)
    {
        if (message.From == null)
        {
            logger.LogWarning("Cannot handle critical check violation: message has no sender");
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var moderationOrchestrator = scope.ServiceProvider.GetRequiredService<IBotModerationService>();

            await moderationOrchestrator.HandleCriticalViolationAsync(
                new CriticalViolationIntent
                {
                    User = UserIdentity.From(message.From),
                    Chat = ChatIdentity.From(message.Chat),
                    MessageId = message.MessageId,
                    Executor = Actor.AutoDetection,
                    Reason = "Critical security policy violation",
                    Violations = violations,
                    TelegramMessage = message
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to handle critical check violation for {User} in {Chat}",
                message.From.ToLogDebug(),
                message.Chat.ToLogDebug());
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
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
            if (!spamResult.IsSpam || spamResult.TotalScore <= config.ReviewQueueThreshold)
            {
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
            var moderationOrchestrator = scope.ServiceProvider.GetRequiredService<IBotModerationService>();

            // Check for hard block or malware (different handling than spam)
            var hardBlockResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.UrlBlocklist);
            // Note: Malware is now handled by FileScanJob directly, not through content detection pipeline

            if (hardBlockResult != null)
            {
                // Hard block = instant policy violation (no OpenAI veto needed)
                logger.LogWarning(
                    "Hard block for message {MessageId} from {User} in {Chat}: {Reason}",
                    message.MessageId,
                    message.From.ToLogDebug(),
                    message.Chat.ToLogDebug(),
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

            // Standard spam detection handling
            var openAIResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
            var openAIConfident = openAIResult != null && openAIResult.Score >= config.AutoBanThreshold;

            if (spamResult.TotalScore >= config.AutoBanThreshold && openAIConfident && !openAIResult!.Abstained && openAIResult.Score > 0)
            {
                // High confidence + OpenAI confirmed = auto-ban
                logger.LogInformation(
                    "Message {MessageId} from {User} in {Chat} triggers auto-ban (score: {TotalScore:F2}, OpenAI: {OpenAIScore:F2})",
                    message.MessageId,
                    message.From.ToLogInfo(),
                    message.Chat.ToLogInfo(),
                    spamResult.TotalScore,
                    openAIResult.Score);

                await moderationOrchestrator.MarkAsSpamAndBanAsync(
                    new SpamBanIntent
                    {
                        User = UserIdentity.From(message.From!),
                        Chat = ChatIdentity.From(message.Chat),
                        MessageId = message.MessageId,
                        Executor = Actor.AutoDetection,
                        Reason = $"Auto-ban: High confidence spam (Score: {spamResult.TotalScore:F2}, OpenAI: {openAIResult.Score:F2})",
                        TelegramMessage = message
                    },
                    cancellationToken);
            }
            else if (spamResult.TotalScore > config.ReviewQueueThreshold)
            {
                // Borderline detection OR OpenAI uncertain → Admin review
                var reason = spamResult.TotalScore >= config.AutoBanThreshold
                    ? $"OpenAI uncertain (<{config.AutoBanThreshold:F2}) - Score: {spamResult.TotalScore:F2}, OpenAI: {openAIResult?.Score ?? 0:F2}"
                    : $"Borderline detection - Score: {spamResult.TotalScore:F2}";

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

                Total Score: {{spamResult.TotalScore:F2}}
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

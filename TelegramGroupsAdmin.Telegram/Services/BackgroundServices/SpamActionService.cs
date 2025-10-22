using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;


using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Handles spam detection actions: auto-ban and borderline report creation
/// </summary>
public class SpamActionService(
    IServiceProvider serviceProvider,
    ILogger<SpamActionService> logger)
{
    // Confidence thresholds for spam detection decisions
    private const int AutoBanNetConfidenceThreshold = 50;
    private const int BorderlineNetConfidenceThreshold = 0;
    private const int OpenAIConfidentThreshold = 85;
    /// <summary>
    /// Determine if detection result should be used for training
    /// High-quality samples only: Confident OpenAI results (85%+) or manual admin decisions
    /// Low-confidence auto-detections are NOT training-worthy
    /// </summary>
    public static bool DetermineIfTrainingWorthy(TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult result)
    {
        // Manual admin decisions are always training-worthy (will be set when admin uses Mark as Spam/Ham)
        // For auto-detections, only confident results are training-worthy

        // Check if OpenAI was involved and was confident (85%+ confidence)
        var openAIResult = result.CheckResults.FirstOrDefault(c => c.CheckName == "OpenAI");
        if (openAIResult != null)
        {
            // OpenAI confident (85%+) = training-worthy
            return openAIResult.Confidence >= OpenAIConfidentThreshold;
        }

        // No OpenAI veto = borderline/uncertain detection
        // Only use for training if net confidence is very high (>80)
        // This prevents low-quality auto-detections from polluting training data
        return result.NetConfidence > 80;
    }

    /// <summary>
    /// Handle content detection actions based on violation type and confidence levels
    /// Phase 4.13: Differentiates between Spam, HardBlock, and Malware
    /// - HardBlock → Instant ban + delete (no OpenAI veto, policy violation)
    /// - Malware → Delete + alert admin (no auto-ban, might be accidental)
    /// - Spam (Net > +50 with OpenAI 85%+) → Auto-ban + delete
    /// - Spam (borderline or uncertain) → Create report for admin review
    /// </summary>
    public async Task HandleSpamDetectionActionsAsync(
        Message message,
        TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult spamResult,
        DetectionResultRecord detectionResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Only take action if spam was detected
            if (!spamResult.IsSpam || spamResult.NetConfidence <= BorderlineNetConfidenceThreshold)
            {
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var reportsRepo = scope.ServiceProvider.GetRequiredService<IReportsRepository>();
            var moderationActionService = scope.ServiceProvider.GetRequiredService<ModerationActionService>();

            // Phase 4.13: Check for hard block or malware (different handling than spam)
            var hardBlockResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == "HardBlock");
            var malwareResult = spamResult.CheckResults.FirstOrDefault(c => c.Result == TelegramGroupsAdmin.ContentDetection.Models.CheckResultType.Malware);

            if (hardBlockResult != null)
            {
                // Hard block = instant policy violation (no OpenAI veto needed)
                logger.LogWarning(
                    "Hard block for message {MessageId} from user {UserId} in chat {ChatId}: {Reason}",
                    message.MessageId, message.From?.Id, message.Chat.Id, hardBlockResult.Details);

                // Execute instant ban across all chats
                await ExecuteAutoBanAsync(
                    scope.ServiceProvider,
                    message,
                    spamResult,
                    hardBlockResult,
                    cancellationToken);

                // Delete the message
                await moderationActionService.DeleteMessageAsync(message.MessageId, message.Chat.Id, cancellationToken);

                logger.LogInformation(
                    "Deleted hard block message {MessageId} and banned user {UserId} (policy violation)",
                    message.MessageId, message.From?.Id);
                return;
            }

            if (malwareResult != null)
            {
                // Malware = delete message + alert admin (don't auto-ban, might be accidental upload)
                logger.LogWarning(
                    "Malware detected in message {MessageId} from user {UserId} in chat {ChatId}: {Details}",
                    message.MessageId, message.From?.Id, message.Chat.Id, malwareResult.Details);

                // Delete the malware-containing message
                await moderationActionService.DeleteMessageAsync(message.MessageId, message.Chat.Id, cancellationToken);

                // Create critical alert for admin review
                await CreateBorderlineReportAsync(
                    reportsRepo,
                    message,
                    spamResult,
                    detectionResult,
                    $"MALWARE DETECTED: {malwareResult.Details}",
                    cancellationToken);

                logger.LogInformation(
                    "Deleted malware message {MessageId} and created admin alert (no auto-ban)",
                    message.MessageId);
                return;
            }

            // Standard spam detection handling below...
            // Check if OpenAI was involved and how confident it was
            var openAIResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == "OpenAI");
            var openAIConfident = openAIResult != null && openAIResult.Confidence >= OpenAIConfidentThreshold;

            // Decision logic based on net confidence and OpenAI involvement
            // Phase 4.5: Skip auto-ban if OpenAI flagged for review
            if (openAIResult?.Result == TelegramGroupsAdmin.ContentDetection.Models.CheckResultType.Review)
            {
                // OpenAI uncertain - send to admin review instead of auto-ban
                await CreateBorderlineReportAsync(
                    reportsRepo,
                    message,
                    spamResult,
                    detectionResult,
                    $"OpenAI flagged for review - Net: {spamResult.NetConfidence}",
                    cancellationToken);

                logger.LogInformation(
                    "Created admin review report for message {MessageId} in chat {ChatId}: OpenAI flagged for human review",
                    message.MessageId,
                    message.Chat.Id);
                return; // Early return - don't auto-ban
            }

            if (spamResult.NetConfidence > AutoBanNetConfidenceThreshold && openAIConfident && openAIResult!.Result == TelegramGroupsAdmin.ContentDetection.Models.CheckResultType.Spam)
            {
                // High confidence + OpenAI confirmed = auto-ban across all managed chats
                logger.LogInformation(
                    "Message {MessageId} from user {UserId} in chat {ChatId} triggers auto-ban (net: {NetConfidence}, OpenAI: {OpenAIConf}%)",
                    message.MessageId,
                    message.From?.Id,
                    message.Chat.Id,
                    spamResult.NetConfidence,
                    openAIResult.Confidence);

                await ExecuteAutoBanAsync(
                    scope.ServiceProvider,
                    message,
                    spamResult,
                    openAIResult,
                    cancellationToken);

                // Delete the spam message from the chat
                await moderationActionService.DeleteMessageAsync(message.MessageId, message.Chat.Id, cancellationToken);

                logger.LogInformation(
                    "Deleted spam message {MessageId} from chat {ChatId} (auto-ban)",
                    message.MessageId,
                    message.Chat.Id);
            }
            else if (spamResult.NetConfidence > BorderlineNetConfidenceThreshold)
            {
                // Borderline detection (0 < net ≤ 50) OR OpenAI uncertain (<85%) → Admin review
                var reason = spamResult.NetConfidence > AutoBanNetConfidenceThreshold
                    ? $"OpenAI uncertain (<{OpenAIConfidentThreshold}%) - Net: {spamResult.NetConfidence}, OpenAI: {openAIResult?.Confidence ?? 0}%"
                    : $"Borderline detection - Net: {spamResult.NetConfidence}";

                await CreateBorderlineReportAsync(
                    reportsRepo,
                    message,
                    spamResult,
                    detectionResult,
                    reason,
                    cancellationToken);

                logger.LogInformation(
                    "Created admin review report for message {MessageId} in chat {ChatId}: {Reason}",
                    message.MessageId,
                    message.Chat.Id,
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
    /// Create a report for borderline spam detections
    /// </summary>
    private static async Task CreateBorderlineReportAsync(
        IReportsRepository reportsRepo,
        Message message,
        TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult spamResult,
        DetectionResultRecord detectionResult,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var report = new Report(
            Id: 0, // Will be assigned by database
            MessageId: (int)message.MessageId, // Convert to int (Telegram message IDs fit in int32)
            ChatId: message.Chat.Id,
            ReportCommandMessageId: null, // Auto-generated report (not from /report command)
            ReportedByUserId: null, // System-generated (not user-reported)
            ReportedByUserName: "Auto-Detection",
            ReportedAt: DateTimeOffset.UtcNow,
            Status: DataModels.ReportStatus.Pending,
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

        await reportsRepo.InsertAsync(report, cancellationToken);
    }

    /// <summary>
    /// Execute auto-ban for confident spam across all managed chats
    /// </summary>
    private async Task ExecuteAutoBanAsync(
        IServiceProvider scopedServiceProvider,
        Message message,
        TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult spamResult,
        TelegramGroupsAdmin.ContentDetection.Models.ContentCheckResponse openAIResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userActionsRepo = scopedServiceProvider.GetRequiredService<IUserActionsRepository>();
            var managedChatsRepo = scopedServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var botFactory = scopedServiceProvider.GetRequiredService<TelegramBotClientFactory>();
            var telegramOptions = scopedServiceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;

            var botClient = botFactory.GetOrCreate(telegramOptions.BotToken);

            // Store ban action in database
            var banAction = new UserActionRecord(
                Id: 0, // Will be assigned by database
                UserId: message.From!.Id,
                ActionType: UserActionType.Ban,
                MessageId: message.MessageId,
                IssuedBy: Actor.AutoDetection,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent ban
                Reason: $"Auto-ban: High confidence spam (Net: {spamResult.NetConfidence}, OpenAI: {openAIResult.Confidence}%)"
            );

            await userActionsRepo.InsertAsync(banAction, cancellationToken);

            // Get all managed chats for cross-chat enforcement
            var managedChats = await managedChatsRepo.GetAllChatsAsync(cancellationToken);
            var activeChats = managedChats.Where(c => c.IsActive).ToList();

            logger.LogInformation(
                "Executing auto-ban for user {UserId} across {ChatCount} managed chats",
                message.From.Id,
                activeChats.Count);

            // Ban user across all managed chats via Telegram API
            int successCount = 0;
            int failCount = 0;

            foreach (var chat in activeChats)
            {
                try
                {
                    await botClient.BanChatMember(
                        chatId: chat.ChatId,
                        userId: message.From.Id,
                        untilDate: null, // Permanent ban
                        revokeMessages: true, // Delete all messages from this user
                        cancellationToken: cancellationToken);

                    successCount++;

                    logger.LogInformation(
                        "Banned user {UserId} from chat {ChatId}",
                        message.From.Id,
                        chat.ChatId);
                }
                catch (Exception ex)
                {
                    failCount++;
                    logger.LogError(ex,
                        "Failed to ban user {UserId} from chat {ChatId}",
                        message.From.Id,
                        chat.ChatId);
                }
            }

            logger.LogInformation(
                "Auto-ban complete for user {UserId}: {SuccessCount}/{TotalCount} successful, {FailCount} failed",
                message.From.Id,
                successCount,
                activeChats.Count,
                failCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to execute auto-ban for user {UserId}",
                message.From?.Id);
        }
    }

    /// <summary>
    /// Handle critical check violations for trusted/admin users (Phase 4.14)
    /// Policy: Delete message + DM notice, NO ban/warn for trusted/admin users
    /// Critical checks (URL filtering, file scanning) bypass trust status
    /// </summary>
    /// <param name="botClient">Telegram bot client</param>
    /// <param name="message">Original message that violated critical check</param>
    /// <param name="violations">List of critical check violations with details</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task HandleCriticalCheckViolationAsync(
        ITelegramBotClient botClient,
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
            var userMessagingService = scope.ServiceProvider.GetRequiredService<IUserMessagingService>();

            var userId = message.From.Id;
            var chatId = message.Chat.Id;
            var userName = message.From.Username ?? message.From.FirstName ?? $"User {userId}";

            logger.LogWarning(
                "Critical check violation by user {UserId} (@{Username}) in chat {ChatId}: {Violations}",
                userId,
                userName,
                chatId,
                string.Join("; ", violations));

            // Step 1: Delete the violating message
            try
            {
                await botClient.DeleteMessage(chatId, message.MessageId, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Deleted message {MessageId} from chat {ChatId} due to critical check violations",
                    message.MessageId,
                    chatId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to delete message {MessageId} from chat {ChatId}",
                    message.MessageId,
                    chatId);
            }

            // Step 2: Notify user about violation (DM preferred, public reply fallback)
            var violationList = string.Join("\n", violations.Select((v, i) => $"{i + 1}. {v}"));
            var notificationMessage = $"⚠️ Your message was deleted due to security policy violations:\n\n{violationList}\n\n" +
                                     $"These checks apply to all users regardless of trust status.";

            var sendResult = await userMessagingService.SendToUserAsync(
                botClient,
                userId,
                chatId,
                notificationMessage,
                replyToMessageId: null,  // Original message already deleted
                cancellationToken).ConfigureAwait(false);

            if (sendResult.Success)
            {
                logger.LogInformation(
                    "Sent critical check violation notice to user {UserId} via {DeliveryMethod}",
                    userId,
                    sendResult.DeliveryMethod);
            }
            else
            {
                logger.LogWarning(
                    "Failed to send critical check violation notice to user {UserId}: {Error}",
                    userId,
                    sendResult.ErrorMessage);
            }

            // Step 3: Log audit event (for transparency)
            logger.LogInformation(
                "Critical check violation handling complete for user {UserId} in chat {ChatId}. " +
                "Message deleted, user notified via {DeliveryMethod}. NO ban/warning applied.",
                userId,
                chatId,
                sendResult.DeliveryMethod);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to handle critical check violation for user {UserId} in chat {ChatId}",
                message.From.Id,
                message.Chat.Id);
        }
    }
}

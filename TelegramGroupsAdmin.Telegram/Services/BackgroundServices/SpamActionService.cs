using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using DataModels = TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Handles spam detection actions: auto-ban and borderline report creation
/// Phase 5.1: Sends notifications to admins for spam events
/// </summary>
public class SpamActionService(
    IServiceProvider serviceProvider,
    ILogger<SpamActionService> logger)
{
    // Confidence thresholds for spam detection decisions
    private const int AutoBanNetConfidenceThreshold = 50;
    private const int BorderlineNetConfidenceThreshold = 0;
    private const int OpenAIConfidentThreshold = 85;

    // FEATURE-4.23: Track recent cleanup job schedules to prevent duplicate jobs for same user
    // Key: TelegramUserId, Value: Timestamp when cleanup job was last scheduled
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTimeOffset> _recentCleanupJobs = new();
    private static readonly TimeSpan CleanupJobDeduplicationWindow = TimeSpan.FromSeconds(30);
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
        var openAIResult = result.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
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
            var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
            var messagingService = scope.ServiceProvider.GetRequiredService<IUserMessagingService>();
            var botFactory = scope.ServiceProvider.GetRequiredService<TelegramBotClientFactory>();
            var telegramOptions = scope.ServiceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;
            var userActionsRepo = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
            var managedChatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();

            // Phase 4.13: Check for hard block or malware (different handling than spam)
            var hardBlockResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.UrlBlocklist);
            var malwareResult = spamResult.CheckResults.FirstOrDefault(c => c.Result == TelegramGroupsAdmin.ContentDetection.Models.CheckResultType.Malware);

            if (hardBlockResult != null)
            {
                // Hard block = instant policy violation (no OpenAI veto needed)
                logger.LogWarning(
                    "Hard block for message {MessageId} from user {UserId} in chat {ChatId}: {Reason}",
                    message.MessageId, message.From?.Id, message.Chat.Id, hardBlockResult.Details);

                // Execute instant ban across all chats
                await ExecuteAutoBanAsync(
                    userActionsRepo,
                    managedChatsRepo,
                    botFactory,
                    telegramOptions,
                    message,
                    spamResult,
                    hardBlockResult,
                    cancellationToken);

                // Delete the message
                await moderationActionService.DeleteMessageAsync(
                    messageId: message.MessageId,
                    chatId: message.Chat.Id,
                    userId: message.From!.Id,
                    deletedBy: Actor.AutoDetection,
                    reason: "Hard block policy violation (automated spam filter)",
                    cancellationToken: cancellationToken);

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
                await moderationActionService.DeleteMessageAsync(
                    messageId: message.MessageId,
                    chatId: message.Chat.Id,
                    userId: message.From!.Id,
                    deletedBy: Actor.FileScanner,
                    reason: $"Malware detected: {malwareResult.Details}",
                    cancellationToken: cancellationToken);

                // Create critical alert for admin review
                await CreateBorderlineReportAsync(
                    reportsRepo,
                    chatAdminsRepository,
                    messagingService,
                    botFactory,
                    telegramOptions,
                    message,
                    spamResult,
                    detectionResult,
                    $"MALWARE DETECTED: {malwareResult.Details}",
                    cancellationToken);

                // Notify chat admins about malware detection (Phase 5.1)
                SendNotificationAsync(message.Chat.Id, NotificationEventType.MalwareDetected,
                    "Malware Detected and Removed",
                    $"Malware was detected in chat '{message.Chat.Title ?? message.Chat.Id.ToString()}' and the message was deleted.\n\n" +
                    $"User: {message.From?.Username ?? message.From?.FirstName ?? message.From?.Id.ToString()}\n" +
                    $"Detection: {malwareResult.Details}\n\n" +
                    $"The user was NOT auto-banned (malware upload may be accidental). Please review the report in the admin panel.",
                    cancellationToken);

                logger.LogInformation(
                    "Deleted malware message {MessageId} and created admin alert (no auto-ban)",
                    message.MessageId);
                return;
            }

            // Standard spam detection handling below...
            // Check if OpenAI was involved and how confident it was
            var openAIResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
            var openAIConfident = openAIResult != null && openAIResult.Confidence >= OpenAIConfidentThreshold;

            // Decision logic based on net confidence and OpenAI involvement
            // Phase 4.5: Skip auto-ban if OpenAI flagged for review
            if (openAIResult?.Result == TelegramGroupsAdmin.ContentDetection.Models.CheckResultType.Review)
            {
                // OpenAI uncertain - send to admin review instead of auto-ban
                await CreateBorderlineReportAsync(
                    reportsRepo,
                    chatAdminsRepository,
                    messagingService,
                    botFactory,
                    telegramOptions,
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

                // Notify chat admins about spam detection (Phase 5.1)
                SendNotificationAsync(message.Chat.Id, NotificationEventType.SpamDetected,
                    "Spam Detected - Auto-Ban Triggered",
                    $"High-confidence spam detected in chat '{message.Chat.Title ?? message.Chat.Id.ToString()}'.\n\n" +
                    $"User: {message.From?.Username ?? message.From?.FirstName ?? message.From?.Id.ToString()}\n" +
                    $"Confidence: {spamResult.NetConfidence}% (OpenAI: {openAIResult.Confidence}%)\n" +
                    $"Action: User auto-banned across all managed chats and message deleted.\n\n" +
                    $"Reason: {spamResult.CheckResults.FirstOrDefault()?.Details ?? "Multiple spam indicators detected"}",
                    cancellationToken);

                await ExecuteAutoBanAsync(
                    userActionsRepo,
                    managedChatsRepo,
                    botFactory,
                    telegramOptions,
                    message,
                    spamResult,
                    openAIResult,
                    cancellationToken);

                // Delete the spam message from the chat
                await moderationActionService.DeleteMessageAsync(
                    messageId: message.MessageId,
                    chatId: message.Chat.Id,
                    userId: message.From!.Id,
                    deletedBy: Actor.AutoDetection,
                    reason: $"Auto-ban triggered (net confidence: {spamResult.NetConfidence}%, OpenAI confirmed)",
                    cancellationToken: cancellationToken);

                // Notify chat admins about message deletion (Phase 5.1)
                SendNotificationAsync(message.Chat.Id, NotificationEventType.SpamAutoDeleted,
                    "Spam Message Auto-Deleted",
                    $"Spam message automatically deleted from chat '{message.Chat.Title ?? message.Chat.Id.ToString()}'.\n\n" +
                    $"User: {message.From?.Username ?? message.From?.FirstName ?? message.From?.Id.ToString()}\n" +
                    $"Message ID: {message.MessageId}\n" +
                    $"User has been banned across all managed chats.",
                    cancellationToken);

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
                    chatAdminsRepository,
                    messagingService,
                    botFactory,
                    telegramOptions,
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
    /// Phase 5.1: Sends DM notifications to admins
    /// </summary>
    private async Task CreateBorderlineReportAsync(
        IReportsRepository reportsRepo,
        IChatAdminsRepository chatAdminsRepository,
        IUserMessagingService messagingService,
        TelegramBotClientFactory botFactory,
        TelegramOptions telegramOptions,
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

        var reportId = await reportsRepo.InsertAsync(report, cancellationToken);

        // Send DM notifications to admins (same pattern as ReportCommand)
        var admins = await chatAdminsRepository.GetChatAdminsAsync(message.Chat.Id, cancellationToken);
        var adminUserIds = admins.Select(a => a.TelegramId).ToList();

        if (adminUserIds.Any())
        {
            var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
            var reportedUser = message.From;
            var messagePreview = message.Text?.Length > 100
                ? message.Text.Substring(0, 100) + "..."
                : message.Text ?? "[Media message]";

            var reportNotification = $"🚨 **New Auto-Detected Report #{reportId}**\n\n" +
                                    $"**Chat:** {chatName}\n" +
                                    $"**Reported by:** Auto-Detection System\n" +
                                    $"**Reported user:** @{reportedUser?.Username ?? reportedUser?.FirstName ?? reportedUser?.Id.ToString() ?? "Unknown"}\n" +
                                    $"**Message:** {messagePreview}\n" +
                                    $"**Confidence:** Net {spamResult.NetConfidence}% / Max {spamResult.MaxConfidence}%\n\n" +
                                    $"[Jump to message](https://t.me/c/{Math.Abs(message.Chat.Id).ToString().TrimStart('-')}/{message.MessageId})\n\n" +
                                    $"Review in the Reports tab or use moderation commands.";

            var botClient = botFactory.GetOrCreate(telegramOptions.BotToken);
            var results = await messagingService.SendToMultipleUsersAsync(
                botClient,
                userIds: adminUserIds,
                chatId: message.Chat.Id,
                messageText: reportNotification,
                replyToMessageId: message.MessageId,
                cancellationToken);

            var dmCount = results.Count(r => r.DeliveryMethod == MessageDeliveryMethod.PrivateDm);
            var mentionCount = results.Count(r => r.DeliveryMethod == MessageDeliveryMethod.ChatMention);

            logger.LogInformation(
                "Auto-detection report {ReportId} notification sent to {TotalAdmins} admins ({DmCount} via DM, {MentionCount} via chat mention)",
                reportId, results.Count, dmCount, mentionCount);
        }
    }

    /// <summary>
    /// Execute auto-ban for confident spam across all managed chats
    /// </summary>
    private async Task ExecuteAutoBanAsync(
        IUserActionsRepository userActionsRepo,
        IManagedChatsRepository managedChatsRepo,
        TelegramBotClientFactory botFactory,
        TelegramOptions telegramOptions,
        Message message,
        TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult spamResult,
        TelegramGroupsAdmin.ContentDetection.Models.ContentCheckResponse openAIResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
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

            // FEATURE-4.23: Schedule cross-chat message cleanup job
            // Note: BanChatMember with revokeMessages=true only deletes messages in the chat where the ban occurred
            // This job deletes all messages from the banned user across ALL managed chats
            // Race condition mitigation: Spambots often post same message in multiple chats rapidly
            // Use deduplication to ensure only one cleanup job runs per user within 30-second window
            try
            {
                var now = DateTimeOffset.UtcNow;
                var userId = message.From.Id;

                // Check if we already scheduled a cleanup job for this user recently
                if (_recentCleanupJobs.TryGetValue(userId, out var lastScheduled))
                {
                    var timeSinceLastSchedule = now - lastScheduled;
                    if (timeSinceLastSchedule < CleanupJobDeduplicationWindow)
                    {
                        logger.LogDebug(
                            "Skipping duplicate cleanup job for user {UserId} (already scheduled {Seconds}s ago)",
                            userId,
                            timeSinceLastSchedule.TotalSeconds);
                        // Don't schedule another job - the existing one will handle all messages
                        goto skipCleanupJob;
                    }
                }

                var deleteMessagesPayload = new DeleteUserMessagesPayload
                {
                    TelegramUserId = userId
                };

                await TickerQUtilities.ScheduleJobAsync(
                    serviceProvider,
                    logger,
                    "DeleteUserMessages",
                    deleteMessagesPayload,
                    delaySeconds: 15, // 15-second delay allows spambot to post across all chats before cleanup
                    retries: 0); // Best-effort, don't retry (48-hour window limitation)

                // Track this scheduling to prevent duplicates
                _recentCleanupJobs[userId] = now;

                // Cleanup old entries to prevent memory leak (remove entries older than window)
                var expiredEntries = _recentCleanupJobs
                    .Where(kvp => now - kvp.Value > CleanupJobDeduplicationWindow)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var expiredUserId in expiredEntries)
                {
                    _recentCleanupJobs.TryRemove(expiredUserId, out _);
                }

                logger.LogInformation(
                    "Scheduled cross-chat message cleanup job for user {UserId} (will execute in 15s)",
                    userId);

                skipCleanupJob: ; // Label for early exit from deduplication check
            }
            catch (Exception jobEx)
            {
                logger.LogWarning(jobEx,
                    "Failed to schedule message cleanup job for user {UserId} (ban still successful)",
                    message.From.Id);
            }

            // Notify chat admins about the ban (Phase 5.1)
            SendNotificationAsync(message.Chat.Id, NotificationEventType.UserBanned,
                "User Auto-Banned",
                $"User automatically banned from chat '{message.Chat.Title ?? message.Chat.Id.ToString()}' and {activeChats.Count - 1} other managed chats.\n\n" +
                $"User: {message.From.Username ?? message.From.FirstName ?? message.From.Id.ToString()}\n" +
                $"Ban Status: {successCount}/{activeChats.Count} chats\n" +
                $"Reason: {banAction.Reason}",
                cancellationToken);
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

            // Step 1: Delete the violating message with tracked deletion
            try
            {
                using var deleteScope = serviceProvider.CreateScope();
                var botMessageService = deleteScope.ServiceProvider.GetRequiredService<BotMessageService>();
                await botMessageService.DeleteAndMarkMessageAsync(
                    botClient,
                    chatId,
                    message.MessageId,
                    deletionSource: "critical_violation",
                    cancellationToken);
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
                cancellationToken);

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

    /// <summary>
    /// Helper method to send notifications with proper scope management (Phase 5.1)
    /// Creates scope to resolve scoped INotificationService from singleton background service
    /// Fire-and-forget pattern - does not await the notification task
    /// </summary>
    private void SendNotificationAsync(long chatId, NotificationEventType eventType, string subject, string message, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notificationService.SendChatNotificationAsync(chatId, eventType, subject, message, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send notification for event {EventType} in chat {ChatId}", eventType, chatId);
            }
        }, ct);
    }
}

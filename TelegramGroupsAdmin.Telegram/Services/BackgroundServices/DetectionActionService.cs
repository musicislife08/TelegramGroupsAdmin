using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Handles content detection actions: auto-ban and borderline report creation
/// Phase 5.1: Sends notifications to admins for detection events
/// </summary>
public class DetectionActionService(
    IServiceProvider serviceProvider,
    ChatManagementService chatManagementService,
    IJobScheduler jobScheduler,
    ILogger<DetectionActionService> logger)
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
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
            var moderationActionService = scope.ServiceProvider.GetRequiredService<Moderation.ModerationOrchestrator>();
            var botFactory = scope.ServiceProvider.GetRequiredService<ITelegramBotClientFactory>();
            var configLoader = scope.ServiceProvider.GetRequiredService<ITelegramConfigLoader>();
            var botToken = await configLoader.LoadConfigAsync();
            var userActionsRepo = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
            var managedChatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();

            // Phase 4.13: Check for hard block or malware (different handling than spam)
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

                // Execute instant ban across all chats
                await ExecuteAutoBanAsync(
                    userActionsRepo,
                    managedChatsRepo,
                    botFactory,
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

                logger.LogWarning(
                    "Deleted hard block message {MessageId} and banned {User} (policy violation)",
                    message.MessageId,
                    LogDisplayName.UserDebug(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0));
                return;
            }

            if (malwareResult != null)
            {
                // Malware = delete message + alert admin (don't auto-ban, might be accidental upload)
                logger.LogWarning(
                    "Malware detected in message {MessageId} from {User} in {Chat}: {Details}",
                    message.MessageId,
                    LogDisplayName.UserDebug(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                    LogDisplayName.ChatDebug(message.Chat.Title, message.Chat.Id),
                    malwareResult.Details);

                // Delete the malware-containing message
                await moderationActionService.DeleteMessageAsync(
                    messageId: message.MessageId,
                    chatId: message.Chat.Id,
                    userId: message.From!.Id,
                    deletedBy: Actor.FileScanner,
                    reason: $"Malware detected: {malwareResult.Details}",
                    cancellationToken: cancellationToken);

                // Create critical alert for admin review
                var malwareReport = BuildAutoDetectionReport(
                    message,
                    spamResult,
                    detectionResult,
                    $"MALWARE DETECTED: {malwareResult.Details}");

                await reportService.CreateReportAsync(
                    malwareReport,
                    message,
                    isAutomated: true,
                    cancellationToken);

                // Notify chat admins about malware detection (Phase 5.1)
                SendNotificationAsync(message.Chat.Id, NotificationEventType.MalwareDetected,
                    "Malware Detected and Removed",
                    $"Malware was detected in chat '{message.Chat.Title ?? message.Chat.Id.ToString()}' and the message was deleted.\n\n" +
                    $"User: {TelegramDisplayName.Format(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id)}\n" +
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
            if (openAIResult?.Result == ContentDetection.Models.CheckResultType.Review)
            {
                // OpenAI uncertain - send to admin review instead of auto-ban
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
                    "Created admin review report for message {MessageId} in chat {ChatId}: OpenAI flagged for human review",
                    message.MessageId,
                    message.Chat.Id);
                return; // Early return - don't auto-ban
            }

            if (spamResult.NetConfidence > AutoBanNetConfidenceThreshold && openAIConfident && openAIResult!.Result == ContentDetection.Models.CheckResultType.Spam)
            {
                // High confidence + OpenAI confirmed = auto-ban across all managed chats
                logger.LogInformation(
                    "Message {MessageId} from {User} in {Chat} triggers auto-ban (net: {NetConfidence}, OpenAI: {OpenAIConf}%)",
                    message.MessageId,
                    LogDisplayName.UserInfo(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                    LogDisplayName.ChatInfo(message.Chat.Title, message.Chat.Id),
                    spamResult.NetConfidence,
                    openAIResult.Confidence);

                // Execute auto-ban and collect results
                var banResult = await ExecuteAutoBanAsync(
                    userActionsRepo,
                    managedChatsRepo,
                    botFactory,
                    message,
                    spamResult,
                    openAIResult,
                    cancellationToken);

                // Delete the spam message from the chat
                bool messageDeleted = false;
                try
                {
                    await moderationActionService.DeleteMessageAsync(
                        messageId: message.MessageId,
                        chatId: message.Chat.Id,
                        userId: message.From!.Id,
                        deletedBy: Actor.AutoDetection,
                        reason: $"Auto-ban triggered (net confidence: {spamResult.NetConfidence}%, OpenAI confirmed)",
                        cancellationToken: cancellationToken);

                    messageDeleted = true;

                    logger.LogInformation(
                        "Deleted spam message {MessageId} from chat {ChatId} (auto-ban)",
                        message.MessageId,
                        message.Chat.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete spam message {MessageId} from chat {ChatId}",
                        message.MessageId, message.Chat.Id);
                }

                // Send consolidated notification (Phase 5.2: replaces 3 separate notifications)
                SendConsolidatedSpamNotificationAsync(
                    message,
                    spamResult,
                    openAIResult,
                    banResult,
                    messageDeleted,
                    cancellationToken);
            }
            else if (spamResult.NetConfidence > BorderlineNetConfidenceThreshold)
            {
                // Borderline detection (0 < net ≤ 50) OR OpenAI uncertain (<85%) → Admin review
                var reason = spamResult.NetConfidence > AutoBanNetConfidenceThreshold
                    ? $"OpenAI uncertain (<{OpenAIConfidentThreshold}%) - Net: {spamResult.NetConfidence}, OpenAI: {openAIResult?.Confidence ?? 0}%"
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
    /// Build a Report record for auto-detected content with admin notes containing detection details
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
    }

    /// <summary>
    /// Execute auto-ban for confident spam across all managed chats
    /// </summary>
    private async Task<AutoBanResult?> ExecuteAutoBanAsync(
        IUserActionsRepository userActionsRepo,
        IManagedChatsRepository managedChatsRepo,
        ITelegramBotClientFactory botFactory,
        Message message,
        TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult spamResult,
        TelegramGroupsAdmin.ContentDetection.Models.ContentCheckResponse openAIResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var operations = await botFactory.GetOperationsAsync();

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

            // ADMIN PROTECTION: Check if user is admin in ANY managed chat
            // If yes, skip ban entirely (protects admins globally across all chats)
            using (var scope = serviceProvider.CreateScope())
            {
                var chatAdminsRepo = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
                var adminChats = await chatAdminsRepo.GetAdminChatsAsync(message.From.Id, cancellationToken);

                if (adminChats.Count > 0)
                {
                    logger.LogInformation(
                        "Skipping auto-ban for {User} - user is admin in {ChatCount} managed chat(s) (admin protection)",
                        LogDisplayName.UserInfo(message.From.FirstName, message.From.LastName, message.From.Username, message.From.Id),
                        adminChats.Count);
                    return null;
                }
            }

            // Health gate: Filter for chats where bot has confirmed permissions
            var healthyChatIds = chatManagementService.FilterHealthyChats(activeChats.Select(c => c.ChatId)).ToHashSet();
            var actionableChats = activeChats.Where(c => healthyChatIds.Contains(c.ChatId)).ToList();

            // Log chats skipped due to health issues
            var skippedChatIds = activeChats.Select(c => c.ChatId).Except(actionableChats.Select(c => c.ChatId)).ToList();
            if (skippedChatIds.Count > 0)
            {
                logger.LogWarning(
                    "Skipping {Count} unhealthy chats for auto-ban: {ChatIds}. " +
                    "Bot lacks required permissions (admin + ban members) in these chats.",
                    skippedChatIds.Count,
                    string.Join(", ", skippedChatIds));
            }

            logger.LogInformation(
                "Executing auto-ban for {User} across {ChatCount} managed chats ({SkippedCount} skipped due to health)",
                LogDisplayName.UserInfo(message.From.FirstName, message.From.LastName, message.From.Username, message.From.Id),
                actionableChats.Count,
                skippedChatIds.Count);

            // Ban user across all actionable managed chats via Telegram API
            int successCount = 0;
            int failCount = 0;

            foreach (var chat in actionableChats)
            {
                try
                {
                    await operations.BanChatMemberAsync(
                        chatId: chat.ChatId,
                        userId: message.From.Id,
                        untilDate: null, // Permanent ban
                        ct: cancellationToken);

                    successCount++;

                    logger.LogInformation(
                        "Banned {User} from {Chat}",
                        LogDisplayName.UserInfo(message.From.FirstName, message.From.LastName, message.From.Username, message.From.Id),
                        LogDisplayName.ChatInfo(chat.ChatName, chat.ChatId));
                }
                catch (Exception ex)
                {
                    failCount++;
                    logger.LogError(ex,
                        "Failed to ban {User} from {Chat}",
                        LogDisplayName.UserDebug(message.From.FirstName, message.From.LastName, message.From.Username, message.From.Id),
                        LogDisplayName.ChatDebug(chat.ChatName, chat.ChatId));
                }
            }

            logger.LogInformation(
                "Auto-ban complete for {User}: {SuccessCount}/{TotalCount} successful, {FailCount} failed",
                LogDisplayName.UserInfo(message.From.FirstName, message.From.LastName, message.From.Username, message.From.Id),
                successCount,
                actionableChats.Count,
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

                await jobScheduler.ScheduleJobAsync(
                    "DeleteUserMessages",
                    deleteMessagesPayload,
                    delaySeconds: 15); // 15-second delay allows spambot to post across all chats before cleanup

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

            skipCleanupJob:; // Label for early exit from deduplication check
            }
            catch (Exception jobEx)
            {
                logger.LogWarning(jobEx,
                    "Failed to schedule message cleanup job for user {UserId} (ban still successful)",
                    message.From.Id);
            }

            // Return ban results for consolidated notification (Phase 5.2)
            return new AutoBanResult(successCount, activeChats.Count, banAction.Reason ?? "Auto-ban triggered");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to execute auto-ban for user {UserId}",
                message.From?.Id);
            return null; // Ban failed
        }
    }

    /// <summary>
    /// Handle critical check violations for trusted/admin users (Phase 4.14)
    /// Policy: Delete message + DM notice, NO ban/warn for trusted/admin users
    /// Critical checks (URL filtering, file scanning) bypass trust status
    /// </summary>
    /// <param name="message">Original message that violated critical check</param>
    /// <param name="violations">List of critical check violations with details</param>
    /// <param name="cancellationToken">Cancellation token</param>
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
            var userMessagingService = scope.ServiceProvider.GetRequiredService<IUserMessagingService>();

            var userId = message.From.Id;
            var chatId = message.Chat.Id;
            var userName = TelegramDisplayName.Format(message.From.FirstName, message.From.LastName, message.From.Username, userId);

            logger.LogWarning(
                "Critical check violation by {User} in {Chat}: {Violations}",
                LogDisplayName.UserDebug(message.From.FirstName, message.From.LastName, message.From.Username, userId),
                LogDisplayName.ChatDebug(message.Chat.Title, chatId),
                string.Join("; ", violations));

            // Step 1: Delete the violating message with tracked deletion
            try
            {
                using var deleteScope = serviceProvider.CreateScope();
                var botMessageService = deleteScope.ServiceProvider.GetRequiredService<BotMessageService>();
                await botMessageService.DeleteAndMarkMessageAsync(
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
                userId,
                chatId,
                notificationMessage,
                replyToMessageId: null,  // Original message already deleted
                cancellationToken: cancellationToken);

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
    /// Send consolidated spam notification with media support (Phase 5.2)
    /// Replaces 3 separate notifications with 1 comprehensive message
    /// </summary>
    private void SendConsolidatedSpamNotificationAsync(
        Message message,
        TelegramGroupsAdmin.ContentDetection.Services.ContentDetectionResult spamResult,
        TelegramGroupsAdmin.ContentDetection.Models.ContentCheckResponse openAIResult,
        AutoBanResult? banResult,
        bool messageDeleted,
        CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Extract user info - using FormatMention since this is sent to Telegram
                var userDisplay = TelegramDisplayName.FormatMention(
                    message.From?.FirstName,
                    message.From?.LastName,
                    message.From?.Username,
                    message.From?.Id);

                // Build message text preview (truncate if >200 chars for caption limit)
                var messageTextPreview = message.Text != null && message.Text.Length > 200
                    ? message.Text[..197] + "..."
                    : message.Text ?? "[No text]";

                // Escape MarkdownV2 special characters
                messageTextPreview = EscapeMarkdownV2(messageTextPreview);
                var chatTitle = EscapeMarkdownV2(message.Chat.Title ?? message.Chat.Id.ToString());
                userDisplay = EscapeMarkdownV2(userDisplay);

                // Build detection details
                var detectionDetails = new System.Text.StringBuilder();
                detectionDetails.AppendLine($"• Net Confidence: {spamResult.NetConfidence}%");
                detectionDetails.AppendLine($"• OpenAI: {openAIResult.Confidence}% \\({EscapeMarkdownV2(openAIResult.Details ?? "Spam detected")}\\)");

                // Add triggered checks (top 3 by confidence)
                var topChecks = spamResult.CheckResults
                    .Where(c => c.Result == ContentDetection.Models.CheckResultType.Spam)
                    .OrderByDescending(c => c.Confidence)
                    .Take(3);

                foreach (var check in topChecks)
                {
                    var checkDetails = EscapeMarkdownV2(check.Details ?? "");
                    detectionDetails.AppendLine($"• {EscapeMarkdownV2(check.CheckName.ToString())}: {checkDetails}");
                }

                // Build action summary
                var actionSummary = new System.Text.StringBuilder();
                if (banResult != null)
                {
                    actionSummary.AppendLine($"✅ Banned from {banResult.SuccessCount}/{banResult.TotalChats} managed chats");
                }
                if (messageDeleted)
                {
                    actionSummary.AppendLine($"✅ Message deleted \\(ID: {message.MessageId}\\)");
                }

                // Build consolidated message
                var consolidatedMessage =
                    $"🚫 *Spam Auto\\-Banned*\n\n" +
                    $"*User:* {userDisplay}\n" +
                    $"*Chat:* {chatTitle}\n\n" +
                    $"📝 *Message:*\n{messageTextPreview}\n\n" +
                    $"🔍 *Detection:*\n{detectionDetails}\n" +
                    $"⛔ *Action Taken:*\n{actionSummary}";

                // Query database for media local path (if media exists)
                string? photoPath = null;
                string? videoPath = null;

                using var scope = serviceProvider.CreateScope();
                var messagesRepo = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();

                // Look up the message to get local file paths if media was downloaded
                var messageRecord = await messagesRepo.GetMessageAsync(message.MessageId, ct);
                if (messageRecord != null)
                {
                    // Photos are stored in PhotoLocalPath
                    if (!string.IsNullOrEmpty(messageRecord.PhotoLocalPath))
                    {
                        photoPath = messageRecord.PhotoLocalPath;
                        logger.LogDebug("Found photo path for spam notification: {PhotoPath}", photoPath);
                    }
                    // Videos/Animations are stored in MediaLocalPath
                    else if (messageRecord.MediaType is MediaType.Video
                        or MediaType.Animation
                        && !string.IsNullOrEmpty(messageRecord.MediaLocalPath))
                    {
                        videoPath = messageRecord.MediaLocalPath;
                        logger.LogDebug("Found video/animation path for spam notification: {VideoPath}", videoPath);
                    }
                }

                // Send DM notification with media support (Phase 5.2)
                // Note: Bypasses NotificationService to enable media support
                var chatAdminsRepo = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
                var telegramMappingRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();
                var dmDeliveryService = scope.ServiceProvider.GetRequiredService<IDmDeliveryService>();

                // Get all active admins for this chat
                var chatAdmins = await chatAdminsRepo.GetChatAdminsAsync(message.Chat.Id, ct);

                foreach (var admin in chatAdmins)
                {
                    // Map telegram ID to web user ID to verify they're linked
                    var mapping = await telegramMappingRepo.GetByTelegramIdAsync(admin.TelegramId, ct);
                    if (mapping == null)
                        continue;

                    // Send DM with media support (preferences will be checked by NotificationService in future)
                    await dmDeliveryService.SendDmWithMediaAsync(
                        admin.TelegramId,
                        "spam_banned",
                        consolidatedMessage,
                        photoPath,
                        videoPath,
                        ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send consolidated spam notification for message {MessageId} in chat {ChatId}",
                    message.MessageId, message.Chat.Id);
            }
        }, ct);
    }

    /// <summary>
    /// Escape special characters for MarkdownV2 format
    /// </summary>
    private static string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var specialChars = new[] { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
        foreach (var c in specialChars)
        {
            text = text.Replace(c.ToString(), "\\" + c);
        }
        return text;
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

/// <summary>
/// Result of auto-ban execution across managed chats (Phase 5.2)
/// </summary>
internal record AutoBanResult(int SuccessCount, int TotalChats, string BanReason);

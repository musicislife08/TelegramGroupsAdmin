using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.BackgroundServices;

/// <summary>
/// Handles spam detection actions: auto-ban and borderline report creation
/// </summary>
public class SpamActionService(
    IServiceProvider serviceProvider,
    ILogger<SpamActionService> logger)
{
    /// <summary>
    /// Determine if detection result should be used for training
    /// High-quality samples only: Confident OpenAI results (85%+) or manual admin decisions
    /// Low-confidence auto-detections are NOT training-worthy
    /// </summary>
    public static bool DetermineIfTrainingWorthy(TelegramGroupsAdmin.SpamDetection.Services.SpamDetectionResult result)
    {
        // Manual admin decisions are always training-worthy (will be set when admin uses Mark as Spam/Ham)
        // For auto-detections, only confident results are training-worthy

        // Check if OpenAI was involved and was confident (85%+ confidence)
        var openAIResult = result.CheckResults.FirstOrDefault(c => c.CheckName == "OpenAI");
        if (openAIResult != null)
        {
            // OpenAI confident (85%+) = training-worthy
            return openAIResult.Confidence >= 85;
        }

        // No OpenAI veto = borderline/uncertain detection
        // Only use for training if net confidence is very high (>80)
        // This prevents low-quality auto-detections from polluting training data
        return result.NetConfidence > 80;
    }

    /// <summary>
    /// Handle spam detection actions based on confidence levels
    /// - Net > +50 with OpenAI 85%+ confident → Auto-ban
    /// - Net > +50 with OpenAI <85% confident → Create report for admin review
    /// - Net +0 to +50 (borderline) → Create report for admin review
    /// - Net < 0 → No action (clean message)
    /// </summary>
    public async Task HandleSpamDetectionActionsAsync(
        Message message,
        TelegramGroupsAdmin.SpamDetection.Services.SpamDetectionResult spamResult,
        DetectionResultRecord detectionResult)
    {
        try
        {
            // Only take action if spam was detected
            if (!spamResult.IsSpam || spamResult.NetConfidence <= 0)
            {
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var reportsRepo = scope.ServiceProvider.GetRequiredService<IReportsRepository>();

            // Check if OpenAI was involved and how confident it was
            var openAIResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == "OpenAI");
            var openAIConfident = openAIResult != null && openAIResult.Confidence >= 85;

            // Decision logic based on net confidence and OpenAI involvement
            if (spamResult.NetConfidence > 50 && openAIConfident && openAIResult!.IsSpam)
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
                    openAIResult);
            }
            else if (spamResult.NetConfidence > 0)
            {
                // Borderline detection (0 < net ≤ 50) OR OpenAI uncertain (<85%) → Admin review
                var reason = spamResult.NetConfidence > 50
                    ? $"OpenAI uncertain (<85%) - Net: {spamResult.NetConfidence}, OpenAI: {openAIResult?.Confidence ?? 0}%"
                    : $"Borderline detection - Net: {spamResult.NetConfidence}";

                await CreateBorderlineReportAsync(
                    reportsRepo,
                    message,
                    spamResult,
                    detectionResult,
                    reason);

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
        TelegramGroupsAdmin.SpamDetection.Services.SpamDetectionResult spamResult,
        DetectionResultRecord detectionResult,
        string reason)
    {
        var report = new Report(
            Id: 0, // Will be assigned by database
            MessageId: (int)message.MessageId, // Convert to int (Telegram message IDs fit in int32)
            ChatId: message.Chat.Id,
            ReportCommandMessageId: null, // Auto-generated report (not from /report command)
            ReportedByUserId: null, // System-generated (not user-reported)
            ReportedByUserName: "Auto-Detection",
            ReportedAt: DateTimeOffset.UtcNow,
            Status: ReportStatus.Pending,
            ReviewedBy: null,
            ReviewedAt: null,
            ActionTaken: null,
            AdminNotes: $"{reason}\n\nDetection Details:\n{detectionResult.Reason}\n\nNet Confidence: {spamResult.NetConfidence}\nMax Confidence: {spamResult.MaxConfidence}",
            WebUserId: null // System-generated
        );

        await reportsRepo.InsertAsync(report);
    }

    /// <summary>
    /// Execute auto-ban for confident spam across all managed chats
    /// </summary>
    private async Task ExecuteAutoBanAsync(
        IServiceProvider scopedServiceProvider,
        Message message,
        TelegramGroupsAdmin.SpamDetection.Services.SpamDetectionResult spamResult,
        TelegramGroupsAdmin.SpamDetection.Models.SpamCheckResponse openAIResult)
    {
        try
        {
            var userActionsRepo = scopedServiceProvider.GetRequiredService<IUserActionsRepository>();
            var managedChatsRepo = scopedServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var botFactory = scopedServiceProvider.GetRequiredService<Services.Telegram.TelegramBotClientFactory>();
            var telegramOptions = scopedServiceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;

            var botClient = botFactory.GetOrCreate(telegramOptions.BotToken);

            // Store ban action in database
            var banAction = new UserActionRecord(
                Id: 0, // Will be assigned by database
                UserId: message.From!.Id,
                ActionType: UserActionType.Ban,
                MessageId: message.MessageId,
                IssuedBy: "Auto-Detection",
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent ban
                Reason: $"Auto-ban: High confidence spam (Net: {spamResult.NetConfidence}, OpenAI: {openAIResult.Confidence}%)"
            );

            await userActionsRepo.InsertAsync(banAction);

            // Get all managed chats for cross-chat enforcement
            var managedChats = await managedChatsRepo.GetAllChatsAsync();
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
                        revokeMessages: true); // Delete all messages from this user

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
}

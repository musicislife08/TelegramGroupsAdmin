using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /spam - Mark message as spam and take action
/// </summary>
public class SpamCommand : IBotCommand
{
    private readonly ILogger<SpamCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "spam";
    public string Description => "Mark message as spam and delete it";
    public string Usage => "/spam (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command

    public SpamCommand(
        ILogger<SpamCommand> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return "❌ Please reply to the spam message.";
        }

        var spamMessage = message.ReplyToMessage;
        var spamUserId = spamMessage.From?.Id;
        var spamUserName = spamMessage.From?.Username ?? spamMessage.From?.FirstName ?? "Unknown";

        if (spamUserId == null)
        {
            return "❌ Could not identify user.";
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
        var detectionResultsRepository = scope.ServiceProvider.GetRequiredService<IDetectionResultsRepository>();
        var messageRepository = scope.ServiceProvider.GetRequiredService<MessageHistoryRepository>();
        var telegramUserMappingRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();

        // Check if target user is an admin (can't mark admin messages as spam)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, spamUserId.Value);
        if (isAdmin)
        {
            return "❌ Cannot mark admin messages as spam.";
        }

        // Check if target user is trusted (can't mark trusted messages as spam)
        var isTrusted = await userActionsRepository.IsUserTrustedAsync(spamUserId.Value, message.Chat.Id);
        if (isTrusted)
        {
            return "❌ Cannot mark trusted user messages as spam.";
        }

        try
        {
            // Map executor Telegram ID to web app user ID
            string? executorUserId = null;
            if (message.From?.Id != null)
            {
                executorUserId = await telegramUserMappingRepository.GetUserIdByTelegramIdAsync(message.From.Id);
            }

            // 1. Delete the spam message
            await botClient.DeleteMessage(message.Chat.Id, spamMessage.MessageId, cancellationToken);

            // 2. Mark message as deleted in database
            await messageRepository.MarkMessageAsDeletedAsync(spamMessage.MessageId, "spam_command");

            // 3. Insert detection result (manual spam classification)
            var detectionResult = new DetectionResultRecord
            {
                MessageId = spamMessage.MessageId,
                DetectedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                DetectionSource = "manual",
                DetectionMethod = "Manual",
                IsSpam = true,
                Confidence = 100,
                Reason = $"Manually marked as spam by admin in chat {message.Chat.Title ?? message.Chat.Id.ToString()}",
                AddedBy = executorUserId, // Mapped from telegram_user_mappings (may be null if not linked)
                UserId = spamUserId.Value,
                MessageText = spamMessage.Text ?? "[no text]"
            };
            await detectionResultsRepository.InsertAsync(detectionResult);

            _logger.LogInformation(
                "Spam command executed by {AdminId} on message {MessageId} from user {SpamUserId} ({SpamUserName}) in chat {ChatId}",
                message.From?.Id, spamMessage.MessageId, spamUserId, spamUserName, message.Chat.Id);

            // TODO: Phase 2.4 - Auto-ban based on spam threshold
            // Count spam detections for this user and auto-ban if threshold exceeded

            return $"✅ Message deleted and marked as spam\n" +
                   $"User: @{spamUserName} ({spamUserId})";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete spam message {MessageId}", spamMessage.MessageId);
            return $"❌ Failed to delete message: {ex.Message}";
        }
    }
}

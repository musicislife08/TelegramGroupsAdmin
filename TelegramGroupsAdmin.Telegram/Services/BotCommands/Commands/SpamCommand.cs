using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /spam - Mark message as spam and take action
/// </summary>
public class SpamCommand : IBotCommand
{
    private readonly ILogger<SpamCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationOrchestrator _moderationService;

    public string Name => "spam";
    public string Description => "Mark message as spam and delete it";
    public string Usage => "/spam (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public SpamCommand(
        ILogger<SpamCommand> logger,
        IServiceProvider serviceProvider,
        ModerationOrchestrator moderationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
    }

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return new CommandResult("❌ Please reply to the spam message.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var spamMessage = message.ReplyToMessage;
        var spamUserId = spamMessage.From?.Id;
        var spamUserName = TelegramDisplayName.Format(
            spamMessage.From?.FirstName,
            spamMessage.From?.LastName,
            spamMessage.From?.Username,
            spamUserId);

        if (spamUserId == null)
        {
            return new CommandResult("❌ Could not identify user.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();

        // Check if target user is an admin (can't mark admin messages as spam)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, spamUserId.Value, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult("❌ Cannot mark admin messages as spam.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        // Check if target user is trusted (can't mark trusted messages as spam)
        var isTrusted = await userActionsRepository.IsUserTrustedAsync(spamUserId.Value, message.Chat.Id, cancellationToken);
        if (isTrusted)
        {
            return new CommandResult("❌ Cannot mark trusted user messages as spam.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        // Get executor actor
        var executor = Core.Models.Actor.FromTelegramUser(
            message.From!.Id,
            message.From.Username,
            message.From.FirstName,
            message.From.LastName);

        // Execute spam and ban action via centralized service
        var reason = $"Spam detected via /spam command in chat {message.Chat.Title ?? message.Chat.Id.ToString()}";
        var result = await _moderationService.MarkAsSpamAndBanAsync(
            messageId: spamMessage.MessageId,
            userId: spamUserId.Value,
            chatId: message.Chat.Id,
            executor: executor,
            reason: reason,
            telegramMessage: spamMessage, // Pass the Telegram message for backfilling if not in database
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return new CommandResult($"❌ Failed to process spam action: {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        _logger.LogInformation(
            "Spam command executed by {AdminId} on message {MessageId} from user {SpamUserId} ({SpamUserName}) in chat {ChatId}. " +
            "Banned from {ChatsAffected} chat(s). Trust removed: {TrustRemoved}",
            message.From?.Id, spamMessage.MessageId, spamUserId, spamUserName, message.Chat.Id, result.ChatsAffected, result.TrustRemoved);

        // Silent mode: No chat feedback, message and command simply disappear
        // Admins see action through DM notifications if enabled
        return new CommandResult(null, DeleteCommandMessage, DeleteResponseAfterSeconds);
    }
}

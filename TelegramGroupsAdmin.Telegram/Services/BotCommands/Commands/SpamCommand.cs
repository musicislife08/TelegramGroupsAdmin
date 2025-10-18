using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /spam - Mark message as spam and take action
/// </summary>
public class SpamCommand : IBotCommand
{
    private readonly ILogger<SpamCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationActionService _moderationService;

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
        ModerationActionService moderationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
    }

    public async Task<CommandResult> ExecuteAsync(
        ITelegramBotClient botClient,
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
        var spamUserName = spamMessage.From?.Username ?? spamMessage.From?.FirstName ?? "Unknown";

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

        // Get executor identifier (web app user ID if mapped, otherwise Telegram username/ID)
        var executorId = await _moderationService.GetExecutorIdentifierAsync(
            message.From!.Id,
            message.From.Username,
            cancellationToken);

        // Execute spam and ban action via centralized service
        var reason = $"Spam detected via /spam command in chat {message.Chat.Title ?? message.Chat.Id.ToString()}";
        var result = await _moderationService.MarkAsSpamAndBanAsync(
            botClient,
            spamMessage.MessageId,
            spamUserId.Value,
            message.Chat.Id,
            executorId,
            reason,
            cancellationToken);

        if (!result.Success)
        {
            return new CommandResult($"❌ Failed to process spam action: {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        _logger.LogInformation(
            "Spam command executed by {AdminId} on message {MessageId} from user {SpamUserId} ({SpamUserName}) in chat {ChatId}",
            message.From?.Id, spamMessage.MessageId, spamUserId, spamUserName, message.Chat.Id);

        var response = $"✅ Message deleted and marked as spam\n" +
                       $"User: @{spamUserName} ({spamUserId})\n" +
                       $"🚫 Banned from {result.ChatsAffected} chat(s)";

        if (result.TrustRemoved)
        {
            response += "\n⚠️ User trust revoked (compromised account protection)";
        }

        return new CommandResult(response, DeleteCommandMessage, DeleteResponseAfterSeconds);
    }
}

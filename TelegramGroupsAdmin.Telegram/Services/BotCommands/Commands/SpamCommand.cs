using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /spam - Mark message as spam and take action
/// </summary>
public class SpamCommand : IBotCommand
{
    private readonly ILogger<SpamCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotModerationService _moderationService;
    private readonly IUserIdentityService _identityService;

    public string Name => "spam";
    public string Description => "Mark message as spam and delete it";
    public string Usage => "/spam (reply to message)";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public SpamCommand(
        ILogger<SpamCommand> logger,
        IServiceProvider serviceProvider,
        IBotModerationService moderationService,
        IUserIdentityService identityService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
        _identityService = identityService;
    }

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        UserIdentity sender,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to the spam message."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var spamMessage = message.ReplyToMessage;
        if (spamMessage.From == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Could not identify user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var spamUser = await _identityService.ResolveAsync(spamMessage.From.Id, cancellationToken);

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target user is an admin (can't mark admin messages as spam)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, spamUser.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Cannot mark admin messages as spam."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        // NOTE: Trust status is intentionally NOT checked here.
        // Trust only bypasses automatic spam detection - admins can always manually mark trusted users as spam
        // if they start posting spam after building up trust.

        var executor = Core.Models.Actor.FromUserIdentity(sender);

        // Execute spam and ban action via centralized service
        var reason = $"Spam detected via /spam command in chat {message.Chat.Title ?? message.Chat.Id.ToString()}";
        var result = await _moderationService.MarkAsSpamAndBanAsync(
            new SpamBanIntent
            {
                User = spamUser,
                Chat = ChatIdentity.From(message.Chat),
                MessageId = spamMessage.MessageId,
                Executor = executor,
                Source = VerdictSource.SpamCommand,
                Reason = reason,
                TelegramMessage = spamMessage // Pass for backfill if message not in database
            },
            cancellationToken);

        if (!result.Success)
        {
            // The chat reply stays generic: ErrorMessage can carry exception text
            _logger.LogWarning("Failed to mark as spam and ban {User}: {Error}", spamUser.ToLogDebug(), result.ErrorMessage);
            return new CommandResult(TelegramMessage.Plain("❌ Failed to process spam action."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        _logger.LogInformation(
            "Spam command executed by {AdminId} on message {MessageId} from user {SpamUserId} ({SpamUserName}) in chat {ChatId}. " +
            "Banned from {ChatsAffected} chat(s). Trust removed: {TrustRemoved}",
            sender.Id, spamMessage.MessageId, spamUser.Id, spamUser.DisplayName, message.Chat.Id, result.ChatsAffected, result.TrustRemoved);

        // Silent mode: No chat feedback, message and command simply disappear
        // Admins see action through DM notifications if enabled
        return new CommandResult(TelegramMessage.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
    }
}

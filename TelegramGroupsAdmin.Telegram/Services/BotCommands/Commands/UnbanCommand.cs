using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /unban - Remove ban from user
/// </summary>
public class UnbanCommand : IBotCommand
{
    private readonly ILogger<UnbanCommand> _logger;
    private readonly IBotModerationService _moderationService;
    private readonly IUserIdentityService _identityService;
    private readonly IConfigService _configService;

    public string Name => "unban";
    public string Description => "Remove ban from user";
    public string Usage => "/unban (reply to message)";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public UnbanCommand(
        ILogger<UnbanCommand> logger,
        IBotModerationService moderationService,
        IUserIdentityService identityService,
        IConfigService configService)
    {
        _logger = logger;
        _moderationService = moderationService;
        _identityService = identityService;
        _configService = configService;
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
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to a message from the user to unban."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var replyFrom = message.ReplyToMessage.From;
        if (replyFrom == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Could not identify target user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetUser = await _identityService.ResolveAsync(replyFrom.Id, cancellationToken);

        try
        {
            var executor = Core.Models.Actor.FromUserIdentity(sender);

            // Execute unban action through ModerationActionService
            var result = await _moderationService.UnbanUserAsync(
                new UnbanIntent
                {
                    User = targetUser,
                    Executor = executor,
                    Reason = $"Manual unban command by {sender.Username ?? sender.Id.ToString()}",
                    RestoreTrust = false
                },
                cancellationToken);

            // Build response based on result
            if (!result.Success)
            {
                // The chat reply stays generic: ErrorMessage can carry exception text
                _logger.LogWarning("Failed to unban {User}: {Error}", targetUser.ToLogDebug(), result.ErrorMessage);
                return new CommandResult(TelegramMessage.Plain("❌ Failed to unban user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            // The mention follows the chat's name-masking setting
            var response = (await _configService.CreateChatMessageBuilderAsync(message.Chat.Id, cancellationToken))
                .Text("✅ User ").Mention(targetUser)
                .Text($" unbanned from {result.ChatsAffected} chat(s)")
                .Build();

            return new CommandResult(response, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unban {User}",
                targetUser.ToLogDebug());
            return new CommandResult(TelegramMessage.Plain("❌ Failed to unban user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }
}

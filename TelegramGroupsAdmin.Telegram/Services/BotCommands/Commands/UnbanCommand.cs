using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /unban - Remove ban from user
/// </summary>
public class UnbanCommand : IBotCommand
{
    private readonly ILogger<UnbanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotModerationService _moderationService;

    public string Name => "unban";
    public string Description => "Remove ban from user";
    public string Usage => "/unban (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public UnbanCommand(
        ILogger<UnbanCommand> logger,
        IServiceProvider serviceProvider,
        IBotModerationService moderationService)
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
            return new CommandResult("❌ Please reply to a message from the user to unban.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return new CommandResult("❌ Could not identify target user.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        try
        {
            // Get executor actor
            var executor = Core.Models.Actor.FromTelegramUser(
                message.From!.Id,
                message.From.Username,
                message.From.FirstName,
                message.From.LastName);

            // Execute unban action through ModerationActionService
            var result = await _moderationService.UnbanUserAsync(
                new UnbanIntent
                {
                    User = UserIdentity.From(targetUser),
                    Executor = executor,
                    Reason = $"Manual unban command by {message.From?.Username ?? message.From?.Id.ToString() ?? "unknown"}",
                    RestoreTrust = false
                },
                cancellationToken);

            // Build response based on result
            if (!result.Success)
            {
                return new CommandResult($"❌ {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            var response = $"✅ User @{targetUser.Username ?? targetUser.Id.ToString()} unbanned from {result.ChatsAffected} chat(s)";

            return new CommandResult(response, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unban {User}",
                LogDisplayName.UserDebug(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id));
            return new CommandResult($"❌ Failed to unban user: {ex.Message}", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }
}

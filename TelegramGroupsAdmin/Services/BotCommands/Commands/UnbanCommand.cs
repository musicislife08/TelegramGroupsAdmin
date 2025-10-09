using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /unban - Remove ban from user
/// </summary>
public class UnbanCommand : IBotCommand
{
    private readonly ILogger<UnbanCommand> _logger;

    public string Name => "unban";
    public string Description => "Remove ban from user";
    public string Usage => "/unban (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;

    public UnbanCommand(ILogger<UnbanCommand> logger)
    {
        _logger = logger;
    }

    public Task<string> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return Task.FromResult("❌ Please reply to a message from the user to unban.");
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return Task.FromResult("❌ Could not identify target user.");
        }

        _logger.LogInformation(
            "Unban command executed by {ExecutorId} on user {TargetId} ({TargetUsername})",
            message.From?.Id,
            targetUser.Id,
            targetUser.Username);

        // TODO: Phase 2.3 Implementation
        // 1. Unban user from current chat
        // 2. Unban from all managed chats (if cross-chat ban exists)
        // 3. Update user_actions table
        // 4. Log to audit_log

        return Task.FromResult(
            $"✅ Ban removed for user @{targetUser.Username ?? targetUser.Id.ToString()}\n\n" +
            $"_Note: Unban implementation coming in Phase 2.3_");
    }
}

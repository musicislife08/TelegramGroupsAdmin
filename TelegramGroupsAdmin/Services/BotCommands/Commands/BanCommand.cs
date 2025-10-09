using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /ban - Ban user from all managed chats
/// </summary>
public class BanCommand : IBotCommand
{
    private readonly ILogger<BanCommand> _logger;

    public string Name => "ban";
    public string Description => "Ban user from all managed chats";
    public string Usage => "/ban (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;

    public BanCommand(ILogger<BanCommand> logger)
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
            return Task.FromResult("❌ Please reply to a message from the user to ban.");
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return Task.FromResult("❌ Could not identify target user.");
        }

        _logger.LogInformation(
            "Ban command executed by {ExecutorId} on user {TargetId} ({TargetUsername})",
            message.From?.Id,
            targetUser.Id,
            targetUser.Username);

        // TODO: Phase 2.3 Implementation
        // 1. Check if target is admin/owner (can't ban admins)
        // 2. Ban user from current chat
        // 3. Ban user from all managed chats (cross-chat ban)
        // 4. Save ban record to user_actions table
        // 5. Log to audit_log

        return Task.FromResult(
            $"✅ User @{targetUser.Username ?? targetUser.Id.ToString()} marked for ban\n\n" +
            $"_Note: Cross-chat ban implementation coming in Phase 2.3_");
    }
}

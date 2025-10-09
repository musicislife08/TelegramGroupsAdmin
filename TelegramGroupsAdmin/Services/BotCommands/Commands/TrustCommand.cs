using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /trust - Whitelist user to bypass spam detection
/// </summary>
public class TrustCommand : IBotCommand
{
    private readonly ILogger<TrustCommand> _logger;

    public string Name => "trust";
    public string Description => "Whitelist user (bypass spam detection)";
    public string Usage => "/trust (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;

    public TrustCommand(ILogger<TrustCommand> logger)
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
            return Task.FromResult("❌ Please reply to a message from the user to trust.");
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return Task.FromResult("❌ Could not identify target user.");
        }

        _logger.LogInformation(
            "Trust command executed by {ExecutorId} on user {TargetId} ({TargetUsername})",
            message.From?.Id,
            targetUser.Id,
            targetUser.Username);

        // TODO: Phase 2.3 Implementation
        // 1. Save trust action to user_actions table
        // 2. Mark as trusted for current chat or all chats (based on args)
        // 3. User messages bypass all spam detection
        // 4. Log to audit_log

        return Task.FromResult(
            $"✅ User @{targetUser.Username ?? targetUser.Id.ToString()} marked as trusted\n\n" +
            $"_Note: Trust implementation coming in Phase 2.3_");
    }
}

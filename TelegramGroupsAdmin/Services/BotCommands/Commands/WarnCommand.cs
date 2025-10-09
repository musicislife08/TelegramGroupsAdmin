using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /warn - Issue warning to user (auto-ban after threshold)
/// </summary>
public class WarnCommand : IBotCommand
{
    private readonly ILogger<WarnCommand> _logger;

    public string Name => "warn";
    public string Description => "Issue warning to user";
    public string Usage => "/warn (reply to message) [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;

    public WarnCommand(ILogger<WarnCommand> logger)
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
            return Task.FromResult("❌ Please reply to a message from the user to warn.");
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return Task.FromResult("❌ Could not identify target user.");
        }

        var reason = args.Length > 0 ? string.Join(" ", args) : "No reason provided";

        _logger.LogInformation(
            "Warn command executed by {ExecutorId} on user {TargetId} ({TargetUsername}) - Reason: {Reason}",
            message.From?.Id,
            targetUser.Id,
            targetUser.Username,
            reason);

        // TODO: Phase 2.3 Implementation
        // 1. Save warning to user_actions table
        // 2. Check warning count for user
        // 3. Auto-ban if threshold exceeded (e.g., 3 warnings)
        // 4. Send warning message to user via DM (if possible)
        // 5. Log to audit_log

        return Task.FromResult(
            $"⚠️ Warning issued to @{targetUser.Username ?? targetUser.Id.ToString()}\n" +
            $"Reason: {reason}\n\n" +
            $"_Note: Warning tracking and auto-ban coming in Phase 2.3_");
    }
}

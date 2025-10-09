using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /spam - Mark message as spam and take action
/// </summary>
public class SpamCommand : IBotCommand
{
    private readonly ILogger<SpamCommand> _logger;

    public string Name => "spam";
    public string Description => "Mark message as spam and delete it";
    public string Usage => "/spam (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;

    public SpamCommand(ILogger<SpamCommand> logger)
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
            return Task.FromResult("❌ Please reply to the spam message.");
        }

        var spamMessage = message.ReplyToMessage;
        var spamUserId = spamMessage.From?.Id;
        var spamUserName = spamMessage.From?.Username ?? spamMessage.From?.FirstName ?? "Unknown";

        _logger.LogInformation(
            "Spam command executed by {AdminId} on message {MessageId} from user {SpamUserId} ({SpamUserName})",
            message.From?.Id, spamMessage.MessageId, spamUserId, spamUserName);

        // TODO: Phase 2.3 Implementation
        // 0. Check if target user is admin/trusted - if so, reject with error message
        // 1. Delete the spam message
        // 2. Add spam sample to detection_results table
        // 3. Optionally ban user based on spam threshold
        // 4. Log to audit_log

        var response = $"✅ Message marked as spam from user @{spamUserName}\n\n" +
                      $"_Note: Full spam action (delete/ban) coming in Phase 2.3_";

        return Task.FromResult(response);
    }
}

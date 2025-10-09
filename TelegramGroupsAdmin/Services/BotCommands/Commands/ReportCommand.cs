using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /report - Report message for admin review
/// </summary>
public class ReportCommand : IBotCommand
{
    private readonly ILogger<ReportCommand> _logger;

    public string Name => "report";
    public string Description => "Report message for admin review";
    public string Usage => "/report (reply to message) [reason]";
    public int MinPermissionLevel => 0; // Anyone can report
    public bool RequiresReply => true;

    public ReportCommand(ILogger<ReportCommand> logger)
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
            return Task.FromResult("❌ Please reply to the message you want to report.");
        }

        var reportedMessage = message.ReplyToMessage;
        var reportedUser = reportedMessage.From;
        var reporter = message.From;

        if (reportedUser == null || reporter == null)
        {
            return Task.FromResult("❌ Could not identify users.");
        }

        var reason = args.Length > 0 ? string.Join(" ", args) : "No reason provided";

        _logger.LogInformation(
            "Report submitted by {ReporterId} ({ReporterUsername}) for message {MessageId} from user {ReportedId} ({ReportedUsername}) - Reason: {Reason}",
            reporter.Id,
            reporter.Username,
            reportedMessage.MessageId,
            reportedUser.Id,
            reportedUser.Username,
            reason);

        // TODO: Phase 2.3 Implementation
        // 1. Save report to detection_results or separate reports table
        // 2. Notify admins (via DM or admin chat)
        // 3. Add to review queue in admin UI
        // 4. Track report abuse (users who spam reports)
        // 5. Log to audit_log

        return Task.FromResult(
            $"✅ Message reported for admin review\n" +
            $"Reported user: @{reportedUser.Username ?? reportedUser.Id.ToString()}\n" +
            $"Reason: {reason}\n\n" +
            $"_Note: Admin notification system coming in Phase 2.3_");
    }
}

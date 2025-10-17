using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /report - Report message for admin review
/// </summary>
public class ReportCommand : IBotCommand
{
    private readonly ILogger<ReportCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "report";
    public string Description => "Report message for admin review";
    public string Usage => "/report (reply to message)";
    public int MinPermissionLevel => 0; // Anyone can report
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public ReportCommand(
        ILogger<ReportCommand> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return "❌ Please reply to the message you want to report.";
        }

        var reportedMessage = message.ReplyToMessage;
        var reportedUser = reportedMessage.From;
        var reporter = message.From;

        if (reportedUser == null || reporter == null)
        {
            return "❌ Could not identify users.";
        }

        // Save report to database
        using var scope = _serviceProvider.CreateScope();
        var reportsRepository = scope.ServiceProvider.GetRequiredService<IReportsRepository>();

        var now = DateTimeOffset.UtcNow;

        var report = new Report(
            Id: 0, // Will be assigned by database
            MessageId: reportedMessage.MessageId,
            ChatId: message.Chat.Id,
            ReportCommandMessageId: message.MessageId,
            ReportedByUserId: reporter.Id,
            ReportedByUserName: reporter.Username ?? reporter.FirstName,
            ReportedAt: now,
            Status: ReportStatus.Pending,
            ReviewedBy: null,
            ReviewedAt: null,
            ActionTaken: null,
            AdminNotes: null
        );

        var reportId = await reportsRepository.InsertAsync(report, cancellationToken);

        _logger.LogInformation(
            "Report {ReportId} submitted by {ReporterId} ({ReporterUsername}) for message {MessageId} from user {ReportedId} ({ReportedUsername})",
            reportId,
            reporter.Id,
            reporter.Username,
            reportedMessage.MessageId,
            reportedUser.Id,
            reportedUser.Username);

        return $"✅ Message reported for admin review (Report #{reportId})\n" +
               $"Reported user: @{reportedUser.Username ?? reportedUser.Id.ToString()}\n\n" +
               $"_Admins will review your report shortly._";
    }
}

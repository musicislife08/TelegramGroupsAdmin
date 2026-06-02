using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /report - Report message for admin review
/// Uses IReportService for unified report creation and notification handling
/// </summary>
public class ReportCommand(
    ILogger<ReportCommand> logger,
    IServiceProvider serviceProvider) : IBotCommand
{
    public string Name => "report";
    public string Description => "Report message for admin review";
    public string Usage => "/report (reply to message)";
    public int MinPermissionLevel => 0; // Anyone can report
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to the message you want to report."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var reportedMessage = message.ReplyToMessage;
        var reportedUser = reportedMessage.From;
        var reporter = message.From;

        if (reportedUser == null || reporter == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Could not identify users."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        using var scope = serviceProvider.CreateScope();
        var reportsRepository = scope.ServiceProvider.GetRequiredService<IReportsRepository>();
        var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();

        // Check for duplicate report (one pending report per message)
        var existingReport = await reportsRepository.GetExistingPendingContentReportAsync(
            reportedMessage.MessageId,
            message.Chat.Id,
            cancellationToken);

        if (existingReport != null)
        {
            var existingReporterName = existingReport.ReportedByUserName ?? "System";
            return new CommandResult(
                new TelegramMessageBuilder()
                    .Text("ℹ️ This message has already been reported.")
                    .LineBreak().LineBreak()
                    .Text($"📋 Report #{existingReport.Id}")
                    .LineBreak()
                    .Text("👤 Reported by: ")
                    .Text(existingReporterName)
                    .LineBreak()
                    .Text($"📅 Reported: {existingReport.ReportedAt:g}")
                    .LineBreak()
                    .Text($"📊 Status: {existingReport.Status}")
                    .LineBreak().LineBreak()
                    .Italic("Admins will review the report shortly.")
                    .Build(),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        var report = new Report(
            Id: 0, // Will be assigned by database
            MessageId: reportedMessage.MessageId,
            Chat: ChatIdentity.From(message.Chat),
            ReportCommandMessageId: message.MessageId,
            ReportedByUserId: reporter.Id,
            ReportedByUserName: reporter.Username ?? reporter.FirstName,
            ReportedAt: DateTimeOffset.UtcNow,
            Status: ReportStatus.Pending,
            ReviewedBy: null,
            ReviewedAt: null,
            ActionTaken: null,
            AdminNotes: null
        );

        var reporterActor = Actor.FromTelegramUser(
            reporter.Id, reporter.Username, reporter.FirstName, reporter.LastName);

        var result = await reportService.CreateReportAsync(
            report,
            reportedMessage,
            reporterActor,
            cancellationToken);

        logger.LogInformation(
            "Report {ReportId} submitted by {ReporterId} ({ReporterUsername}) for message {MessageId} from user {ReportedId} ({ReportedUsername})",
            result.ReportId,
            reporter.Id,
            reporter.Username,
            reportedMessage.MessageId,
            reportedUser.Id,
            reportedUser.Username);

        return new CommandResult(
            new TelegramMessageBuilder()
                .Text($"✅ Message reported for admin review (Report #{result.ReportId})")
                .LineBreak()
                .Text("Reported user: ")
                .Mention(new UserIdentity(reportedUser.Id, reportedUser.FirstName, reportedUser.LastName, reportedUser.Username))
                .LineBreak().LineBreak()
                .Italic("Admins will be notified shortly.")
                .Build(),
            DeleteCommandMessage,
            DeleteResponseAfterSeconds);
    }
}

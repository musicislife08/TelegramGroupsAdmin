using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using DataModels = TelegramGroupsAdmin.Data.Models;

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
            return new CommandResult("❌ Please reply to the message you want to report.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var reportedMessage = message.ReplyToMessage;
        var reportedUser = reportedMessage.From;
        var reporter = message.From;

        if (reportedUser == null || reporter == null)
        {
            return new CommandResult("❌ Could not identify users.", DeleteCommandMessage, DeleteResponseAfterSeconds);
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
                $"ℹ️ This message has already been reported.\n\n" +
                $"📋 Report #{existingReport.Id}\n" +
                $"👤 Reported by: {existingReporterName}\n" +
                $"📅 Reported: {existingReport.ReportedAt:g}\n" +
                $"📊 Status: {existingReport.Status}\n\n" +
                $"_Admins will review the report shortly._",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        var report = new Report(
            Id: 0, // Will be assigned by database
            MessageId: reportedMessage.MessageId,
            ChatId: message.Chat.Id,
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

        var result = await reportService.CreateReportAsync(
            report,
            reportedMessage,
            isAutomated: false,
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
            $"✅ Message reported for admin review (Report #{result.ReportId})\n" +
            $"Reported user: @{reportedUser.Username ?? reportedUser.Id.ToString()}\n\n" +
            $"_Admins will be notified shortly._",
            DeleteCommandMessage,
            DeleteResponseAfterSeconds);
    }
}

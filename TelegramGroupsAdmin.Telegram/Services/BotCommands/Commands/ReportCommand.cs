using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /report - Report message for admin review
/// Uses IReportService for unified report creation and notification handling
/// </summary>
public class ReportCommand(
    ILogger<ReportCommand> logger,
    IServiceProvider serviceProvider,
    IUserIdentityService identityService,
    IConfigService configService) : IBotCommand
{
    public string Name => "report";
    public string Description => "Report message for admin review";
    public string Usage => "/report (reply to message)";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Member; // everyone
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        UserIdentity sender,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to the message you want to report."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var reportedMessage = message.ReplyToMessage;
        if (reportedMessage.From == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Could not identify users."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var reportedUser = await identityService.ResolveAsync(reportedMessage.From.Id, cancellationToken);
        var masking = await configService.GetNameMaskingAsync(message.Chat.Id, cancellationToken);

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
            var reply = TelegramMessageBuilder.For(masking)
                .Text("ℹ️ This message has already been reported.")
                .LineBreak().LineBreak()
                .Text($"📋 Report #{existingReport.Id}")
                .LineBreak()
                .Text("👤 Reported by: ");
            // The stored name is a snapshot; resolve a Telegram reporter by id so a flagged name is masked.
            if (existingReport.ReportedByUserId is { } reporterId)
                reply.Mention(await identityService.ResolveAsync(reporterId, cancellationToken));
            else
                reply.Text(existingReport.ReportedByUserName ?? "System");

            return new CommandResult(
                reply
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
            ReportedByUserId: sender.Id,
            ReportedByUserName: sender.Username ?? sender.FirstName,
            ReportedAt: DateTimeOffset.UtcNow,
            Status: ReportStatus.Pending,
            ReviewedBy: null,
            ReviewedAt: null,
            ActionTaken: null,
            AdminNotes: null
        );

        var reporterActor = Actor.FromUserIdentity(sender);

        var result = await reportService.CreateReportAsync(
            report,
            reportedMessage,
            reporterActor,
            cancellationToken);

        logger.LogInformation(
            "Report {ReportId} submitted by {ReporterId} ({ReporterUsername}) for message {MessageId} from user {ReportedId} ({ReportedUsername})",
            result.ReportId,
            sender.Id,
            sender.Username,
            reportedMessage.MessageId,
            reportedUser.Id,
            reportedUser.Username);

        return new CommandResult(
            TelegramMessageBuilder.For(masking)
                .Text($"✅ Message reported for admin review (Report #{result.ReportId})")
                .LineBreak()
                .Text("Reported user: ")
                .Mention(reportedUser)
                .LineBreak().LineBreak()
                .Italic("Admins will be notified shortly.")
                .Build(),
            DeleteCommandMessage,
            DeleteResponseAfterSeconds);
    }
}

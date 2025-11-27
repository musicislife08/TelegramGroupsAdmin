using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using DataModels = TelegramGroupsAdmin.Data.Models;



namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /report - Report message for admin review
/// Notifies all chat admins via DM if available, falls back to chat mention
/// Phase 5.1: Sends notification through notification preferences system
/// </summary>
public class ReportCommand : IBotCommand
{
    private readonly ILogger<ReportCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUserMessagingService _messagingService;
    private readonly INotificationService _notificationService;

    public string Name => "report";
    public string Description => "Report message for admin review";
    public string Usage => "/report (reply to message)";
    public int MinPermissionLevel => 0; // Anyone can report
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public ReportCommand(
        ILogger<ReportCommand> logger,
        IServiceProvider serviceProvider,
        IUserMessagingService messagingService,
        INotificationService notificationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _messagingService = messagingService;
        _notificationService = notificationService;
    }

    public async Task<CommandResult> ExecuteAsync(
        ITelegramBotClient botClient,
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

        // Save report to database
        using var scope = _serviceProvider.CreateScope();
        var reportsRepository = scope.ServiceProvider.GetRequiredService<IReportsRepository>();

        // Check for duplicate report (one pending report per message)
        var existingReport = await reportsRepository.GetExistingPendingReportAsync(
            reportedMessage.MessageId,
            message.Chat.Id,
            cancellationToken);

        if (existingReport != null)
        {
            var reporterName = existingReport.ReportedByUserName ?? "System";
            return new CommandResult(
                $"ℹ️ This message has already been reported.\n\n" +
                $"📋 Report #{existingReport.Id}\n" +
                $"👤 Reported by: {reporterName}\n" +
                $"📅 Reported: {existingReport.ReportedAt:g}\n" +
                $"📊 Status: {existingReport.Status}\n\n" +
                $"_Admins will review the report shortly._",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        var now = DateTimeOffset.UtcNow;

        var report = new Report(
            Id: 0, // Will be assigned by database
            MessageId: reportedMessage.MessageId,
            ChatId: message.Chat.Id,
            ReportCommandMessageId: message.MessageId,
            ReportedByUserId: reporter.Id,
            ReportedByUserName: reporter.Username ?? reporter.FirstName,
            ReportedAt: now,
            Status: DataModels.ReportStatus.Pending,
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

        // Send notification to chat admins (Phase 5.1)
        var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
        var messagePreview = reportedMessage.Text?.Length > 100
            ? reportedMessage.Text.Substring(0, 100) + "..."
            : reportedMessage.Text ?? "[Media message]";

        _ = _notificationService.SendChatNotificationAsync(
            chatId: message.Chat.Id,
            eventType: NotificationEventType.MessageReported,
            subject: $"Message Reported in {chatName}",
            message: $"A user has reported a message for admin review.\n\n" +
                     $"Report ID: #{reportId}\n" +
                     $"Reported by: @{reporter.Username ?? reporter.FirstName ?? reporter.Id.ToString()}\n" +
                     $"Reported user: @{reportedUser.Username ?? reportedUser.FirstName ?? reportedUser.Id.ToString()}\n" +
                     $"Message preview: {messagePreview}\n\n" +
                     $"Please review this report in the Reports tab of the admin panel.",
            ct: cancellationToken);

        // Notify all admins of the new report via direct message (immediate alert)
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var admins = await chatAdminsRepository.GetChatAdminsAsync(message.Chat.Id, cancellationToken);
        var adminUserIds = admins.Select(a => a.TelegramId).ToList();

        if (adminUserIds.Any())
        {
            var reportNotification = $"🚨 **New Report #{reportId}**\n\n" +
                                    $"**Chat:** {chatName}\n" +
                                    $"**Reported by:** @{reporter.Username ?? reporter.FirstName ?? reporter.Id.ToString()}\n" +
                                    $"**Reported user:** @{reportedUser.Username ?? reportedUser.FirstName ?? reportedUser.Id.ToString()}\n" +
                                    $"**Message:** {messagePreview}\n\n" +
                                    $"[Jump to message](https://t.me/c/{Math.Abs(message.Chat.Id).ToString().TrimStart('-')}/{reportedMessage.MessageId})\n\n" +
                                    $"Review in the Reports tab or use moderation commands.";

            var results = await _messagingService.SendToMultipleUsersAsync(
                botClient,
                userIds: adminUserIds,
                chatId: message.Chat.Id,
                messageText: reportNotification,
                replyToMessageId: reportedMessage.MessageId,
                cancellationToken);

            var dmCount = results.Count(r => r.DeliveryMethod == MessageDeliveryMethod.PrivateDm);
            var mentionCount = results.Count(r => r.DeliveryMethod == MessageDeliveryMethod.ChatMention);

            _logger.LogInformation(
                "Report {ReportId} notification sent to {TotalAdmins} admins ({DmCount} via DM, {MentionCount} via chat mention)",
                reportId, results.Count, dmCount, mentionCount);

            return new CommandResult(
                $"✅ Message reported for admin review (Report #{reportId})\n" +
                $"Reported user: @{reportedUser.Username ?? reportedUser.Id.ToString()}\n" +
                $"Notified {dmCount} admin(s) via DM, {mentionCount} in chat\n\n" +
                $"_Admins will review your report shortly._",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        return new CommandResult(
            $"✅ Message reported for admin review (Report #{reportId})\n" +
            $"Reported user: @{reportedUser.Username ?? reportedUser.Id.ToString()}\n\n" +
            $"_Admins will review your report shortly._",
            DeleteCommandMessage,
            DeleteResponseAfterSeconds);
    }
}

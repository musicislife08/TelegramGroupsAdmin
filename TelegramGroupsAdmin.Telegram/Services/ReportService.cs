using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for creating reports and sending notifications
/// Consolidates report creation logic used by both /report command and automated detection
/// </summary>
public class ReportService(
    IReportsRepository reportsRepository,
    IJobTriggerService jobTriggerService,
    IAuditService auditService,
    IUserMessagingService messagingService,
    IChatAdminsRepository chatAdminsRepository,
    ILogger<ReportService> logger) : IReportService
{
    public async Task<ReportCreationResult> CreateReportAsync(
        Report report,
        Message? originalMessage,
        bool isAutomated,
        CancellationToken cancellationToken = default)
    {
        // 1. Insert report into database
        var reportId = await reportsRepository.InsertAsync(report, cancellationToken);

        logger.LogInformation(
            "Report {ReportId} created: ChatId={ChatId}, MessageId={MessageId}, IsAutomated={IsAutomated}, ReportedBy={ReportedBy}",
            reportId,
            report.ChatId,
            report.MessageId,
            isAutomated,
            report.ReportedByUserName ?? "Auto-Detection");

        // 2. Log audit event
        var actor = isAutomated
            ? Actor.AutoDetection
            : report.ReportedByUserId.HasValue
                ? Actor.FromTelegramUser(report.ReportedByUserId.Value, report.ReportedByUserName)
                : Actor.Unknown;

        var target = originalMessage?.From != null
            ? Actor.FromTelegramUser(
                originalMessage.From.Id,
                originalMessage.From.Username,
                originalMessage.From.FirstName,
                originalMessage.From.LastName)
            : null;

        await auditService.LogEventAsync(
            AuditEventType.ReportCreated,
            actor,
            target,
            value: $"Report #{reportId} for message {report.MessageId} in chat {report.ChatId}",
            cancellationToken: cancellationToken);

        // 3. Send notification via INotificationService (respects user preferences)
        var chatName = originalMessage?.Chat.Title ?? $"Chat {report.ChatId}";
        var messagePreview = GetMessagePreview(originalMessage, report);
        var reportedUserName = GetReportedUserName(originalMessage, report);

        var notificationSubject = isAutomated
            ? $"Auto-Detected Report in {chatName}"
            : $"Message Reported in {chatName}";

        var notificationMessage = BuildNotificationMessage(
            reportId, chatName, report.ReportedByUserName, reportedUserName, messagePreview, isAutomated);

        // Send notification via Quartz job (reliable delivery instead of fire-and-forget)
        var notificationPayload = new SendChatNotificationPayload(
            ChatId: report.ChatId,
            EventType: NotificationEventType.MessageReported,
            Subject: notificationSubject,
            Message: notificationMessage);

        await jobTriggerService.TriggerNowAsync(
            "SendChatNotificationJob",
            notificationPayload,
            cancellationToken);

        // 4. Send direct DM notifications to chat admins (immediate alert fallback)
        var (dmCount, mentionCount) = await SendDirectDmNotificationsAsync(
            reportId,
            report,
            originalMessage,
            chatName,
            messagePreview,
            reportedUserName,
            isAutomated,
            cancellationToken);

        return new ReportCreationResult(reportId, dmCount, mentionCount);
    }

    private async Task<(int DmCount, int MentionCount)> SendDirectDmNotificationsAsync(
        long reportId,
        Report report,
        Message? originalMessage,
        string chatName,
        string messagePreview,
        string reportedUserName,
        bool isAutomated,
        CancellationToken cancellationToken)
    {
        try
        {
            var admins = await chatAdminsRepository.GetChatAdminsAsync(report.ChatId, cancellationToken);
            var adminUserIds = admins.Select(a => a.TelegramId).ToList();

            if (!adminUserIds.Any())
            {
                logger.LogDebug("No admins found for chat {ChatId}, skipping DM notifications", report.ChatId);
                return (0, 0);
            }

            var reporterName = isAutomated
                ? "Auto-Detection System"
                : $"@{report.ReportedByUserName ?? report.ReportedByUserId?.ToString() ?? "Unknown"}";

            var jumpLink = originalMessage != null
                ? $"[Jump to message](https://t.me/c/{Math.Abs(report.ChatId)}/{originalMessage.MessageId})"
                : "";

            var reportNotification = isAutomated
                ? $"🚨 **New Auto-Detected Report #{reportId}**\n\n" +
                  $"**Chat:** {chatName}\n" +
                  $"**Reported by:** {reporterName}\n" +
                  $"**Reported user:** {reportedUserName}\n" +
                  $"**Message:** {messagePreview}\n\n" +
                  (string.IsNullOrEmpty(jumpLink) ? "" : $"{jumpLink}\n\n") +
                  $"Review in the Reports tab or use moderation commands."
                : $"🚨 **New Report #{reportId}**\n\n" +
                  $"**Chat:** {chatName}\n" +
                  $"**Reported by:** {reporterName}\n" +
                  $"**Reported user:** {reportedUserName}\n" +
                  $"**Message:** {messagePreview}\n\n" +
                  (string.IsNullOrEmpty(jumpLink) ? "" : $"{jumpLink}\n\n") +
                  $"Review in the Reports tab or use moderation commands.";

            var results = await messagingService.SendToMultipleUsersAsync(
                userIds: adminUserIds,
                chat: originalMessage!.Chat,
                messageText: reportNotification,
                replyToMessageId: originalMessage.MessageId,
                cancellationToken: cancellationToken);

            var dmCount = results.Count(r => r.DeliveryMethod == MessageDeliveryMethod.PrivateDm);
            var mentionCount = results.Count(r => r.DeliveryMethod == MessageDeliveryMethod.ChatMention);

            logger.LogInformation(
                "Report {ReportId} DM notification sent to {TotalAdmins} admins ({DmCount} via DM, {MentionCount} via chat mention)",
                reportId, results.Count, dmCount, mentionCount);

            return (dmCount, mentionCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send DM notifications for report {ReportId}", reportId);
            return (0, 0);
        }
    }

    private static string GetMessagePreview(Message? message, Report report)
    {
        if (message?.Text != null)
        {
            return message.Text.Length > 100
                ? message.Text[..100] + "..."
                : message.Text;
        }

        if (message?.Caption != null)
        {
            return message.Caption.Length > 100
                ? message.Caption[..100] + "..."
                : message.Caption;
        }

        return "[Media message]";
    }

    private static string GetReportedUserName(Message? message, Report report)
    {
        if (message?.From != null)
        {
            return $"@{message.From.Username ?? message.From.FirstName ?? message.From.Id.ToString()}";
        }

        return "Unknown";
    }

    private static string BuildNotificationMessage(
        long reportId,
        string chatName,
        string? reporterName,
        string reportedUserName,
        string messagePreview,
        bool isAutomated)
    {
        var reporter = isAutomated
            ? "Auto-Detection System"
            : $"@{reporterName ?? "Unknown"}";

        return $"Report ID: #{reportId}\n" +
               $"Reported by: {reporter}\n" +
               $"Reported user: {reportedUserName}\n" +
               $"Message preview: {messagePreview}\n\n" +
               $"Please review this report in the Reports tab of the admin panel.";
    }
}

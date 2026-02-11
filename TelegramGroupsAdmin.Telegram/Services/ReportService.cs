using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Repositories;
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
    IMessageHistoryRepository messageHistoryRepository,
    ILogger<ReportService> logger) : IReportService
{
    public async Task<ReportCreationResult> CreateReportAsync(
        Report report,
        Message? originalMessage,
        bool isAutomated,
        CancellationToken cancellationToken = default)
    {
        // 1. Insert report into database
        var reportId = await reportsRepository.InsertContentReportAsync(report, cancellationToken);

        logger.LogInformation(
            "Report {ReportId} created: ChatId={ChatId}, MessageId={MessageId}, IsAutomated={IsAutomated}, ReportedBy={ReportedBy}",
            reportId,
            report.Chat.Id,
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
            value: $"Report #{reportId} for message {report.MessageId} in chat {report.Chat.Id}",
            cancellationToken: cancellationToken);

        // 3. Send notification via INotificationService (respects user preferences)
        var chatName = report.Chat.ChatName ?? $"Chat {report.Chat.Id}";
        var messagePreview = GetMessagePreview(originalMessage, report);
        var reportedUserName = GetReportedUserName(originalMessage, report);

        var notificationSubject = isAutomated
            ? $"Auto-Detected Report in {chatName}"
            : $"Message Reported in {chatName}";

        var notificationMessage = BuildNotificationMessage(
            reportId, chatName, report.ReportedByUserName, reportedUserName, messagePreview, isAutomated);

        // Get reported user's Telegram ID for moderation action buttons
        var reportedUserId = originalMessage?.From?.Id;

        // Get photo path from stored message for DM with image
        string? photoPath = null;
        var storedMessage = await messageHistoryRepository.GetMessageAsync(report.MessageId, cancellationToken);
        if (storedMessage?.PhotoLocalPath != null)
        {
            // PhotoLocalPath is stored as relative (e.g., "full/{chatId}/{messageId}.jpg")
            // Need to construct absolute path for DM delivery
            photoPath = Path.Combine("/data", "media", storedMessage.PhotoLocalPath);
        }

        // Send notification via Quartz job (reliable delivery instead of fire-and-forget)
        // Now includes report metadata for action buttons and photo for DM with image
        var notificationPayload = new SendChatNotificationPayload(
            Chat: report.Chat,
            EventType: NotificationEventType.MessageReported,
            Subject: notificationSubject,
            Message: notificationMessage,
            ReportId: reportId,
            PhotoPath: photoPath,
            ReportedUserId: reportedUserId,
            ReportType: ReportType.ContentReport);

        await jobTriggerService.TriggerNowAsync(
            "SendChatNotificationJob",
            notificationPayload,
            cancellationToken);

        return new ReportCreationResult(reportId);
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

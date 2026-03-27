using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for creating reports and sending notifications
/// Consolidates report creation logic used by both /report command and automated detection
/// </summary>
public class ReportService(
    IReportsRepository reportsRepository,
    INotificationService notificationService,
    IAuditService auditService,
    IMessageHistoryRepository messageHistoryRepository,
    ReportMetrics reportMetrics,
    ILogger<ReportService> logger) : IReportService
{
    public async Task<ReportCreationResult> CreateReportAsync(
        Report report,
        Message originalMessage,
        bool isAutomated,
        CancellationToken cancellationToken = default)
    {
        // Guard: every report must have a known sender for moderation actions to work
        if (originalMessage.From is null)
        {
            logger.LogWarning("Report attempted without a message sender — skipping. Chat={Chat}, MessageId={MessageId}",
                report.Chat.ToLogDebug(), report.MessageId);
            return new ReportCreationResult(0);
        }

        // 1. Insert report into database
        var reportId = await reportsRepository.InsertContentReportAsync(report, cancellationToken);

        reportMetrics.RecordReportCreated("content", isAutomated ? "auto" : "user");

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

        var target = Actor.FromTelegramUser(
            originalMessage.From.Id,
            originalMessage.From.Username,
            originalMessage.From.FirstName,
            originalMessage.From.LastName);

        await auditService.LogEventAsync(
            AuditEventType.ReportCreated,
            actor,
            target,
            value: $"Report #{reportId} for message {report.MessageId} in chat {report.Chat.Id}",
            cancellationToken: cancellationToken);

        // 3. Send notification via typed notification service
        var messagePreview = GetMessagePreview(originalMessage, report);

        // Build reported user identity from original message (From guaranteed non-null by guard above)
        var reportedUser = UserIdentity.From(originalMessage.From);

        // Get photo path from stored message for DM with image
        string? photoPath = null;
        var storedMessage = await messageHistoryRepository.GetMessageAsync(report.MessageId, report.Chat.Id, cancellationToken);
        if (storedMessage?.PhotoLocalPath != null)
        {
            photoPath = Path.Combine("/data", "media", storedMessage.PhotoLocalPath);
        }

        // Fire-and-forget — notification delivery should not block report creation
        _ = notificationService.SendReportNotificationAsync(
            chat: report.Chat,
            reportedUser: reportedUser,
            reporterUserId: report.ReportedByUserId,
            reporterName: report.ReportedByUserName,
            isAutomated: isAutomated,
            messagePreview: messagePreview,
            photoPath: photoPath,
            reportId: reportId,
            reportType: ReportType.ContentReport,
            ct: CancellationToken.None);

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

}

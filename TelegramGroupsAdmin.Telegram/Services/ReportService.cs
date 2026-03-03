using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Extensions;
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

        // 3. Send notification via typed notification service
        var messagePreview = GetMessagePreview(originalMessage, report);

        // Build reported user identity from original message
        UserIdentity? reportedUser = originalMessage?.From != null
            ? UserIdentity.From(originalMessage.From)
            : null;

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
            ct: cancellationToken);

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

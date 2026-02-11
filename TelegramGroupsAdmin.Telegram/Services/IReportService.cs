using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of creating a report
/// </summary>
public record ReportCreationResult(
    long ReportId);

/// <summary>
/// Service for creating reports and sending notifications
/// Consolidates report creation logic used by both /report command and automated detection
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Create a report and send notifications to chat admins
    /// </summary>
    /// <param name="report">The report to create</param>
    /// <param name="originalMessage">The Telegram message being reported (for context in notifications)</param>
    /// <param name="isAutomated">True if this is an automated detection, false if user-submitted</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing report ID and notification counts</returns>
    Task<ReportCreationResult> CreateReportAsync(
        Report report,
        Message? originalMessage,
        bool isAutomated,
        CancellationToken cancellationToken = default);
}

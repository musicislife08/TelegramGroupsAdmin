using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

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
    /// <param name="reporter">
    /// The actor submitting the report. Pass <see cref="Actor.AutoDetection"/> (or another
    /// system actor such as <see cref="Actor.Cas"/>) for automated reports, or
    /// <see cref="Actor.FromTelegramUser(long, string?, string?, string?)"/> for user-submitted reports.
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing report ID and notification counts</returns>
    Task<ReportCreationResult> CreateReportAsync(
        Report report,
        Message originalMessage,
        Actor reporter,
        CancellationToken cancellationToken = default);
}

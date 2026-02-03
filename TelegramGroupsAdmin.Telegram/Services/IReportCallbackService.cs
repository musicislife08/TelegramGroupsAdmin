using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Application-level service for handling report moderation callback queries from inline buttons in DMs.
/// Orchestrates the report review workflow, calling bot services for Telegram operations
/// and routing to type-specific handlers based on ReportType (ContentReport, ImpersonationAlert, ExamFailure).
/// </summary>
public interface IReportCallbackService
{
    /// <summary>
    /// Returns true if this handler can handle the given callback data.
    /// Supports both legacy 'rpt:' and new 'rev:' prefixes.
    /// </summary>
    bool CanHandle(string callbackData);

    /// <summary>
    /// Handle a review action callback query.
    /// Routes to appropriate handler based on ReviewType.
    /// </summary>
    Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default);
}

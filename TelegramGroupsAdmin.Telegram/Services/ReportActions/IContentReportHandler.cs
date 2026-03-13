using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

internal interface IContentReportHandler
{
    Task<ReviewActionResult> SpamAsync(long reportId, Actor executor, CancellationToken ct);
    Task<ReviewActionResult> BanAsync(long reportId, Actor executor, CancellationToken ct);
    Task<ReviewActionResult> WarnAsync(long reportId, Actor executor, CancellationToken ct);
    Task<ReviewActionResult> DismissAsync(long reportId, Actor executor, string? reason, CancellationToken ct);
}

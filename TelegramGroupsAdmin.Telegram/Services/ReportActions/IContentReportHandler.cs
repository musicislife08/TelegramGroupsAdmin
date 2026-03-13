using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

internal interface IContentReportHandler
{
    Task<ReviewActionResult> SpamAsync(long reportId, Actor executor, CancellationToken cancellationToken);
    Task<ReviewActionResult> BanAsync(long reportId, Actor executor, CancellationToken cancellationToken);
    Task<ReviewActionResult> WarnAsync(long reportId, Actor executor, CancellationToken cancellationToken);
    Task<ReviewActionResult> DismissAsync(long reportId, Actor executor, string? reason, CancellationToken cancellationToken);
}

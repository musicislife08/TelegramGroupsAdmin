using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

internal interface IProfileScanHandler
{
    Task<ReviewActionResult> BanAsync(long alertId, Actor executor, CancellationToken cancellationToken);
    Task<ReviewActionResult> KickAsync(long alertId, Actor executor, CancellationToken cancellationToken);
    Task<ReviewActionResult> AllowAsync(long alertId, Actor executor, CancellationToken cancellationToken);
}

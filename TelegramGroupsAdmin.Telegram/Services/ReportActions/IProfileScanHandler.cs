using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

internal interface IProfileScanHandler
{
    Task<ReviewActionResult> BanAsync(long alertId, Actor executor, CancellationToken ct);
    Task<ReviewActionResult> KickAsync(long alertId, Actor executor, CancellationToken ct);
    Task<ReviewActionResult> AllowAsync(long alertId, Actor executor, CancellationToken ct);
}

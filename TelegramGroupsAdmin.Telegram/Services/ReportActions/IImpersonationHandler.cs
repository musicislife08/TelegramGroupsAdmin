using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

internal interface IImpersonationHandler
{
    Task<ReviewActionResult> ConfirmAsync(long alertId, Actor executor, CancellationToken ct);
    Task<ReviewActionResult> DismissAsync(long alertId, Actor executor, CancellationToken ct);
    Task<ReviewActionResult> TrustAsync(long alertId, Actor executor, CancellationToken ct);
}

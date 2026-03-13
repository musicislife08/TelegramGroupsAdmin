using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

internal interface IExamHandler
{
    Task<ReviewActionResult> ApproveAsync(long examId, Actor executor, CancellationToken cancellationToken);
    Task<ReviewActionResult> DenyAsync(long examId, Actor executor, CancellationToken cancellationToken);
    Task<ReviewActionResult> DenyAndBanAsync(long examId, Actor executor, CancellationToken cancellationToken);
}

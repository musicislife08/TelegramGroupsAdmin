using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Singleton orchestrator for all report actions. Owns per-report-ID semaphore locking
/// and creates a fresh DI scope per call. Delegates to type-specific scoped handlers.
/// </summary>
internal sealed class ReportActionsService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReportActionsService> logger) : IReportActionsService
{
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _reportLocks = new();

    // Content report actions
    public Task<ReviewActionResult> HandleContentSpamAsync(long reportId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(reportId, scope =>
            scope.ServiceProvider.GetRequiredService<IContentReportHandler>().SpamAsync(reportId, executor, ct), ct);

    public Task<ReviewActionResult> HandleContentBanAsync(long reportId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(reportId, scope =>
            scope.ServiceProvider.GetRequiredService<IContentReportHandler>().BanAsync(reportId, executor, ct), ct);

    public Task<ReviewActionResult> HandleContentWarnAsync(long reportId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(reportId, scope =>
            scope.ServiceProvider.GetRequiredService<IContentReportHandler>().WarnAsync(reportId, executor, ct), ct);

    public Task<ReviewActionResult> HandleContentDismissAsync(long reportId, Actor executor, string? reason, CancellationToken ct)
        => ExecuteWithLockAsync(reportId, scope =>
            scope.ServiceProvider.GetRequiredService<IContentReportHandler>().DismissAsync(reportId, executor, reason, ct), ct);

    // Profile scan actions
    public Task<ReviewActionResult> HandleProfileScanBanAsync(long alertId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IProfileScanHandler>().BanAsync(alertId, executor, ct), ct);

    public Task<ReviewActionResult> HandleProfileScanKickAsync(long alertId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IProfileScanHandler>().KickAsync(alertId, executor, ct), ct);

    public Task<ReviewActionResult> HandleProfileScanAllowAsync(long alertId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IProfileScanHandler>().AllowAsync(alertId, executor, ct), ct);

    // Impersonation actions
    public Task<ReviewActionResult> HandleImpersonationConfirmAsync(long alertId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IImpersonationHandler>().ConfirmAsync(alertId, executor, ct), ct);

    public Task<ReviewActionResult> HandleImpersonationDismissAsync(long alertId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IImpersonationHandler>().DismissAsync(alertId, executor, ct), ct);

    public Task<ReviewActionResult> HandleImpersonationTrustAsync(long alertId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IImpersonationHandler>().TrustAsync(alertId, executor, ct), ct);

    // Exam actions
    public Task<ReviewActionResult> HandleExamApproveAsync(long examId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(examId, scope =>
            scope.ServiceProvider.GetRequiredService<IExamHandler>().ApproveAsync(examId, executor, ct), ct);

    public Task<ReviewActionResult> HandleExamDenyAsync(long examId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(examId, scope =>
            scope.ServiceProvider.GetRequiredService<IExamHandler>().DenyAsync(examId, executor, ct), ct);

    public Task<ReviewActionResult> HandleExamDenyAndBanAsync(long examId, Actor executor, CancellationToken ct)
        => ExecuteWithLockAsync(examId, scope =>
            scope.ServiceProvider.GetRequiredService<IExamHandler>().DenyAndBanAsync(examId, executor, ct), ct);

    private async Task<ReviewActionResult> ExecuteWithLockAsync(
        long reportId,
        Func<IServiceScope, Task<ReviewActionResult>> action,
        CancellationToken ct)
    {
        var semaphore = _reportLocks.GetOrAdd(reportId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            using var scope = scopeFactory.CreateScope();
            return await action(scope);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report action failed for report {ReportId}", reportId);
            return new ReviewActionResult(Success: false, Message: "Action failed unexpectedly. Check logs for details.");
        }
        finally
        {
            semaphore.Release();
            _reportLocks.TryRemove(reportId, out _);
        }
    }
}

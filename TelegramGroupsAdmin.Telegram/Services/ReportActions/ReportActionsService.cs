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
    private sealed class ReportLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount;
    }

    private readonly ConcurrentDictionary<long, ReportLock> _reportLocks = new();

    // Content report actions
    public Task<ReviewActionResult> HandleContentSpamAsync(long reportId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(reportId, scope =>
            scope.ServiceProvider.GetRequiredService<IContentReportHandler>()
                .SpamAsync(reportId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleContentBanAsync(long reportId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(reportId, scope =>
            scope.ServiceProvider.GetRequiredService<IContentReportHandler>()
                .BanAsync(reportId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleContentWarnAsync(long reportId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(reportId, scope =>
            scope.ServiceProvider.GetRequiredService<IContentReportHandler>()
                .WarnAsync(reportId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleContentDismissAsync(long reportId, Actor executor, string? reason, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(reportId, scope =>
            scope.ServiceProvider.GetRequiredService<IContentReportHandler>()
                .DismissAsync(reportId, executor, reason, cancellationToken: cancellationToken), cancellationToken);

    // Profile scan actions
    public Task<ReviewActionResult> HandleProfileScanBanAsync(long alertId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IProfileScanHandler>()
                .BanAsync(alertId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleProfileScanKickAsync(long alertId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IProfileScanHandler>()
                .KickAsync(alertId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleProfileScanAllowAsync(long alertId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IProfileScanHandler>()
                .AllowAsync(alertId, executor, cancellationToken: cancellationToken), cancellationToken);

    // Impersonation actions
    public Task<ReviewActionResult> HandleImpersonationConfirmAsync(long alertId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IImpersonationHandler>()
                .ConfirmAsync(alertId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleImpersonationDismissAsync(long alertId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IImpersonationHandler>()
                .DismissAsync(alertId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleImpersonationTrustAsync(long alertId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(alertId, scope =>
            scope.ServiceProvider.GetRequiredService<IImpersonationHandler>()
                .TrustAsync(alertId, executor, cancellationToken: cancellationToken), cancellationToken);

    // Exam actions
    public Task<ReviewActionResult> HandleExamApproveAsync(long examId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(examId, scope =>
            scope.ServiceProvider.GetRequiredService<IExamHandler>()
                .ApproveAsync(examId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleExamDenyAsync(long examId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(examId, scope =>
            scope.ServiceProvider.GetRequiredService<IExamHandler>()
                .DenyAsync(examId, executor, cancellationToken: cancellationToken), cancellationToken);

    public Task<ReviewActionResult> HandleExamDenyAndBanAsync(long examId, Actor executor, CancellationToken cancellationToken)
        => ExecuteWithLockAsync(examId, scope =>
            scope.ServiceProvider.GetRequiredService<IExamHandler>()
                .DenyAndBanAsync(examId, executor, cancellationToken: cancellationToken), cancellationToken);

    private async Task<ReviewActionResult> ExecuteWithLockAsync(
        long reportId,
        Func<IServiceScope, Task<ReviewActionResult>> action,
        CancellationToken cancellationToken)
    {
        var entry = _reportLocks.GetOrAdd(reportId, _ => new ReportLock());
        Interlocked.Increment(ref entry.ReferenceCount);
        var acquired = false;
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            acquired = true;

            await using var scope = scopeFactory.CreateAsyncScope();
            return await action(scope);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report action failed for report {ReportId}", reportId);
            return new ReviewActionResult(Success: false, Message: "Action failed unexpectedly. Check logs for details.");
        }
        finally
        {
            if (acquired)
                entry.Semaphore.Release();
            if (Interlocked.Decrement(ref entry.ReferenceCount) == 0)
                _reportLocks.TryRemove(new KeyValuePair<long, ReportLock>(reportId, entry));
        }
    }
}

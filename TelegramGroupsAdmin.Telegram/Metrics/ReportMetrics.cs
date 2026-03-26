using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelegramGroupsAdmin.Telegram.Metrics;

/// <summary>
/// Metrics for report creation, resolution, and pending count.
/// Pending count is eventually consistent — resets to 0 on app restart.
/// </summary>
public sealed class ReportMetrics
{
    private readonly Counter<long> _createdTotal;
    private readonly Counter<long> _resolvedTotal;
    private readonly Histogram<double> _resolutionDuration;

    private long _pendingCount;

    public ReportMetrics()
    {
        var meter = new Meter("TelegramGroupsAdmin.Reports");

        _createdTotal = meter.CreateCounter<long>(
            "tga.reports.created_total",
            description: "Reports created by type and source");

        _resolvedTotal = meter.CreateCounter<long>(
            "tga.reports.resolved_total",
            description: "Reports resolved by type and action");

        _resolutionDuration = meter.CreateHistogram<double>(
            "tga.reports.resolution.duration",
            unit: "ms",
            description: "Report resolution duration by type");

        meter.CreateObservableGauge(
            "tga.reports.pending_count",
            () => Interlocked.Read(ref _pendingCount),
            description: "Current total pending reports");
    }

    public void RecordReportCreated(string type, string source)
    {
        _createdTotal.Add(1, new TagList { { "type", type }, { "source", source } });
        Interlocked.Increment(ref _pendingCount);
    }

    public void RecordReportResolved(string type, string action, double durationMs)
    {
        _resolvedTotal.Add(1, new TagList { { "type", type }, { "action", action } });
        _resolutionDuration.Record(durationMs, new TagList { { "type", type } });
    }

    /// <summary>
    /// Decrement the pending report count. Call when a report resolution is attempted
    /// (regardless of success/failure) since the report is no longer awaiting a decision.
    /// </summary>
    public void DecrementPending()
    {
        Interlocked.Decrement(ref _pendingCount);
    }
}

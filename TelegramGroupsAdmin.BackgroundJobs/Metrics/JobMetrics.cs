using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelegramGroupsAdmin.BackgroundJobs.Metrics;

/// <summary>
/// Metrics for Quartz.NET background job execution.
/// Replaces flat counters from TelemetryConstants.
/// </summary>
public sealed class JobMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Jobs");

    private readonly Counter<long> _executionsTotal;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _rowsAffectedTotal;

    public JobMetrics()
    {
        _executionsTotal = _meter.CreateCounter<long>(
            "tga.jobs.executions_total",
            description: "Job execution counts by name and status");

        _duration = _meter.CreateHistogram<double>(
            "tga.jobs.duration",
            unit: "ms",
            description: "Job execution time by name");

        _rowsAffectedTotal = _meter.CreateCounter<long>(
            "tga.jobs.rows_affected_total",
            description: "Rows deleted or processed by cleanup jobs");
    }

    public void RecordJobExecution(string jobName, bool success, double durationMs)
    {
        var tags = new TagList
        {
            { "job_name", jobName },
            { "status", success ? "success" : "failure" }
        };
        _executionsTotal.Add(1, tags);
        _duration.Record(durationMs, new TagList { { "job_name", jobName } });
    }

    public void RecordRowsAffected(string jobName, long count)
    {
        if (count > 0)
            _rowsAffectedTotal.Add(count, new TagList { { "job_name", jobName } });
    }
}

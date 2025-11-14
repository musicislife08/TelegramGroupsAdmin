using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelegramGroupsAdmin.Core.Telemetry;

/// <summary>
/// Centralized telemetry constants for OpenTelemetry instrumentation.
/// Provides ActivitySources for distributed tracing and Meters for custom metrics.
/// </summary>
public static class TelemetryConstants
{
    // =========================================================================
    // ActivitySources for Distributed Tracing
    // =========================================================================

    /// <summary>
    /// ActivitySource for spam detection pipeline operations.
    /// Used to trace message analysis, algorithm execution, and detection results.
    /// </summary>
    public static readonly ActivitySource SpamDetection = new("TelegramGroupsAdmin.SpamDetection");

    /// <summary>
    /// ActivitySource for file scanning operations.
    /// Used to trace ClamAV and VirusTotal scanning workflows.
    /// </summary>
    public static readonly ActivitySource FileScanning = new("TelegramGroupsAdmin.FileScanning");

    /// <summary>
    /// ActivitySource for message processing operations.
    /// Used to trace message queue processing and handler execution.
    /// </summary>
    public static readonly ActivitySource MessageProcessing = new("TelegramGroupsAdmin.MessageProcessing");

    /// <summary>
    /// ActivitySource for background job execution.
    /// Used to trace Quartz.NET job lifecycle and execution.
    /// </summary>
    public static readonly ActivitySource BackgroundJobs = new("TelegramGroupsAdmin.BackgroundJobs");

    // =========================================================================
    // Meters for Custom Metrics
    // =========================================================================

    /// <summary>
    /// Meter for custom application metrics.
    /// Provides counters, histograms, and gauges for operational insights.
    /// </summary>
    public static readonly Meter Metrics = new("TelegramGroupsAdmin.Metrics");

    // =========================================================================
    // Counters - Monotonically increasing values
    // =========================================================================

    /// <summary>
    /// Counter for total spam detections.
    /// Tags: algorithm (string), result (spam|ham)
    /// </summary>
    public static readonly Counter<long> SpamDetections = Metrics.CreateCounter<long>(
        "spam_detections_total",
        description: "Total number of spam detections by algorithm and result");

    /// <summary>
    /// Counter for file scan results.
    /// Tags: tier (string), result (malicious|clean)
    /// </summary>
    public static readonly Counter<long> FileScanResults = Metrics.CreateCounter<long>(
        "file_scan_results_total",
        description: "Total number of file scans by tier and result");

    /// <summary>
    /// Counter for background job executions.
    /// Tags: job_name (string), status (success|failure)
    /// </summary>
    public static readonly Counter<long> JobExecutions = Metrics.CreateCounter<long>(
        "job_executions_total",
        description: "Total number of job executions by name and status");

    // =========================================================================
    // Histograms - Distribution of values over time
    // =========================================================================

    /// <summary>
    /// Histogram for spam detection duration in milliseconds.
    /// Tags: algorithm (string)
    /// </summary>
    public static readonly Histogram<double> SpamDetectionDuration = Metrics.CreateHistogram<double>(
        "spam_detection_duration_ms",
        unit: "ms",
        description: "Duration of spam detection algorithm execution in milliseconds");

    /// <summary>
    /// Histogram for file scan duration in milliseconds.
    /// Tags: tier (string)
    /// </summary>
    public static readonly Histogram<double> FileScanDuration = Metrics.CreateHistogram<double>(
        "file_scan_duration_ms",
        unit: "ms",
        description: "Duration of file scanning operations in milliseconds");

    /// <summary>
    /// Histogram for background job duration in milliseconds.
    /// Tags: job_name (string)
    /// </summary>
    public static readonly Histogram<double> JobDuration = Metrics.CreateHistogram<double>(
        "job_duration_ms",
        unit: "ms",
        description: "Duration of background job execution in milliseconds");
}

using System.Diagnostics;

namespace TelegramGroupsAdmin.Core.Telemetry;

/// <summary>
/// ActivitySources for OpenTelemetry distributed tracing.
/// Metrics have been migrated to domain-scoped classes (DetectionMetrics, JobMetrics, etc.)
/// </summary>
public static class TelemetryConstants
{
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
}

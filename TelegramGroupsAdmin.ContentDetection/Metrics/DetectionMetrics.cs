using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelegramGroupsAdmin.ContentDetection.Metrics;

/// <summary>
/// Metrics for spam detection, file scanning, and OpenAI veto tracking.
/// Replaces flat counters/histograms from TelemetryConstants.
/// </summary>
public sealed class DetectionMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Detection");

    private readonly Counter<long> _spamTotal;
    private readonly Histogram<double> _algorithmDuration;
    private readonly Counter<long> _fileScanTotal;
    private readonly Histogram<double> _fileScanDuration;
    private readonly Counter<long> _vetoTotal;

    public DetectionMetrics()
    {
        _spamTotal = _meter.CreateCounter<long>(
            "tga.detection.spam_total",
            description: "Per-algorithm spam detection counts");

        _algorithmDuration = _meter.CreateHistogram<double>(
            "tga.detection.algorithm.duration",
            unit: "ms",
            description: "Per-algorithm execution time");

        _fileScanTotal = _meter.CreateCounter<long>(
            "tga.detection.file_scan_total",
            description: "File scan results by tier and outcome");

        _fileScanDuration = _meter.CreateHistogram<double>(
            "tga.detection.file_scan.duration",
            unit: "ms",
            description: "File scan latency by tier");

        _vetoTotal = _meter.CreateCounter<long>(
            "tga.detection.veto_total",
            description: "OpenAI veto count per algorithm");
    }

    public void RecordSpamDetection(string algorithm, string result, double durationMs)
    {
        var tags = new TagList
        {
            { "algorithm", algorithm },
            { "result", result }
        };
        _spamTotal.Add(1, tags);
        _algorithmDuration.Record(durationMs, new TagList { { "algorithm", algorithm } });
    }

    public void RecordFileScan(string tier, bool isMalicious, double durationMs)
    {
        var tags = new TagList
        {
            { "tier", tier },
            { "result", isMalicious ? "malicious" : "clean" }
        };
        _fileScanTotal.Add(1, tags);
        _fileScanDuration.Record(durationMs, new TagList { { "tier", tier } });
    }

    public void RecordVeto(string algorithm)
    {
        _vetoTotal.Add(1, new TagList { { "algorithm", algorithm } });
    }
}

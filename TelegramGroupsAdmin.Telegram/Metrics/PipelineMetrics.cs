using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelegramGroupsAdmin.Telegram.Metrics;

/// <summary>
/// Metrics for message processing pipeline, moderation actions, command handling,
/// and profile scanning.
/// </summary>
public sealed class PipelineMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Pipeline");

    private readonly Counter<long> _messagesProcessedTotal;
    private readonly Counter<long> _moderationActionsTotal;
    private readonly Counter<long> _commandsHandledTotal;
    private readonly Counter<long> _profileScansTotal;
    private readonly Counter<long> _profileScanTimeoutsTotal;
    private readonly Counter<long> _profileScanSkippedTotal;

    private readonly Histogram<double> _processingDuration;
    private readonly Histogram<double> _profileScanDuration;

    public PipelineMetrics()
    {
        _messagesProcessedTotal = _meter.CreateCounter<long>(
            "tga.pipeline.messages_processed_total",
            description: "Messages processed by source and result");

        _moderationActionsTotal = _meter.CreateCounter<long>(
            "tga.pipeline.moderation_actions_total",
            description: "Moderation actions by action type and trigger");

        _commandsHandledTotal = _meter.CreateCounter<long>(
            "tga.pipeline.commands_handled_total",
            description: "Bot commands handled by command name");

        _profileScansTotal = _meter.CreateCounter<long>(
            "tga.pipeline.profile_scans_total",
            description: "Profile scans by outcome and source");

        _profileScanTimeoutsTotal = _meter.CreateCounter<long>(
            "tga.pipeline.profile_scan.timeouts_total",
            description: "Profile scan timeouts");

        _profileScanSkippedTotal = _meter.CreateCounter<long>(
            "tga.pipeline.profile_scan.skipped_total",
            description: "Profile scans skipped by reason");

        _processingDuration = _meter.CreateHistogram<double>(
            "tga.pipeline.processing.duration",
            unit: "ms",
            description: "Message processing duration by source");

        _profileScanDuration = _meter.CreateHistogram<double>(
            "tga.pipeline.profile_scan.duration",
            unit: "ms",
            description: "Profile scan duration by source");
    }

    public void RecordMessageProcessed(string source, string result, double durationMs)
    {
        _messagesProcessedTotal.Add(1, new TagList
        {
            { "source", source },
            { "result", result }
        });
        _processingDuration.Record(durationMs, new TagList { { "source", source } });
    }

    public void RecordModerationAction(string action, string trigger)
    {
        _moderationActionsTotal.Add(1, new TagList
        {
            { "action", action },
            { "trigger", trigger }
        });
    }

    public void RecordCommandHandled(string command)
    {
        _commandsHandledTotal.Add(1, new TagList { { "command", command } });
    }

    public void RecordProfileScan(string outcome, string source, double durationMs)
    {
        _profileScansTotal.Add(1, new TagList
        {
            { "outcome", outcome },
            { "source", source }
        });
        _profileScanDuration.Record(durationMs, new TagList { { "source", source } });
    }

    public void RecordProfileScanTimeout()
    {
        _profileScanTimeoutsTotal.Add(1);
    }

    public void RecordProfileScanSkipped(string reason)
    {
        _profileScanSkippedTotal.Add(1, new TagList { { "reason", reason } });
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelegramGroupsAdmin.Core.Metrics;

/// <summary>
/// Metrics for external API calls: OpenAI, VirusTotal, SendGrid, Telegram Bot API.
/// Per-feature attribution via required tags on recording methods.
/// </summary>
public sealed class ApiMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Api");

    private readonly Counter<long> _openAiCallsTotal;
    private readonly Histogram<double> _openAiLatency;
    private readonly Counter<long> _openAiTokensTotal;
    private readonly Counter<long> _virusTotalCallsTotal;
    private readonly Histogram<double> _virusTotalLatency;
    private readonly Counter<long> _virusTotalQuotaExhaustedTotal;
    private readonly Counter<long> _sendGridSendsTotal;
    private readonly Counter<long> _telegramCallsTotal;
    private readonly Counter<long> _telegramErrorsTotal;

    public ApiMetrics()
    {
        _openAiCallsTotal = _meter.CreateCounter<long>(
            "tga.api.openai.calls_total",
            description: "OpenAI API call count per feature");

        _openAiLatency = _meter.CreateHistogram<double>(
            "tga.api.openai.latency",
            unit: "ms",
            description: "OpenAI API latency distribution");

        _openAiTokensTotal = _meter.CreateCounter<long>(
            "tga.api.openai.tokens_total",
            description: "OpenAI token consumption per feature");

        _virusTotalCallsTotal = _meter.CreateCounter<long>(
            "tga.api.virustotal.calls_total",
            description: "VirusTotal API calls by operation");

        _virusTotalLatency = _meter.CreateHistogram<double>(
            "tga.api.virustotal.latency",
            unit: "ms",
            description: "VirusTotal API latency per operation");

        _virusTotalQuotaExhaustedTotal = _meter.CreateCounter<long>(
            "tga.api.virustotal.quota_exhausted_total",
            description: "VirusTotal daily quota exhaustion events");

        _sendGridSendsTotal = _meter.CreateCounter<long>(
            "tga.api.sendgrid.sends_total",
            description: "SendGrid email sends by template and status");

        _telegramCallsTotal = _meter.CreateCounter<long>(
            "tga.api.telegram.calls_total",
            description: "Telegram Bot API calls by operation");

        _telegramErrorsTotal = _meter.CreateCounter<long>(
            "tga.api.telegram.errors_total",
            description: "Telegram Bot API error breakdown");
    }

    public void RecordOpenAiCall(string feature, string model, int promptTokens, int completionTokens, double durationMs, bool success)
    {
        _openAiCallsTotal.Add(1, new TagList
        {
            { "feature", feature },
            { "model", model },
            { "status", success ? "success" : "failure" }
        });

        _openAiLatency.Record(durationMs, new TagList
        {
            { "feature", feature },
            { "model", model }
        });

        if (promptTokens > 0)
            _openAiTokensTotal.Add(promptTokens, new TagList
            {
                { "feature", feature },
                { "model", model },
                { "type", "prompt" }
            });

        if (completionTokens > 0)
            _openAiTokensTotal.Add(completionTokens, new TagList
            {
                { "feature", feature },
                { "model", model },
                { "type", "completion" }
            });
    }

    public void RecordVirusTotalCall(string operation, double durationMs, bool success)
    {
        _virusTotalCallsTotal.Add(1, new TagList
        {
            { "operation", operation },
            { "status", success ? "success" : "failure" }
        });
        _virusTotalLatency.Record(durationMs, new TagList { { "operation", operation } });
    }

    public void RecordVirusTotalQuotaExhausted()
    {
        _virusTotalQuotaExhaustedTotal.Add(1);
    }

    public void RecordSendGridSend(string template, bool success)
    {
        _sendGridSendsTotal.Add(1, new TagList
        {
            { "template", template },
            { "status", success ? "success" : "failure" }
        });
    }

    public void RecordTelegramApiCall(string operation, bool success)
    {
        _telegramCallsTotal.Add(1, new TagList
        {
            { "operation", operation },
            { "status", success ? "success" : "failure" }
        });
    }

    public void RecordTelegramApiError(string errorType)
    {
        _telegramErrorsTotal.Add(1, new TagList { { "error_type", errorType } });
    }
}

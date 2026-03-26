using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelegramGroupsAdmin.Telegram.Metrics;

/// <summary>
/// Metrics for welcome flow outcomes, security checks, bot joins, timeouts, and user departures.
/// </summary>
public sealed class WelcomeMetrics
{
    private readonly Counter<long> _joinsTotal;
    private readonly Counter<long> _securityChecksTotal;
    private readonly Counter<long> _botJoinsTotal;
    private readonly Counter<long> _timeoutsTotal;
    private readonly Counter<long> _leavesTotal;
    private readonly Histogram<double> _duration;

    public WelcomeMetrics()
    {
        var meter = new Meter("TelegramGroupsAdmin.Welcome");

        _joinsTotal = meter.CreateCounter<long>(
            "tga.welcome.joins_total",
            description: "Welcome flow outcomes by result");

        _securityChecksTotal = meter.CreateCounter<long>(
            "tga.welcome.security_checks_total",
            description: "Security checks by check type and result");

        _botJoinsTotal = meter.CreateCounter<long>(
            "tga.welcome.bot_joins_total",
            description: "Bot join outcomes by result");

        _timeoutsTotal = meter.CreateCounter<long>(
            "tga.welcome.timeouts_total",
            description: "Welcome timeouts");

        _leavesTotal = meter.CreateCounter<long>(
            "tga.welcome.leaves_total",
            description: "Users who left during welcome flow");

        _duration = meter.CreateHistogram<double>(
            "tga.welcome.duration",
            unit: "ms",
            description: "Welcome flow duration by result");
    }

    public void RecordWelcomeOutcome(string result, double durationMs)
    {
        _joinsTotal.Add(1, new TagList { { "result", result } });
        _duration.Record(durationMs, new TagList { { "result", result } });
    }

    public void RecordSecurityCheck(string check, string result)
    {
        _securityChecksTotal.Add(1, new TagList
        {
            { "check", check },
            { "result", result }
        });
    }

    public void RecordBotJoin(string result)
    {
        _botJoinsTotal.Add(1, new TagList { { "result", result } });
    }

    public void RecordWelcomeTimeout()
    {
        _timeoutsTotal.Add(1);
    }

    public void RecordUserLeft()
    {
        _leavesTotal.Add(1);
    }
}

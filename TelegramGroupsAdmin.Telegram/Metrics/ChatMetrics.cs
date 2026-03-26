using System.Diagnostics;
using System.Diagnostics.Metrics;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Telegram.Metrics;

/// <summary>
/// Metrics for managed chats, health checks, message types, and user joins/leaves.
/// </summary>
public sealed class ChatMetrics
{
    private readonly Counter<long> _healthCheckTotal;
    private readonly Counter<long> _markedInactiveTotal;
    private readonly Counter<long> _messagesTotal;
    private readonly Counter<long> _userJoinsTotal;
    private readonly Counter<long> _userLeavesTotal;

    public ChatMetrics(IChatCache chatCache)
    {
        var meter = new Meter("TelegramGroupsAdmin.Chats");

        meter.CreateObservableGauge(
            "tga.chats.managed_count",
            () => chatCache.Count,
            description: "Number of managed chats currently in the cache");

        _healthCheckTotal = meter.CreateCounter<long>(
            "tga.chats.health_check_total",
            description: "Health checks by result");

        _markedInactiveTotal = meter.CreateCounter<long>(
            "tga.chats.marked_inactive_total",
            description: "Chats marked inactive after consecutive failures");

        _messagesTotal = meter.CreateCounter<long>(
            "tga.chats.messages_total",
            description: "Messages processed by type");

        _userJoinsTotal = meter.CreateCounter<long>(
            "tga.chats.user_joins_total",
            description: "Users joined managed chats");

        _userLeavesTotal = meter.CreateCounter<long>(
            "tga.chats.user_leaves_total",
            description: "Users left managed chats");
    }

    public void RecordHealthCheck(string result)
    {
        _healthCheckTotal.Add(1, new TagList { { "result", result } });
    }

    public void RecordChatMarkedInactive()
    {
        _markedInactiveTotal.Add(1);
    }

    public void RecordMessage(string type)
    {
        _messagesTotal.Add(1, new TagList { { "type", type } });
    }

    public void RecordUserJoin()
    {
        _userJoinsTotal.Add(1);
    }

    public void RecordUserLeave()
    {
        _userLeavesTotal.Add(1);
    }
}

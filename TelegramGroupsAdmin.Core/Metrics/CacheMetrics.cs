using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelegramGroupsAdmin.Core.Metrics;

/// <summary>
/// Metrics for in-memory cache hit/miss/removal tracking.
/// </summary>
public sealed class CacheMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Cache");

    private readonly Counter<long> _hitsTotal;
    private readonly Counter<long> _missesTotal;
    private readonly Counter<long> _removalsTotal;

    public CacheMetrics()
    {
        _hitsTotal = _meter.CreateCounter<long>(
            "tga.cache.hits_total",
            description: "Cache hits by cache name");

        _missesTotal = _meter.CreateCounter<long>(
            "tga.cache.misses_total",
            description: "Cache misses by cache name");

        _removalsTotal = _meter.CreateCounter<long>(
            "tga.cache.removals_total",
            description: "Explicit cache removals by cache name");
    }

    public void RecordHit(string cacheName)
    {
        _hitsTotal.Add(1, new TagList { { "cache_name", cacheName } });
    }

    public void RecordMiss(string cacheName)
    {
        _missesTotal.Add(1, new TagList { { "cache_name", cacheName } });
    }

    public void RecordRemoval(string cacheName)
    {
        _removalsTotal.Add(1, new TagList { { "cache_name", cacheName } });
    }
}

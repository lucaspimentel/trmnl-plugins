using System.Collections.Concurrent;

namespace TrmnlApi.Services;

/// <summary>
/// Process-lifetime counters for forecast requests. Uptime resets reveal restarts, which is how
/// restart-driven cache loss is distinguished from a genuinely low hit rate.
/// </summary>
public sealed class ForecastMetrics(TimeProvider timeProvider)
{
    private readonly DateTimeOffset _startedAt = timeProvider.GetUtcNow();
    private readonly ConcurrentDictionary<string, long> _byProvider = new(StringComparer.Ordinal);

    private long _freshFetch;
    private long _freshHit;
    private long _staleServed;
    private long _upstreamFailures;

    public void RecordServed(string cacheStatus, string winningProvider)
    {
        switch (cacheStatus)
        {
            case WeatherForecastOrchestrator.CacheFreshFetch:
                Interlocked.Increment(ref _freshFetch);
                break;
            case WeatherForecastOrchestrator.CacheFreshHit:
                Interlocked.Increment(ref _freshHit);
                break;
            case WeatherForecastOrchestrator.CacheStaleServed:
                Interlocked.Increment(ref _staleServed);
                break;
        }

        _byProvider.AddOrUpdate(winningProvider, 1, static (_, count) => count + 1);
    }

    public void RecordUpstreamFailure() => Interlocked.Increment(ref _upstreamFailures);

    public MetricsSnapshot Snapshot()
    {
        var freshFetch = Interlocked.Read(ref _freshFetch);
        var freshHit = Interlocked.Read(ref _freshHit);
        var staleServed = Interlocked.Read(ref _staleServed);
        var served = freshFetch + freshHit + staleServed;

        // A stale_served response still avoided a live upstream call, so it counts as a hit.
        var hits = freshHit + staleServed;

        return new MetricsSnapshot(
            StartedAt: _startedAt,
            UptimeSeconds: (long)(timeProvider.GetUtcNow() - _startedAt).TotalSeconds,
            Served: served,
            FreshFetch: freshFetch,
            FreshHit: freshHit,
            StaleServed: staleServed,
            UpstreamFailures: Interlocked.Read(ref _upstreamFailures),
            HitRate: served == 0 ? 0 : Math.Round((double)hits / served, 4),
            ByProvider: _byProvider.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }
}

public sealed record MetricsSnapshot(
    DateTimeOffset StartedAt,
    long UptimeSeconds,
    long Served,
    long FreshFetch,
    long FreshHit,
    long StaleServed,
    long UpstreamFailures,
    double HitRate,
    IReadOnlyDictionary<string, long> ByProvider);

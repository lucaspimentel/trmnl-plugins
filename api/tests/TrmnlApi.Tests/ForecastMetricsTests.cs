using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class ForecastMetricsTests
{
    [Fact]
    public void Snapshot_NoRequests_IsEmpty()
    {
        var metrics = new ForecastMetrics(new TestClock());

        var snapshot = metrics.Snapshot();

        Assert.Equal(0, snapshot.Served);
        Assert.Equal(0, snapshot.UpstreamFailures);
        Assert.Equal(0, snapshot.HitRate);
        Assert.Empty(snapshot.ByProvider);
    }

    [Theory]
    [InlineData(WeatherForecastOrchestrator.CacheFreshFetch, 1, 0, 0, 0d)]
    [InlineData(WeatherForecastOrchestrator.CacheFreshHit, 0, 1, 0, 1d)]
    [InlineData(WeatherForecastOrchestrator.CacheStaleServed, 0, 0, 1, 1d)]
    public void RecordServed_CountsByCacheStatus(string cacheStatus, long freshFetch, long freshHit, long staleServed, double hitRate)
    {
        var metrics = new ForecastMetrics(new TestClock());

        metrics.RecordServed(cacheStatus, "open-meteo");

        var snapshot = metrics.Snapshot();
        Assert.Equal(freshFetch, snapshot.FreshFetch);
        Assert.Equal(freshHit, snapshot.FreshHit);
        Assert.Equal(staleServed, snapshot.StaleServed);
        Assert.Equal(1, snapshot.Served);
        Assert.Equal(hitRate, snapshot.HitRate);
    }

    [Fact]
    public void HitRate_MixedOutcomes_CountsStaleAsHit()
    {
        var metrics = new ForecastMetrics(new TestClock());

        metrics.RecordServed(WeatherForecastOrchestrator.CacheFreshFetch, "open-meteo");
        metrics.RecordServed(WeatherForecastOrchestrator.CacheFreshHit, "open-meteo");
        metrics.RecordServed(WeatherForecastOrchestrator.CacheStaleServed, "open-meteo");
        metrics.RecordServed(WeatherForecastOrchestrator.CacheFreshHit, "open-meteo");

        var snapshot = metrics.Snapshot();

        Assert.Equal(4, snapshot.Served);
        Assert.Equal(0.75, snapshot.HitRate);
    }

    [Fact]
    public void RecordServed_TracksWinningProvider()
    {
        var metrics = new ForecastMetrics(new TestClock());

        metrics.RecordServed(WeatherForecastOrchestrator.CacheFreshFetch, "open-meteo");
        metrics.RecordServed(WeatherForecastOrchestrator.CacheFreshHit, "open-meteo");
        metrics.RecordServed(WeatherForecastOrchestrator.CacheFreshFetch, "pirate-weather");

        var snapshot = metrics.Snapshot();

        Assert.Equal(2, snapshot.ByProvider["open-meteo"]);
        Assert.Equal(1, snapshot.ByProvider["pirate-weather"]);
    }

    [Fact]
    public void RecordUpstreamFailure_DoesNotCountAsServed()
    {
        var metrics = new ForecastMetrics(new TestClock());

        metrics.RecordUpstreamFailure();
        metrics.RecordUpstreamFailure();

        var snapshot = metrics.Snapshot();

        Assert.Equal(2, snapshot.UpstreamFailures);
        Assert.Equal(0, snapshot.Served);
    }

    [Fact]
    public void Snapshot_UptimeTracksClock()
    {
        var clock = new TestClock();
        var startedAt = clock.GetUtcNow();
        var metrics = new ForecastMetrics(clock);

        clock.Advance(TimeSpan.FromMinutes(90));
        var snapshot = metrics.Snapshot();

        Assert.Equal(startedAt, snapshot.StartedAt);
        Assert.Equal(5400, snapshot.UptimeSeconds);
    }
}

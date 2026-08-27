using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrmnlApi.Models.OpenMeteo;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class PlaceResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsThePlaceTheGeocoderFound()
    {
        var (resolver, client, _) = Build(Boston());

        var place = await resolver.ResolveAsync("Boston");

        Assert.NotNull(place);
        Assert.Equal("Boston", place.Name);
        Assert.Equal("Massachusetts", place.Admin1);
        Assert.Equal("United States", place.Country);
        Assert.Equal("US", place.CountryCode);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_SnapsToTheForecastCacheGrid()
    {
        // Snapping here rather than after the cache lookup is what stops the forecast cache
        // fragmenting by input form.
        var (resolver, _, _) = Build(Boston());

        var place = await resolver.ResolveAsync("Boston");

        Assert.Equal(42.36, place!.Latitude);   // 42.35843 rounds away from zero
        Assert.Equal(-71.06, place.Longitude);  // -71.05977 likewise
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenNothingMatched()
    {
        var (resolver, client, _) = Build(result: null);

        Assert.Null(await resolver.ResolveAsync("zzzzqqqq"));
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_MemoizesAHit()
    {
        var (resolver, client, _) = Build(Boston());

        await resolver.ResolveAsync("Boston");
        var second = await resolver.ResolveAsync("Boston");

        Assert.Equal("Boston", second!.Name);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_MemoizesAMiss()
    {
        // Misses are the cheap case to generate in bulk, so a repeat must not do full work.
        var (resolver, client, _) = Build(result: null);

        await resolver.ResolveAsync("zzzzqqqq");
        Assert.Null(await resolver.ResolveAsync("zzzzqqqq"));

        Assert.Equal(1, client.Calls);
    }

    [Theory]
    [InlineData("boston")]
    [InlineData("BOSTON")]
    [InlineData("BoStOn")]
    public async Task ResolveAsync_MemoKeyIgnoresCase(string variant)
    {
        var (resolver, client, _) = Build(Boston());

        await resolver.ResolveAsync("Boston");
        await resolver.ResolveAsync(variant);

        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_DistinctQueriesAreDistinctEntries()
    {
        var (resolver, client, _) = Build(Boston());

        await resolver.ResolveAsync("Boston");
        await resolver.ResolveAsync("Portland");

        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_LooksAgain_AfterTheHitTtlExpires()
    {
        var (resolver, client, clock) = Build(Boston());

        await resolver.ResolveAsync("Boston");
        clock.Advance(TimeSpan.FromDays(8));   // past the 7 day HitTtl
        await resolver.ResolveAsync("Boston");

        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_ForgetsAMiss_SoonerThanAHit()
    {
        var (resolver, client, clock) = Build(result: null);

        await resolver.ResolveAsync("zzzzqqqq");
        clock.Advance(TimeSpan.FromHours(25));  // past the 24 hour MissTtl, well inside HitTtl
        await resolver.ResolveAsync("zzzzqqqq");

        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_OverLongInput_NeverReachesTheGeocoderOrTheMemo()
    {
        var (resolver, client, cache) = BuildWithCache(Boston());
        var tooLong = new string('a', PlaceResolver.MaxQueryLength + 1);

        Assert.Null(await resolver.ResolveAsync(tooLong));

        Assert.Equal(0, client.Calls);
        // Junk keys that reach the memo evict real ones, which is its own problem.
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task ResolveAsync_AtTheLengthLimit_IsStillLookedUp()
    {
        var (resolver, client, _) = Build(Boston());

        await resolver.ResolveAsync(new string('a', PlaceResolver.MaxQueryLength));

        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_GeocoderOutage_PropagatesRatherThanCachingAMiss()
    {
        // Caching an outage as a miss would keep answering "no such place" for a day after the
        // geocoder came back.
        var (resolver, client, cache) = BuildWithCache(Boston());
        client.Failure = new HttpRequestException("upstream is down");

        await Assert.ThrowsAsync<HttpRequestException>(() => resolver.ResolveAsync("Boston"));

        Assert.Equal(0, cache.Count);
    }

    private static OpenMeteoGeocodingResult Boston() => new(
        Name: "Boston",
        Latitude: 42.35843,
        Longitude: -71.05977,
        Country: "United States",
        CountryCode: "US",
        Admin1: "Massachusetts",
        FeatureCode: "PPLA");

    private static (PlaceResolver Resolver, StubGeocodingClient Client, TestClock Clock) Build(OpenMeteoGeocodingResult? result)
    {
        var clock = new TestClock();
        var client = new StubGeocodingClient { Result = result };
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100, Clock = clock });
        var resolver = new PlaceResolver(
            client, cache, Options.Create(new PlaceCacheOptions()), NullLogger<PlaceResolver>.Instance);
        return (resolver, client, clock);
    }

    private static (PlaceResolver Resolver, StubGeocodingClient Client, MemoryCache Cache) BuildWithCache(OpenMeteoGeocodingResult? result)
    {
        var client = new StubGeocodingClient { Result = result };
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var resolver = new PlaceResolver(
            client, cache, Options.Create(new PlaceCacheOptions()), NullLogger<PlaceResolver>.Instance);
        return (resolver, client, cache);
    }

    private sealed class StubGeocodingClient : IOpenMeteoGeocodingClient
    {
        public OpenMeteoGeocodingResult? Result { get; set; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<OpenMeteoGeocodingResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is not null
                ? Task.FromException<OpenMeteoGeocodingResult?>(Failure)
                : Task.FromResult(Result);
        }
    }
}

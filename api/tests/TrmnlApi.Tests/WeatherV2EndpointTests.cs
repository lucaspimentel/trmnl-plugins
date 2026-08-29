using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrmnlApi.Endpoints;
using TrmnlApi.Geo;
using TrmnlApi.Models;
using TrmnlApi.Models.OpenMeteo;
using TrmnlApi.Observability;
using TrmnlApi.Providers;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class WeatherV2EndpointTests
{
    private const string Primary = "pirate-weather";

    [Fact]
    public async Task Handle_PlaceName_ResolvesAndEchoesThePlaceBack()
    {
        var harness = new Harness();

        var (status, body) = await harness.Get("?place=Boston");

        Assert.Equal(200, status);
        var place = Json(body).GetProperty("place");
        Assert.Equal("Boston", place.GetProperty("name").GetString());
        // The short label now, not the display name: "Boston, MA" fits the title bar's 18
        // characters where "Boston, Massachusetts" did not.
        Assert.Equal("MA", place.GetProperty("admin1").GetString());
        Assert.Equal("US", place.GetProperty("country_code").GetString());
        Assert.Equal(42.36, place.GetProperty("latitude").GetDouble());
        Assert.Equal(-71.06, place.GetProperty("longitude").GetDouble());
    }

    [Theory]
    [InlineData("&show_place=yes")]
    [InlineData("&show_place=maybe")]
    [InlineData("")]
    public async Task Handle_ShowPlaceNotDeclined_KeepsThePlaceBlock(string setting)
    {
        var harness = new Harness();

        var (status, body) = await harness.Get($"?place=Boston{setting}");

        Assert.Equal(200, status);
        Assert.True(Json(body).TryGetProperty("place", out _));
    }

    [Fact]
    public async Task Handle_ShowPlaceNo_OmitsThePlaceBlock()
    {
        // The title bar guards on the block being there, so dropping it is how the setting
        // reaches a template that cannot read the setting itself.
        var harness = new Harness();

        var (status, body) = await harness.Get("?place=Boston&show_place=no");

        Assert.Equal(200, status);
        Assert.False(Json(body).TryGetProperty("place", out _));
        // Still resolved, just not echoed: the forecast has to come from somewhere.
        Assert.Equal(1, harness.GeocodingClient.Calls);
    }

    [Fact]
    public async Task Handle_Coordinates_SkipsTheGeocoderButStillShowsThePlace()
    {
        // Before the bundled dataset, a coordinate caller saw no location at all and so had no
        // way to notice a transposed pair.
        var harness = new Harness();

        var (status, body) = await harness.Get("?place=42.35,-71.05");

        Assert.Equal(200, status);
        Assert.Equal(0, harness.GeocodingClient.Calls);
        Assert.Equal(0, harness.LocalGeocoder.Calls);
        var place = Json(body).GetProperty("place");
        Assert.Equal("Boston", place.GetProperty("name").GetString());
        Assert.Equal("MA", place.GetProperty("admin1").GetString());
    }

    [Fact]
    public async Task Handle_LocalGeocoderHit_NeverCallsTheVendor()
    {
        var harness = new Harness();
        harness.LocalGeocoder.Result = new GeoMatch(42.36, -71.06, "Boston");

        var (status, body) = await harness.Get("?place=Boston");

        Assert.Equal(200, status);
        Assert.Equal(0, harness.GeocodingClient.Calls);
        Assert.Equal("Boston", Json(body).GetProperty("place").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Handle_LocalGeocoderMiss_FallsBackToTheVendor()
    {
        // The safety net that lets the vendor be retired on measurement rather than on hope.
        var harness = new Harness();

        var (status, _) = await harness.Get("?place=Boston");

        Assert.Equal(200, status);
        Assert.Equal(1, harness.LocalGeocoder.Calls);
        Assert.Equal(1, harness.GeocodingClient.Calls);
    }

    [Fact]
    public async Task Handle_ForwardsBothTheCountryAndTheTimeZoneToTheGeocoder()
    {
        // Neither is validated into an error and neither changes the response shape, so a wiring
        // mistake here is invisible: the setting simply stops working, which is exactly how the
        // Country dropdown shipped broken the first time.
        var harness = new Harness();

        await harness.Get("?place=02180&country=us_-_united_states_of_america&tz=America/New_York");

        Assert.Equal("us_-_united_states_of_america", harness.LocalGeocoder.LastPreferredCountry);
        Assert.Equal("America/New_York", harness.LocalGeocoder.LastTimeZone);
    }

    [Fact]
    public async Task Handle_PostalHit_TakesItsNameFromTheReverseLookup()
    {
        // Postal place names are unusable as labels, so a postal match carries coordinates only.
        var harness = new Harness();
        harness.LocalGeocoder.Result = new GeoMatch(17.98, -66.11, CityName: null);
        harness.PlaceLookup.Result = new GeoPlace("Guayama", "US-PR", "Puerto Rico", "US", "United States of America");

        var (_, body) = await harness.Get("?place=00784");

        var place = Json(body).GetProperty("place");
        Assert.Equal("Guayama", place.GetProperty("name").GetString());
        Assert.Equal("PR", place.GetProperty("admin1").GetString());
    }

    [Fact]
    public async Task Handle_LookupFindsNothing_StillServesTheForecast()
    {
        var harness = new Harness();
        harness.PlaceLookup.Result = GeoPlace.Empty;

        var (status, body) = await harness.Get("?place=42.35,-71.05");

        Assert.Equal(200, status);
        Assert.True(Json(body).TryGetProperty("current", out _));
        // Nothing truthful to show. Omitted, not invented.
        Assert.False(Json(body).TryGetProperty("place", out _));
    }

    [Fact]
    public async Task Handle_LookupFindsNothingForANamedPlace_KeepsTheVendorsOwnLabels()
    {
        // Nobody ends up worse off than before the dataset existed.
        var harness = new Harness();
        harness.PlaceLookup.Result = GeoPlace.Empty;

        var (_, body) = await harness.Get("?place=Boston");

        var place = Json(body).GetProperty("place");
        Assert.Equal("Boston", place.GetProperty("name").GetString());
        Assert.Equal("Massachusetts", place.GetProperty("admin1").GetString());
        Assert.Equal("US", place.GetProperty("country_code").GetString());
    }

    [Fact]
    public async Task Handle_SavedCoordinateParameters_StillWork()
    {
        // The transition affordance for an install that upgraded with coordinates already set.
        var harness = new Harness();

        var (status, body) = await harness.Get("?latitude=42.35&longitude=-71.05");

        Assert.Equal(200, status);
        Assert.Equal(0, harness.GeocodingClient.Calls);
        Assert.True(Json(body).TryGetProperty("current", out _));
    }

    [Theory]
    [InlineData("")]                                     // nothing at all
    [InlineData("?place=")]                              // present but empty
    [InlineData("?place=%20%20")]                        // whitespace counts as blank
    public async Task Handle_NothingSupplied_Returns200WithPlaceMissing(string queryString)
    {
        var harness = new Harness();

        var (status, body) = await harness.Get(queryString);

        Assert.Equal(200, status);
        Assert.Equal(ErrorCodes.PlaceMissing, ErrorCode(body));
    }

    [Fact]
    public async Task Handle_OutOfRangePair_Returns200WithPlaceInvalidQuotingTheInput()
    {
        var harness = new Harness();

        var (status, body) = await harness.Get("?place=-171.05,%2042.35");

        Assert.Equal(200, status);
        var error = Json(body).GetProperty("error");
        Assert.Equal(ErrorCodes.PlaceInvalid, error.GetProperty("code").GetString());
        // Custom fields are not readable from a template, so the message is the only place the
        // typed input can come from.
        Assert.Contains("-171.05, 42.35", error.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.NotEmpty(error.GetProperty("hint").GetString()!);
    }

    [Fact]
    public async Task Handle_NoSuchPlace_Returns200WithPlaceNotFoundQuotingTheInput()
    {
        var harness = new Harness();
        harness.GeocodingClient.Result = null;

        var (status, body) = await harness.Get("?place=zzzzqqqq");

        Assert.Equal(200, status);
        var error = Json(body).GetProperty("error");
        Assert.Equal(ErrorCodes.PlaceNotFound, error.GetProperty("code").GetString());
        Assert.Contains("zzzzqqqq", error.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_GeocoderOutage_Returns200WithWeatherUnavailableNotNotFound()
    {
        // The distinction the whole error split exists for: an outage must not tell someone to
        // retype an address that was never the problem.
        var harness = new Harness();
        harness.GeocodingClient.Failure = new HttpRequestException("upstream is down");

        var (status, body) = await harness.Get("?place=Boston");

        Assert.Equal(200, status);
        Assert.Equal(ErrorCodes.WeatherUnavailable, ErrorCode(body));
    }

    [Fact]
    public async Task Handle_AllProvidersDownAndNothingCached_Returns200WithWeatherUnavailable()
    {
        // v1 returns 502 here. v2 must not: TRMNL counts polling failures toward a degraded state
        // that only a manual reset clears.
        var harness = new Harness(providerFails: true);

        var (status, body) = await harness.Get("?place=Boston");

        Assert.Equal(200, status);
        Assert.Equal(ErrorCodes.WeatherUnavailable, ErrorCode(body));
        Assert.Equal(1, harness.Metrics.Snapshot().UpstreamFailures);
    }

    [Theory]
    [InlineData("?place=Boston&units=banana")]
    [InlineData("?place=Boston&hours=0")]
    [InlineData("?place=Boston&hours=999")]
    [InlineData("?place=Boston&days=0")]
    [InlineData("?place=Boston&days=99")]
    [InlineData("?place=Boston&provider=not-a-provider")]
    public async Task Handle_BadPluginParameter_Returns200WithRequestInvalid(string queryString)
    {
        // These come from the plugin's own polling URL rather than from anything typed, so
        // reaching one means the plugin is misconfigured. Still 200: a status code would walk the
        // install into the degraded state just the same.
        var harness = new Harness();

        var (status, body) = await harness.Get(queryString);

        Assert.Equal(200, status);
        Assert.Equal(ErrorCodes.RequestInvalid, ErrorCode(body));
    }

    [Fact]
    public async Task Handle_ClientCancelled_KeepsAStatusCode()
    {
        // Nobody is left to render an error object, so this one is not a device-visible failure.
        var harness = new Harness(providerDelay: true);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var (status, _) = await harness.Get("?place=Boston", cancelled.Token);

        Assert.Equal(499, status);
    }

    [Fact]
    public async Task Handle_LongInput_IsQuotedBackTruncated()
    {
        var harness = new Harness();
        harness.GeocodingClient.Result = null;

        var (_, body) = await harness.Get("?place=" + new string('a', 100));

        var message = Json(body).GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains("...", message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 100), message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_Success_CarriesTheSameMetaAsV1()
    {
        var harness = new Harness();

        var (_, body) = await harness.Get("?place=Boston");

        var meta = Json(body).GetProperty("meta");
        Assert.Equal(Primary, meta.GetProperty("provider").GetString());
        Assert.Equal("12h", meta.GetProperty("time_format").GetString());
        Assert.False(Json(body).TryGetProperty("error", out _));
    }

    [Theory]
    [InlineData("test:place_missing", ErrorCodes.PlaceMissing)]
    [InlineData("test:place_invalid", ErrorCodes.PlaceInvalid)]
    [InlineData("test:place_not_found", ErrorCodes.PlaceNotFound)]
    [InlineData("test:request_invalid", ErrorCodes.RequestInvalid)]
    [InlineData("test:weather_unavailable", ErrorCodes.WeatherUnavailable)]
    // Typed into a web form, so the case it arrives in is not worth caring about.
    [InlineData("TEST:Place_Missing", ErrorCodes.PlaceMissing)]
    // A name nobody defined is itself a scenario rather than a place to geocode.
    [InlineData("test:nonsense", ErrorCodes.RequestInvalid)]
    public async Task Handle_ErrorTestScenario_ReturnsThatErrorWithA200(string place, string expectedCode)
    {
        var harness = new Harness();

        var (status, body) = await harness.Get("?place=" + Uri.EscapeDataString(place));

        Assert.Equal(200, status);
        Assert.Equal(expectedCode, ErrorCode(body));
        Assert.Equal(0, harness.GeocodingClient.Calls);
    }

    [Fact]
    public async Task Handle_UnknownTestScenario_NamesTheOnesThatExist()
    {
        var harness = new Harness();

        var (_, body) = await harness.Get("?place=test:nonsense");

        var message = Json(body).GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains("stale", message, StringComparison.Ordinal);
        Assert.Contains(ErrorCodes.WeatherUnavailable, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("test:499", 499)]
    [InlineData("test:502", 502)]
    public async Task Handle_StatusCodeTestScenario_ReturnsThatStatus(string place, int expected)
    {
        var harness = new Harness();

        var (status, _) = await harness.Get("?place=" + place);

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task Handle_ServerErrorTestScenario_Throws()
    {
        // Deliberately not caught here: the point is to reach the real unhandled-exception handler,
        // which is what turns this into the 500 a device would actually receive.
        var harness = new Harness();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Get("?place=test:500"));
    }

    [Fact]
    public async Task Handle_StaleTestScenario_ServesARealForecastReportedAsStale()
    {
        var harness = new Harness();

        var (status, body) = await harness.Get("?place=test:stale");

        Assert.Equal(200, status);
        var meta = Json(body).GetProperty("meta");
        Assert.Equal("stale_served", meta.GetProperty("cache").GetString());
        Assert.True(meta.GetProperty("age_seconds").GetInt64() > 3600);
        Assert.False(Json(body).TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Handle_PrecipitationTestScenario_FlattensTheLastTwoDailyHighs()
    {
        // The same transformation v1's fake parameter applies, so the two cannot drift.
        var harness = new Harness();

        var (status, body) = await harness.Get("?place=test:precipitation");

        Assert.Equal(200, status);
        var daily = Json(body).GetProperty("daily").GetProperty("entries");
        var last = daily[daily.GetArrayLength() - 1];
        Assert.Equal(last.GetProperty("low").GetInt32(), last.GetProperty("high").GetInt32());
    }

    [Theory]
    // No colon, so not a sentinel however much it looks like one.
    [InlineData("test")]
    [InlineData("Testerton")]
    public async Task Handle_PlaceThatMerelyLooksLikeAScenario_IsGeocodedNormally(string place)
    {
        var harness = new Harness();

        var (status, _) = await harness.Get("?place=" + place);

        Assert.Equal(200, status);
        Assert.Equal(1, harness.GeocodingClient.Calls);
    }

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;

    private static string? ErrorCode(string body) =>
        Json(body).GetProperty("error").GetProperty("code").GetString();

    private sealed class Harness
    {
        private readonly TestClock _clock = new();
        private readonly WeatherForecastOrchestrator _orchestrator;
        private readonly PlaceResolver _placeResolver;
        private readonly ServiceProvider _services = new ServiceCollection().AddLogging().BuildServiceProvider();

        public StubGeocodingClient GeocodingClient { get; } = new();
        public StubLocalGeocoder LocalGeocoder { get; } = new();
        public StubPlaceLookup PlaceLookup { get; } = new();
        public ForecastMetrics Metrics { get; }

        public Harness(bool providerFails = false, bool providerDelay = false)
        {
            var provider = new StubProvider(Primary)
            {
                Failure = providerFails ? new HttpRequestException("boom") : null,
                Delay = providerDelay,
                Response = MakeResponse()
            };
            var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10, Clock = _clock });
            var cache = new WeatherCache(memoryCache, Options.Create(new WeatherCacheOptions()), _clock);
            var resolver = new WeatherProviderResolver([provider], [Primary]);
            _orchestrator = new WeatherForecastOrchestrator(
                resolver, cache, _clock, NullLogger<WeatherForecastOrchestrator>.Instance);
            Metrics = new ForecastMetrics(_clock);
            _placeResolver = new PlaceResolver(
                GeocodingClient,
                new MemoryCache(new MemoryCacheOptions { SizeLimit = 100, Clock = _clock }),
                Options.Create(new PlaceCacheOptions()),
                NullLogger<PlaceResolver>.Instance);
        }

        public async Task<(int Status, string Body)> Get(string queryString, CancellationToken cancellationToken = default)
        {
            var context = new DefaultHttpContext { RequestServices = _services };
            context.Request.QueryString = new QueryString(queryString);
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            var result = await WeatherV2Endpoint.Handle(
                context.Request,
                _placeResolver,
                LocalGeocoder,
                PlaceLookup,
                _orchestrator,
                _clock,
                Metrics,
                NullLogger<WeatherV2Endpoint>.Instance,
                NullLogger<ForecastServed>.Instance,
                cancellationToken);

            await result.ExecuteAsync(context);
            return (context.Response.StatusCode, Encoding.UTF8.GetString(responseBody.ToArray()));
        }
    }

    private static WeatherResponse MakeResponse() => new(
        Current: new CurrentConditions("2026-01-01T00:00", 0, 0, 0, 0, "clear", "", 0, 0, "", true),
        Hourly: new HourlyForecast([new HourlyEntry("2026-01-01T00:00", "12a", 40, 0, "wi-day-sunny", true)]),
        Daily: new DailyForecast(
        [
            new DailyEntry("2026-01-01", 50, 30, "clear", "wi-day-sunny", 0, "", ""),
            new DailyEntry("2026-01-02", 52, 32, "clear", "wi-day-sunny", 0, "", ""),
            new DailyEntry("2026-01-03", 54, 34, "clear", "wi-day-sunny", 0, "", "")
        ]));

    private sealed class StubGeocodingClient : IOpenMeteoGeocodingClient
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }

        public OpenMeteoGeocodingResult? Result { get; set; } = new(
            Name: "Boston",
            Latitude: 42.35843,
            Longitude: -71.05977,
            Country: "United States",
            CountryCode: "US",
            Admin1: "Massachusetts",
            FeatureCode: "PPLA");

        public Task<OpenMeteoGeocodingResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is not null
                ? Task.FromException<OpenMeteoGeocodingResult?>(Failure)
                : Task.FromResult(Result);
        }
    }

    // Misses by default, so the tests that are about the vendor path stay about the vendor path.
    private sealed class StubLocalGeocoder : ILocalGeocoder
    {
        public int Calls { get; private set; }
        public GeoMatch? Result { get; set; }
        public string? LastPreferredCountry { get; private set; }
        public string? LastTimeZone { get; private set; }

        public GeoMatch? Find(string text, string? preferredCountry = null, string? timeZone = null)
        {
            Calls++;
            LastPreferredCountry = preferredCountry;
            LastTimeZone = timeZone;
            return Result;
        }
    }

    private sealed class StubPlaceLookup : IPlaceLookup
    {
        public int Calls { get; private set; }

        public GeoPlace Result { get; set; } = new(
            City: "Boston",
            SubdivisionCode: "US-MA",
            SubdivisionName: "Massachusetts",
            CountryCode: "US",
            Country: "United States of America");

        public GeoPlace Find(double latitude, double longitude)
        {
            Calls++;
            return Result;
        }
    }

    private sealed class StubProvider(string name) : IWeatherProvider
    {
        public string Name { get; } = name;
        public WeatherResponse? Response { get; set; }
        public Exception? Failure { get; set; }
        public bool Delay { get; set; }

        public async Task<WeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric, CancellationToken cancellationToken = default)
        {
            if (Delay)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            if (Failure is not null)
            {
                throw Failure;
            }
            return Response!;
        }
    }
}

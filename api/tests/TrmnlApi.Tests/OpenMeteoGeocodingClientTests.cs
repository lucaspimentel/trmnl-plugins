using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class OpenMeteoGeocodingClientTests
{
    // Shapes copied from the live API, mojibake and all: these are what the parser has to survive.
    private const string BostonBody = """
        {"results":[
          {"id":4930956,"name":"Boston","latitude":42.35843,"longitude":-71.05977,
           "feature_code":"PPLA","country_code":"US","country":"United States","admin1":"Massachusetts"}
        ],"generationtime_ms":0.7}
        """;

    // "Portland, ME" really does return a headland, an island and an airport below the city.
    private const string PortlandBody = """
        {"results":[
          {"name":"Portland Point","latitude":44.80561,"longitude":-70.89952,
           "feature_code":"CAPE","country_code":"US","country":"United States","admin1":"Maine"},
          {"name":"Portland International Jetport","latitude":43.64728,"longitude":-70.30992,
           "feature_code":"AIRP","country_code":"US","country":"United States","admin1":"Maine"},
          {"name":"Portland","latitude":43.65737,"longitude":-70.2589,
           "feature_code":"PPLA2","country_code":"US","country":"United States","admin1":"Maine"}
        ],"generationtime_ms":0.7}
        """;

    // A miss omits the key entirely rather than returning an empty array.
    private const string NoMatchBody = """{"generationtime_ms":0.3}""";

    [Fact]
    public async Task SearchAsync_ReturnsTheFirstMatch()
    {
        var handler = new StubHandler(HttpStatusCode.OK, BostonBody);
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig(apiKey: null));

        var result = await client.SearchAsync("Boston");

        Assert.NotNull(result);
        Assert.Equal("Boston", result.Name);
        Assert.Equal(42.35843, result.Latitude);
        Assert.Equal(-71.05977, result.Longitude);
        Assert.Equal("US", result.CountryCode);
        Assert.Equal("United States", result.Country);
        Assert.Equal("Massachusetts", result.Admin1);
    }

    [Fact]
    public async Task SearchAsync_SkipsResultsThatAreNotPopulatedPlaces()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PortlandBody);
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig(apiKey: null));

        var result = await client.SearchAsync("Portland, ME");

        Assert.NotNull(result);
        Assert.Equal("Portland", result.Name);
        Assert.Equal("PPLA2", result.FeatureCode);
    }

    [Theory]
    [InlineData("PPL")]     // a postal code resolves to one of these
    [InlineData("PPLA")]
    [InlineData("PPLA2")]
    [InlineData("PPLC")]
    public async Task SearchAsync_AcceptsEveryPopulatedPlaceCode(string featureCode)
    {
        var body = $$"""{"results":[{"name":"Somewhere","latitude":1,"longitude":2,"feature_code":"{{featureCode}}"}]}""";
        var handler = new StubHandler(HttpStatusCode.OK, body);
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig(apiKey: null));

        Assert.NotNull(await client.SearchAsync("Somewhere"));
    }

    [Theory]
    [InlineData(NoMatchBody)]                                                                    // no results key at all
    [InlineData("""{"results":[]}""")]                                                           // empty, in case that ever changes
    [InlineData("""{"results":[{"name":"Portland Point","latitude":1,"longitude":2,"feature_code":"CAPE"}]}""")]  // nothing populated
    [InlineData("""{"results":[{"name":"Nameless","latitude":1,"longitude":2}]}""")]             // feature_code missing
    [InlineData("null")]                                                                         // valid JSON, no object
    public async Task SearchAsync_ReturnsNull_WhenNothingMatched(string body)
    {
        var handler = new StubHandler(HttpStatusCode.OK, body);
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig(apiKey: null));

        Assert.Null(await client.SearchAsync("zzzzqqqq"));
    }

    [Fact]
    public async Task SearchAsync_NonSuccessStatus_ThrowsRatherThanReportingAMiss()
    {
        // A geocoder outage must stay distinguishable from "no such place": one is temporary and
        // the other tells the user to retype a place name that was fine.
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, "upstream is down");
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig(apiKey: null));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync("Boston"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Contains("503", ex.Message);
        Assert.Contains("upstream is down", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_LongErrorBody_TruncatesToSnippet()
    {
        var longBody = new string('x', 1000);
        var handler = new StubHandler(HttpStatusCode.InternalServerError, longBody);
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig(apiKey: null));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync("Boston"));

        Assert.DoesNotContain(longBody, ex.Message);
        Assert.Contains(new string('x', 500), ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_NoApiKey_UsesFreeHostWithoutApiKeyParam(string? apiKey)
    {
        var handler = new StubHandler(HttpStatusCode.OK, NoMatchBody);
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig(apiKey));

        await client.SearchAsync("Boston");

        Assert.NotNull(handler.LastUrl);
        Assert.StartsWith("https://geocoding-api.open-meteo.com/v1/search?", handler.LastUrl);
        Assert.DoesNotContain("apikey=", handler.LastUrl);
    }

    [Fact]
    public async Task SearchAsync_ApiKeyConfigured_UsesCustomerHostWithApiKeyParam()
    {
        var handler = new StubHandler(HttpStatusCode.OK, NoMatchBody);
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig("secret-key-1"));

        await client.SearchAsync("Boston");

        Assert.NotNull(handler.LastUrl);
        Assert.StartsWith("https://customer-geocoding-api.open-meteo.com/v1/search?", handler.LastUrl);
        Assert.EndsWith("&apikey=secret-key-1", handler.LastUrl);
    }

    [Theory]
    [InlineData("Boston, MA", "Boston%2C%20MA")]
    [InlineData("São Paulo", "S%C3%A3o%20Paulo")]   // non-ASCII survives the round trip
    [InlineData("a&b=c", "a%26b%3Dc")]                    // cannot smuggle extra query parameters
    public async Task SearchAsync_EscapesTheQuery(string query, string expectedEscaped)
    {
        var handler = new StubHandler(HttpStatusCode.OK, NoMatchBody);
        var client = new OpenMeteoGeocodingClient(new HttpClient(handler), BuildConfig(apiKey: null));

        await client.SearchAsync(query);

        Assert.Contains($"name={expectedEscaped}&", handler.LastUrl);
    }

    private static IConfiguration BuildConfig(string? apiKey)
    {
        var dict = new Dictionary<string, string?>();
        if (apiKey is not null) dict[OpenMeteoClient.ApiKeySettingName] = apiKey;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // AbsoluteUri, not ToString(): ToString() unescapes %20 and %2C back to literals, which
            // would hide whether the query was escaped before it went out.
            LastUrl = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

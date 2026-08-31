using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class PirateWeatherClientTests
{
    [Fact]
    public void Ctor_NoApiKeyConfigured_ThrowsInvalidOperationException()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        Assert.Throws<InvalidOperationException>(() => new PirateWeatherClient(new HttpClient(handler), BuildConfig(apiKey: null)));
    }

    [Fact]
    public async Task GetForecastAsync_NonSuccessStatus_ThrowsHttpRequestExceptionWithStatusAndBody()
    {
        const string errorBody = "{\"error\":\"bad request\"}";
        var handler = new StubHandler(HttpStatusCode.BadRequest, errorBody);
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig("test-key"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetForecastAsync(200, 0));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("400", ex.Message);
        Assert.Contains("bad request", ex.Message);
    }

    [Fact]
    public async Task GetForecastAsync_LongErrorBody_TruncatesToSnippet()
    {
        var longBody = new string('x', 1000);
        var handler = new StubHandler(HttpStatusCode.InternalServerError, longBody);
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig("test-key"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetForecastAsync(0, 0));

        Assert.DoesNotContain(longBody, ex.Message);
        Assert.Contains(new string('x', 500), ex.Message);
    }

    [Fact]
    public async Task GetForecastAsync_NullDeserializedResult_ThrowsJsonException()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig("test-key"));

        await Assert.ThrowsAsync<JsonException>(() => client.GetForecastAsync(0, 0));
    }

    [Fact]
    public async Task GetForecastAsync_BuildsUrlWithPlaceholderAndUnits()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig("secret-key"));

        try { await client.GetForecastAsync(42.36, -71.06, metric: false); }
        catch (JsonException) { /* expected: empty object can't bind to PirateWeatherResponse */ }

        Assert.NotNull(handler.LastUrl);
        Assert.Contains("/forecast/header-auth/42.36,-71.06?units=us", handler.LastUrl);
        Assert.Contains("exclude=minutely,alerts,flags", handler.LastUrl);
    }

    /// <summary>
    /// The key must never reach the URL. The tracer names client spans after the request path, so a
    /// key in the path is a key published to APM: that is exactly what this replaced.
    /// </summary>
    [Fact]
    public async Task GetForecastAsync_SendsApiKeyInHeaderAndNeverInUrl()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig("secret-key"));

        try { await client.GetForecastAsync(42.36, -71.06); } catch (JsonException) { }

        Assert.Equal("secret-key", Assert.Single(handler.LastApiKeyHeader!));
        Assert.DoesNotContain("secret-key", handler.LastUrl!);
    }

    /// <summary>
    /// A retry re-sends the same <see cref="HttpRequestMessage"/> through the pipeline, so the
    /// header has to survive it. Losing it would turn a transient failure into a 401.
    /// </summary>
    [Fact]
    public async Task GetForecastAsync_EveryAttemptCarriesTheHeader()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig("secret-key"));

        for (var i = 0; i < 3; i++)
        {
            try { await client.GetForecastAsync(1, 2); } catch (JsonException) { }
            Assert.Equal("secret-key", Assert.Single(handler.LastApiKeyHeader!));
        }
    }

    [Fact]
    public async Task GetForecastAsync_Metric_UsesSiUnits()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig("k"));

        try { await client.GetForecastAsync(0, 0, metric: true); } catch (JsonException) { }

        Assert.Contains("units=si", handler.LastUrl!);
    }

    private static IConfiguration BuildConfig(string? apiKey)
    {
        var dict = new Dictionary<string, string?>();
        if (apiKey is not null) dict[PirateWeatherClient.ApiKeySettingName] = apiKey;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        public IEnumerable<string>? LastApiKeyHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            LastApiKeyHeader = request.Headers.TryGetValues(PirateWeatherClient.ApiKeyHeaderName, out var values)
                ? values
                : null;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

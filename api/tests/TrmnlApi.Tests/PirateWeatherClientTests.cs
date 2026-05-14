using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class PirateWeatherClientTests
{
    [Fact]
    public async Task GetForecastAsync_NoApiKeyConfigured_ThrowsInvalidOperationException()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig(apiKey: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetForecastAsync(0, 0));
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
    public async Task GetForecastAsync_BuildsUrlWithApiKeyAndUnits()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new PirateWeatherClient(new HttpClient(handler), BuildConfig("secret-key"));

        try { await client.GetForecastAsync(42.36, -71.06, metric: false); }
        catch (JsonException) { /* expected: empty object can't bind to PirateWeatherResponse */ }

        Assert.NotNull(handler.LastUrl);
        Assert.Contains("/forecast/secret-key/42.36,-71.06?units=us", handler.LastUrl);
        Assert.Contains("exclude=minutely,alerts,flags", handler.LastUrl);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class OpenMeteoClientTests
{
    [Fact]
    public async Task GetForecastAsync_NonSuccessStatus_ThrowsHttpRequestExceptionWithStatusAndBody()
    {
        const string errorBody = "{\"error\":true,\"reason\":\"Latitude must be in range\"}";
        var handler = new StubHandler(HttpStatusCode.BadRequest, errorBody);
        var client = new OpenMeteoClient(new HttpClient(handler), BuildConfig(apiKey: null));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetForecastAsync(200, 0));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("400", ex.Message);
        Assert.Contains("Latitude must be in range", ex.Message);
    }

    [Fact]
    public async Task GetForecastAsync_LongErrorBody_TruncatesToSnippet()
    {
        var longBody = new string('x', 1000);
        var handler = new StubHandler(HttpStatusCode.InternalServerError, longBody);
        var client = new OpenMeteoClient(new HttpClient(handler), BuildConfig(apiKey: null));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetForecastAsync(0, 0));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.DoesNotContain(longBody, ex.Message);
        Assert.Contains(new string('x', 500), ex.Message);
    }

    [Fact]
    public async Task GetForecastAsync_NullDeserializedResult_ThrowsJsonException()
    {
        // "null" is valid JSON and deserializes to a null OpenMeteoResponse
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        var client = new OpenMeteoClient(new HttpClient(handler), BuildConfig(apiKey: null));

        await Assert.ThrowsAsync<JsonException>(() => client.GetForecastAsync(0, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetForecastAsync_NoApiKey_UsesFreeHostWithoutApiKeyParam(string? apiKey)
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        var client = new OpenMeteoClient(new HttpClient(handler), BuildConfig(apiKey));

        try { await client.GetForecastAsync(52.52, 13.41); } catch (JsonException) { }

        Assert.NotNull(handler.LastUrl);
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?", handler.LastUrl);
        Assert.DoesNotContain("apikey=", handler.LastUrl);
    }

    [Fact]
    public async Task GetForecastAsync_ApiKeyConfigured_UsesCustomerHostWithApiKeyParam()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        var client = new OpenMeteoClient(new HttpClient(handler), BuildConfig("secret-key-1"));

        try { await client.GetForecastAsync(52.52, 13.41); } catch (JsonException) { }

        Assert.NotNull(handler.LastUrl);
        Assert.StartsWith("https://customer-api.open-meteo.com/v1/forecast?", handler.LastUrl);
        Assert.EndsWith("&apikey=secret-key-1", handler.LastUrl);
        Assert.Contains("latitude=52.52&longitude=13.41", handler.LastUrl);
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
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

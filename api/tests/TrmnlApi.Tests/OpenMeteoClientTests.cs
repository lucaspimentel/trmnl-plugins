using System.Net;
using System.Net.Http;
using System.Text.Json;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class OpenMeteoClientTests
{
    [Fact]
    public async Task GetForecastAsync_NonSuccessStatus_ThrowsHttpRequestExceptionWithStatusAndBody()
    {
        const string errorBody = "{\"error\":true,\"reason\":\"Latitude must be in range\"}";
        var handler = new StubHandler(HttpStatusCode.BadRequest, errorBody);
        var httpClient = new HttpClient(handler);
        var client = new OpenMeteoClient(httpClient);

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
        var httpClient = new HttpClient(handler);
        var client = new OpenMeteoClient(httpClient);

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
        var httpClient = new HttpClient(handler);
        var client = new OpenMeteoClient(httpClient);

        await Assert.ThrowsAsync<JsonException>(() => client.GetForecastAsync(0, 0));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}

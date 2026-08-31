using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TrmnlLegacyProxy;

namespace TrmnlLegacyProxy.Tests;

/// <summary>
/// These all test one property: a caller cannot tell it is being proxied. Every case here is a way
/// that could quietly stop being true for forked plugins that can never be updated.
/// </summary>
public class ForecastProxyTests
{
    [Theory]
    // The ordinary case, and the once-a-minute junk caller's 400, which is forwarded like anything
    // else rather than being short-circuited into a second code path that could drift.
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TheOriginStatusCodeIsPassedThrough(HttpStatusCode status)
    {
        var context = await Relay(new HttpResponseMessage(status)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        Assert.Equal((int)status, context.Response.StatusCode);
    }

    [Fact]
    public async Task TheBodyIsRelayedByteForByte()
    {
        // Deliberately not what a serializer would emit: keys out of alphabetical order, a float
        // that round-trips badly, and unicode. Anything that deserializes and re-serializes on the
        // way through changes at least one of these, and none of it shows up in a parsed compare.
        const string body = """{"z":1,"a":{"temp":74.0,"city":"Donostia / San Sebastián"},"m":[1.10,2.0]}""";

        var context = await Relay(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        Assert.Equal(body, ReadBody(context));
    }

    [Fact]
    public async Task TheOriginContentTypeIsPreserved()
    {
        var context = await Relay(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
    }

    [Theory]
    // The whole query string is handed on as written. `fake=true` is the case that matters: it is
    // undocumented, and rebuilding the query from parsed values is exactly how it would be lost.
    [InlineData("?latitude=42.3&longitude=-71.0&units=imperial&hours=25&days=6")]
    [InlineData("?latitude=42.3&longitude=-71.0&fake=true")]
    [InlineData("?place=Donostia%20%2F%20San%20Sebasti%C3%A1n&tz=Europe%2FMadrid")]
    [InlineData("")]
    public async Task TheQueryStringReachesTheOriginUnchanged(string query)
    {
        Uri? seen = null;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        }, request => seen = request.RequestUri);

        await Invoke(handler, query);

        Assert.NotNull(seen);
        Assert.Equal("/api/v1/forecast", seen!.AbsolutePath);
        Assert.Equal(query, seen.Query);
    }

    [Fact]
    public async Task TheCallersTracingHeadersAreCarriedAcross()
    {
        // This baggage is what identified the caller as the plugin platform's own backend. Dropping
        // it would break their trace and throw away the only provenance signal there is.
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        }, request => seen = request);

        await Invoke(handler, "?latitude=1&longitude=2", request =>
        {
            request.Headers["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
            request.Headers["baggage"] = "sentry-environment=production";
        });

        Assert.NotNull(seen);
        Assert.True(seen!.Headers.Contains("traceparent"));
        Assert.True(seen.Headers.Contains("baggage"));
    }

    [Fact]
    public async Task EveryRequestIsMarkedAsComingThroughTheOldHost()
    {
        // The marker is how the receiving service can finally count this traffic. Without it the
        // proxied requests are indistinguishable from direct ones.
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        }, request => seen = request);

        await Invoke(handler, "?latitude=1&longitude=2");

        Assert.NotNull(seen);
        Assert.True(seen!.Headers.Contains("X-Legacy-Proxy"));
    }

    [Fact]
    public async Task AnUnreachableOriginBecomesABadGateway()
    {
        // The failure this design knowingly accepts: the old host used to answer on its own and no
        // longer can. It should say so plainly rather than inventing a forecast.
        var handler = new StubHandler(_ => throw new HttpRequestException("origin down"));

        var context = await Invoke(handler, "?latitude=1&longitude=2");

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
    }

    private static async Task<HttpContext> Relay(HttpResponseMessage originResponse)
    {
        var context = NewContext("?latitude=1&longitude=2");
        await new ForecastProxy.RelayedResponse(originResponse).ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return context;
    }

    private static async Task<HttpContext> Invoke(
        StubHandler handler, string query, Action<HttpRequest>? configure = null)
    {
        var context = NewContext(query);
        configure?.Invoke(context.Request);

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://origin.invalid") };
        var proxy = new ForecastProxy(new StubClientFactory(client), NullLogger<ForecastProxy>.Instance);

        var result = await proxy.Run(context.Request, CancellationToken.None);
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return context;
    }

    private static DefaultHttpContext NewContext(string query)
    {
        var context = new DefaultHttpContext
        {
            // Results.StatusCode resolves services when it executes, and DefaultHttpContext leaves
            // RequestServices null. Only the test harness needs this; the real host supplies it.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };

        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/forecast";
        context.Request.QueryString = new QueryString(query);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadBody(HttpContext context) =>
        new StreamReader(context.Response.Body).ReadToEnd();

    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        Action<HttpRequestMessage>? observe = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            observe?.Invoke(request);
            return Task.FromResult(respond(request));
        }
    }
}

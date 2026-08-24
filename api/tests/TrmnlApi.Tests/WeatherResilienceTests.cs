using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class WeatherResilienceTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 1)]
    [InlineData(HttpStatusCode.RequestTimeout, 3)]
    [InlineData(HttpStatusCode.InternalServerError, 3)]
    [InlineData(HttpStatusCode.BadGateway, 3)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 3)]
    public async Task Configure_429NotRetried_OtherTransientStatusesRetried(HttpStatusCode status, int expectedCalls)
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(status));
        var client = BuildClient(handler);

        var response = await client.GetAsync("https://example.com/forecast");

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(expectedCalls, handler.CallCount);
    }

    [Fact]
    public async Task Configure_HttpRequestException_Retried()
    {
        var handler = new CountingHandler(_ => throw new HttpRequestException("transient"));
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.com/forecast"));

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public void Configure_BoundsTheFailureBudget()
    {
        var options = new HttpStandardResilienceOptions();

        WeatherResilience.Configure(options);

        // A failing provider must be abandoned fast enough that the orchestrator's stale-cache
        // fallback is reached in seconds, not a minute. Worst case is roughly
        // TotalRequestTimeout x provider count.
        Assert.Equal(TimeSpan.FromSeconds(10), options.TotalRequestTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.AttemptTimeout.Timeout);
        Assert.Equal(2, options.Retry.MaxRetryAttempts);
    }

    private static HttpClient BuildClient(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(options =>
            {
                WeatherResilience.Configure(options);
                // Strip retry delays so the test runs in milliseconds rather than seconds.
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
            });
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(respond(request));
        }
    }
}

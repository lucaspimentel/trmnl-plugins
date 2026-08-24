using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;
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

    [Fact]
    public void Configure_TightensTheCircuitBreakerEnoughToOpen()
    {
        var options = new HttpStandardResilienceOptions();

        WeatherResilience.Configure(options);

        // The stock 100-failures-in-30s threshold is unreachable at the traffic this API sees, so
        // the breaker would never open. These are low enough to trip on a real outage.
        Assert.Equal(0.5, options.CircuitBreaker.FailureRatio);
        Assert.Equal(3, options.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(60), options.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CircuitBreaker.BreakDuration);

        // Options validation rejects a sampling window shorter than two attempt timeouts, and it
        // only runs when the HttpClient is first resolved, so catch it here instead of at startup.
        Assert.True(options.CircuitBreaker.SamplingDuration >= options.AttemptTimeout.Timeout * 2);
    }

    [Fact]
    public async Task Configure_RepeatedRateLimits_OpenTheCircuit()
    {
        // 429 is never retried, so each request contributes one failure and it takes
        // MinimumThroughput requests to open.
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = BuildClient(handler);

        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("https://example.com/forecast");
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        Assert.Equal(3, handler.CallCount);

        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("https://example.com/forecast"));

        // The whole point: the rejected request never touched the upstream.
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Configure_OneRetriedServerError_OpensTheCircuit()
    {
        // A 500 is retried, and the breaker sits inside the retry loop, so a single request
        // contributes three failures and opens the circuit on its own.
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = BuildClient(handler);

        var response = await client.GetAsync("https://example.com/forecast");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(3, handler.CallCount);

        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("https://example.com/forecast"));
        Assert.Equal(3, handler.CallCount);
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

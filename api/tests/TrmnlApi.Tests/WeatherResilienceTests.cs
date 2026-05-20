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
    [InlineData(HttpStatusCode.RequestTimeout, 4)]
    [InlineData(HttpStatusCode.InternalServerError, 4)]
    [InlineData(HttpStatusCode.BadGateway, 4)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 4)]
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

        Assert.Equal(4, handler.CallCount);
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

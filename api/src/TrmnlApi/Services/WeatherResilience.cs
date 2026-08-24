using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly.Retry;
using Polly.Timeout;

namespace TrmnlApi.Services;

public static class WeatherResilience
{
    // The orchestrator can fall back to another provider and then to stale cache, so when a
    // provider is failing the cheapest correct move is to give up on it quickly and go use what
    // we already have. The stock budget (30s per provider, 10s per attempt, 3 retries) means a
    // two-provider outage keeps a device waiting about a minute for data already sitting in the
    // cache. These values bound that to roughly 20s.
    //
    // MaxRetryAttempts is 2 rather than 3 so the setting states the real behavior: with a 10s
    // total budget a third attempt would usually be cut off mid-flight anyway.
    //
    // The circuit breaker is deliberately left at its defaults. Devices poll hourly, so any break
    // duration short enough to be safe has already elapsed before the next request arrives; a
    // breaker cannot do useful work at this request rate.
    public static void Configure(HttpStandardResilienceOptions options)
    {
        options.Retry.ShouldHandle = ShouldRetry;
        options.Retry.MaxRetryAttempts = 2;
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    }

    // 429 means we've hit the upstream's rate limit; retrying within the request
    // window won't help. Fail fast so the orchestrator can fall back to the next
    // provider instead of waiting through the retry budget.
    public static ValueTask<bool> ShouldRetry(RetryPredicateArguments<HttpResponseMessage> args) =>
        ValueTask.FromResult(
            args.Outcome.Exception is HttpRequestException or TimeoutRejectedException
            || (args.Outcome.Result is { } r
                && r.StatusCode != HttpStatusCode.TooManyRequests
                && ((int)r.StatusCode == 408 || (int)r.StatusCode >= 500)));
}

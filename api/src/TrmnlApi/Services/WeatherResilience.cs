using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly.Retry;
using Polly.Timeout;

namespace TrmnlApi.Services;

public static class WeatherResilience
{
    // 429 means we've hit the upstream's rate limit; retrying within the request
    // window won't help. Fail fast so the orchestrator can fall back to the next
    // provider instead of waiting through the retry budget.
    public static void Configure(HttpStandardResilienceOptions options) =>
        options.Retry.ShouldHandle = ShouldRetry;

    public static ValueTask<bool> ShouldRetry(RetryPredicateArguments<HttpResponseMessage> args) =>
        ValueTask.FromResult(
            args.Outcome.Exception is HttpRequestException or TimeoutRejectedException
            || (args.Outcome.Result is { } r
                && r.StatusCode != HttpStatusCode.TooManyRequests
                && ((int)r.StatusCode == 408 || (int)r.StatusCode >= 500)));
}

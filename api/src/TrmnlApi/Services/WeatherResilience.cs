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
    // The circuit breaker numbers below replace defaults that could never fire here: opening the
    // stock breaker needs 100 failures inside a 30s window, and only about four requests a minute
    // reach a provider. A breaker that never opens means a sustained outage costs a live call on
    // every single request.
    //
    // The standard handler orders strategies Retry -> CircuitBreaker -> AttemptTimeout, so the
    // breaker counts attempts rather than requests. A 500 is retried, so one request contributes
    // three failures and opens the circuit immediately; a 429 is not retried (see ShouldRetry), so
    // it contributes one failure per request and takes three requests, well under a minute at the
    // observed arrival rate. The slow failure mode is suppressed at once and the cheap one shortly
    // after, which is the order that matters.
    //
    // The breaker keeps its default predicate on purpose. That predicate counts 429 as a failure
    // even though ShouldRetry deliberately does not: retrying a rate limit inside one request is
    // pointless, but a rate limit is exactly the condition worth suppressing across requests.
    //
    // Opening early is cheap: the orchestrator falls through to the next provider and then to a
    // cached forecast at most a couple of hours old, on a display that refreshes hourly.
    //
    // SamplingDuration must stay at or above twice AttemptTimeout or options validation fails when
    // the HttpClient is first resolved.
    public static void Configure(HttpStandardResilienceOptions options)
    {
        options.Retry.ShouldHandle = ShouldRetry;
        options.Retry.MaxRetryAttempts = 2;
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 3;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
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

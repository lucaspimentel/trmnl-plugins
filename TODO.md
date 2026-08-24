# TODO

## Weather API reliability & availability

Improvements identified during a review of the caching and fallback workflow in the weather API.
Ordered by impact-to-effort ratio (highest first). **Note:** most of these were written while the
API still ran on Azure Functions (Consumption plan). It now runs as a single always-on container,
so items whose premise was instance fragmentation or Functions-specific hosting are annotated below.

- [ ] **Decommission the leftover Azure resources**
  - The hosting migration is complete and all device traffic is served by the container host, but the
    old Azure resources were deliberately kept as the rollback target and are still running: the
    `trmnl-plugins-api` and `trmnl-plugins-api-staging` Function Apps, their Application Insights
    resources, and their storage accounts. Delete them once the current setup has proven stable.
  - The repo side is already clean: the Azure-App-Service Datadog configs
    (`dd-appsettings.{production,staging}.json`), the Azure Functions VS Code extension
    recommendation, and the leftover `TrmnlApi.Functions` namespace have all been removed.

- [ ] **P1 — Shared L2 cache** (largely superseded; now a contingency)
  - Original premise: `WeatherCache` uses `IMemoryCache` (per-process), and on a multi-instance Consumption plan the cache was cold most of the time, neutralizing the 3h `StaleTtl` defense. Migrating to a single always-on container fixed the fragmentation directly, so the L2 cache is no longer the main lever.
  - Still open as a contingency for **restart-driven** cache loss: every deploy or restart wipes `IMemoryCache` and produces a warm-up burst of upstream calls. Only worth building if `/metrics` shows those bursts materially degrading the hit rate.
  - Implementation sketch if it becomes necessary: Redis as a second service on the same private network, `Microsoft.Extensions.Caching.StackExchangeRedis` as `IDistributedCache` L2 behind the `IMemoryCache` L1, Redis key TTL = `StaleTtl`, same key schema as `WeatherCache.CacheKey` (`weather:{provider}:{lat:F2}:{lon:F2}:{metric|imperial}`). On a restart L1 is cold but L2 is warm: the first request hits L2 and repopulates L1.

- [ ] **P2 — Tighten the circuit breaker so it can actually trip (recommended next code change)**
  - The standard handler's defaults are `FailureRatio=0.1`, `MinimumThroughput=100`,
    `SamplingDuration=30s`, `BreakDuration=5s` (re-verified 2026-08-24 against
    `Microsoft.Extensions.Http.Resilience` 10.6.0). At the measured ~4 upstream requests per minute,
    `MinimumThroughput=100` is unreachable, so the breaker never opens and a sustained upstream
    outage costs a live call on every request. `WeatherResilience.Configure` deliberately leaves the
    whole `CircuitBreaker` section at defaults today.
  - **Two facts measured 2026-08-24 that decide the design:**
    - The breaker's default predicate **does** count 429 as a failure, unlike our retry predicate,
      which excludes it on purpose (`api/src/TrmnlApi/Services/WeatherResilience.cs:33-38`).
      Measured: 429, 500 and 408 handled; 400 and 200 not. So a tuned breaker trips on the
      2026-08-19 double-429, which is the failure that motivated this item.
    - The standard handler orders strategies Retry -> CircuitBreaker -> AttemptTimeout, so the
      breaker samples **attempts, not requests**. With the numbers below, a 500 opens the circuit on
      the *first* failing request (one request produces three failed attempts, so three samples),
      while a 429 (never retried, one sample per request) needs three requests, roughly 45s at the
      measured rate. The slow failure mode is suppressed instantly and the cheap fail-fast one within
      a minute, which is the right way round.
  - Proposed numbers, lower than the `MinimumThroughput` around 5 this item previously suggested:

    ```csharp
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 3;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);  // must be >= 2 x AttemptTimeout
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    ```

  - Tuning this aggressively is justified by the cost asymmetry: an over-eager open just routes to
    the next provider, or to stale cache that is 10 minutes to 2 hours old, on a display that devices
    refresh hourly. A false open is close to harmless; a breaker that never trips is the status quo.
    The one thing to weigh is that at `MinimumThroughput=3` a single blip request that 500s three
    times opens the circuit for 30s. If that reads as too twitchy, 4 requires two failing requests
    instead, but start at 3 and let `/metrics` and the APM spans argue otherwise.
  - Wiring is already in place: `BrokenCircuitException` is thrown with **zero** upstream calls, is
    not retried by `ShouldRetry`, and is caught by `WeatherForecastOrchestrator.IsTransient` via
    `ExecutionRejectedException` (`WeatherForecastOrchestrator.cs:180`), so it falls through to the
    next provider and then to stale cache.
  - Two small follow-ons to land in the same change:
    - Add a `BrokenCircuitException` case to `BuildUpstreamFromException`
      (`WeatherForecastOrchestrator.cs:183-189`) mapping to 503, so `meta.upstream` reports the open
      circuit instead of falling through to the generic `null`-status branch.
    - Extend `WeatherResilienceTests` with the two probes above as real assertions. Both are
      deterministic and fast (the 500 case ran in 76ms) once `Retry.Delay` is zeroed the way the
      existing tests do.

- [x] **P2 — Negative caching for failing providers (closed — the tuned circuit breaker does this job)**
  - Decided 2026-08-24 in favor of tuning the circuit breaker above. The two always overlapped and
    this list always said to pick one; this is the pick.
  - The reason to pick the breaker: negative caching *is* a circuit breaker with a threshold of one,
    hand-rolled one layer up in the orchestrator, while a working one already sits unused in the HTTP
    pipeline. It would cost roughly 60-100 lines plus new state, tests, its own metrics, and a
    stale-sentinel failure mode across deploys. The breaker costs about four lines of config.
  - It buys exactly two things over the tuned breaker: suppression after one failure instead of
    three (worth about two wasted upstream calls per 429 outage, not worth a subsystem), and
    honoring the upstream's `Retry-After`, which the breaker genuinely cannot do because
    `BreakDuration` is fixed.
  - **Keep the `Retry-After` piece in your pocket.** If 429s recur *and* the provider sends a long
    `Retry-After`, add just that as a narrow enhancement on top of the breaker. Do not build a
    general negative cache to get it.

- [ ] **P2 — Background refresh on stale-served**
  - When `WeatherForecastOrchestrator.GetAsync` serves a stale entry (`api/src/TrmnlApi/Services/WeatherForecastOrchestrator.cs:127-141`), the next request still has to wait for live retries again. A fire-and-forget refresh after returning stale would warm the cache.
  - The original "fire-and-forget is unsafe on Consumption" gotcha no longer applies now that the API runs as a single always-on container: a background task survives between requests, so a plain `Task.Run`/`IHostedService` refresh is workable. Still guard against stampedes (pairs with the single-flight item below).
  - Medium effort, decent value. No longer depends on the shared L2 cache, since one process owns the whole cache.

- [ ] **P3 — Single-flight / request coalescing**
  - If N concurrent requests for the same `(provider, lat, lon, units)` hit a cold instance, all N call the upstream. A `SemaphoreSlim` per cache key would collapse them.
  - Low priority given current low-concurrency traffic from TRMNL devices, but cheap to add.

- [ ] **P2 — Dedicated outbound IP (on hold — only matters if the free tier is ever revisited)**
  - Open-Meteo's *free* daily quota is per source IP, and the API now egresses through a shared NAT address, so the free tier is unusable without a dedicated egress IP.
  - The current host offers no dedicated egress IP (its "static outbound IPs" are documented as possibly shared). A true dedicated IP would require egressing through a self-hosted forward proxy on a cheap VPS.
  - Moot while the paid Open-Meteo key is in use (it removes the quota ceiling entirely). Revisit only if the paid key is ever dropped.

- [x] **Bound the per-provider failure budget (replaces "tighten resilience handler")**
  - Done: `WeatherResilience.Configure` now sets `TotalRequestTimeout` 10s (was 30s),
    `AttemptTimeout` 5s (was 10s), `MaxRetryAttempts` 2 (was 3). A two-provider outage now reaches
    the stale-cache fallback in roughly 20s instead of a minute. Verified in production: a cold-cache
    request returns in ~0.5s, so the 5s attempt timeout leaves about 10x headroom.
  - Also hardened while here: `WeatherForecastOrchestrator.IsTransient` now tests for Polly's
    `ExecutionRejectedException` base type instead of `TimeoutRejectedException`. Both
    `TimeoutRejectedException` and `BrokenCircuitException` derive from it, so a Polly rejection can
    no longer skip the fallback chain and surface as an unhandled 500. This is a prerequisite for
    ever enabling the circuit breaker.
  - **Jitter was already on**, contrary to the original item: `UseJitter` defaults to `true` in the
    standard handler. `WeatherResilienceTests` turns jitter *off* only to make retry timing
    deterministic in tests, which is probably where the mistaken claim came from. Nothing to do.
  - **Measured traffic rate — read this before reasoning about time windows.** `/metrics` on
    production showed 725 requests over 6102s uptime, about **7 req/min in aggregate** (~4/min of
    those reaching upstream). Devices poll hourly *individually*, but installations are staggered, so
    requests arrive at the API roughly every 8 seconds. Do not confuse the per-device refresh
    interval with the arrival rate: an earlier pass at this item wrongly concluded that short break
    durations and short cache TTLs could never span two requests, and dropped both the circuit
    breaker and negative caching on that basis. Recheck `/metrics` rather than assuming either way.

- [ ] **P2 — Alert on upstream 429 rates for api.open-meteo.com and api.pirateweather.net**
  - No alerting today on dependency rate-limiting. The 2026-08-19 double-429 was found reactively via `meta.upstream` on `stale_served` responses, not by an alert.
  - Add a monitor/alert on 429 response rates (and upstream failure rates generally) for both providers so quota exhaustion or upstream outages are caught before users see 502s.
  - **Unblocked as of 2026-08-24:** APM is live in both environments, so the upstream provider calls now arrive as `http.request` spans carrying `http.status_code` and `out.host`, which is what a monitor would key on. Alert on those rather than on `GET /metrics`, whose counters reset every restart.

## Weather display & accuracy

- [ ] **Allow enabling/disabling the different subviews (current status, hourly forecast, daily forecast) and adjust layout accordingly**
  - The weather plugin renders three subviews: current conditions (`weather_current` / `weather_current_compact` templates in `plugins/weather/src/shared.liquid`), the hourly chart (`weather_hourly_chart`), and the daily forecast (`weather_daily_bars_vertical`). `full.liquid` renders all three in a fixed two-column layout (current + hourly on the left, daily bars on the right); `half_horizontal.liquid`, `half_vertical.liquid`, and `quadrant.liquid` each render subsets.
  - Add toggle custom fields to `plugins/weather/src/settings.yml` (e.g. `show_current`, `show_hourly`, `show_daily` — boolean/checkbox-style, defaulting on) alongside the existing `hours`/`days` fields, then conditionally `{% if show_x %}...{% endif %}` each `render` call in the layout `.liquid` files.
  - "Adjust layout accordingly" is the meatier part: when one or two subviews are disabled, the remaining view(s) should expand to fill the freed space rather than leave a gap — e.g. with only current+hourly enabled, the hourly chart should widen to full width; with only daily enabled, the daily bars should span the whole screen. Likely needs per-combination layout branches (or a flex container that reflows) and may require touching the Highcharts `chart_height`/width and the daily bars' vertical-vs-horizontal orientation.
  - Consider which layouts make sense for each combination (full vs half vs quadrant) and whether to gate some combinations as invalid.

- [ ] **Investigate new TRMNL framework features and assess what could improve the weather plugin**
  - The TRMNL UI framework is now open-source at https://github.com/usetrmnl/trmnl-framework ("TRMNL ePaper design system", a Rails engine), with updated docs at https://trmnl.com/framework. The plugin currently pins `framework_version: 2.3.7` in `plugins/weather/src/settings.yml`.
  - Research scope: review recent framework releases/commits (the repo went open-source ~2026-08, latest commits reference a 3.x line and a 3.2.0 re-cut) and the updated docs (Guides, Arrangement, Responsive utilities, Styling, Typography, Runtime, Paint, Sass, Themes, Variables, Foundation, Elements, Components) for new components/utilities that the weather plugin could adopt.
  - Candidate areas to evaluate: anything that simplifies the current hand-rolled layout work in `plugins/weather/src/{full,half_horizontal,half_vertical,quadrant}.liquid` and `shared.liquid` (e.g. better responsive utilities, arrangement primitives, or chart/data-viz helpers), new runtime features relevant to the Highcharts hourly chart, and whether bumping `framework_version` is safe/worthwhile. Cross-reference against the existing subview-toggle task (current/hourly/daily enable/disable + layout adjustment) since new layout primitives could make that work easier.
  - Output: a short findings note (here or in a follow-up set of TODO items) of concrete, actionable improvements.

- [x] **Improve cross-user cache dedup for nearby coordinates (closed — already implemented at the only workable granularity)**
  - **This already works.** `WeatherCache.CacheKey` (`api/src/TrmnlApi/Services/WeatherCache.cs:30-31`) formats the coordinates with `:F2`, which *rounds* rather than truncates, so the cache is keyed on a 0.01 deg grid: roughly 1.1 km north-south, and about 0.8 km east-west at 42 deg N. Two requests anywhere inside the same cell (e.g. `42.3649` and `42.3601`) already collapse to one entry and one upstream call. The item was originally written as if no dedup existed; it does.
  - The only thing left open was going *coarser*, and both routes are dead ends:
  - **Coarsening the key (round to a fixed grid) does not work** — tested F1 (0.1 deg) vs raw F2 against Open-Meteo at `42.36,-71.06` (2026-06-06): the two requests land in different grid cells **4.23 km apart**, with current temp differing **1.4 F** and hourly temps up to **3.2 F**. Open-Meteo serves a high-res grid (~1-2 km) here, finer than F2, so F1 jumps several cells and degrades accuracy. The F2 key already matches the raw request closely.
  - **Snapping to the provider's resolved coords has a chicken-and-egg problem**: the snapped coords only come back *in the response*, so you can't key a cache *read* on them without first calling the provider (defeating the cache). The only correct form is a `requested-key -> snapped-key` alias map (two-level lookup), which still costs one provider call per distinct requested coordinate to learn its mapping.
  - **Conclusion**: F2 is the finest-grained dedup that costs no accuracy, and it is already in place. Coarser is measurably worse; the alias map costs a provider call per distinct coordinate to learn its mapping. Reopen only if usage shows heavy geographic clustering, and only via the alias-map approach.

- [x] **Round coordinates before the provider call, not just in the cache key (done)**
  - Implemented at `api/src/TrmnlApi/Services/WeatherForecastOrchestrator.cs:59-65`: both coordinates
    are snapped to the same 0.01 degree grid with `MidpointRounding.AwayFromZero` (matching the `F2`
    formatting in `WeatherCache.CacheKey`, which `Math.Round` would otherwise miss by defaulting to
    banker's rounding) before the cache lookup, the upstream call, and the cache write, so all three
    agree.
  - A cell's cached body now always corresponds to the cell centre rather than to whichever raw
    coordinate happened to miss first, which is what makes cache contents reproducible when
    debugging. The item was simply left unchecked after the fact; closing it 2026-08-24.

## Observability

- [x] **Instrument the API with Datadog APM (deferred Phase 3 fast-follow)**
  - Done 2026-08-24, both environments. `api/Dockerfile` installs a pinned `datadog-dotnet-apm` tarball into `/opt/datadog` and sets the `CORECLR_*` profiler vars; `Datadog.Trace` tracks the same version. A Datadog Agent runs as its own service per environment, reached at `datadog-agent.railway.internal`. Full setup in `api/docs/observability.md`. No application code changed.
  - Verified: `aspnet_core.request` -> `weather.forecast` -> `http.request` in one trace, with the cache-status and provider tags on the middle span, and `GET /health` filtered out at the agent.
  - **Correction to an earlier claim in this item:** the `Datadog.Trace` package does *not* set `CORECLR_PROFILER` for you. It is the manual instrumentation API only, so without a separate install there would be one span per trace and no HTTP spans either side. The `Datadog.Trace.Bundle` package would also work but was rejected: its nupkg is ~176MB and copies every runtime identifier into the publish output.
  - Three things cost real time and are worth remembering. The agent does not listen on the injected `PORT`, so its deploy hangs and, with `restartPolicyType: NEVER`, the previous container keeps serving, making a config change look like it had no effect. A sealed variable cannot be copied between environments, since the value is unreadable by design, so syncing one produces a variable that is present by name but empty. And the tracer reports `runtime_metrics_enabled: true` by default, contrary to the documented default.

- [ ] **Pirate Weather needs its own API key (blocking the fallback-path test and any fallback trace coverage)**
  - Currently the Pirate Weather key is shared across the old Azure prod/staging apps and the current host, and is returning 429, so `pirate-weather` requests silently fall back to open-meteo and the fallback path (and its trace coverage) is untestable.

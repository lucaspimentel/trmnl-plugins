# TODO

## Weather API reliability & availability

Improvements identified during a review of the caching and fallback workflow in the weather API.
Ordered by impact-to-effort ratio (highest first). **Note:** most of these were written while the
API still ran on Azure Functions (Consumption plan). It now runs as a single always-on container,
so items whose premise was instance fragmentation or Functions-specific hosting are annotated below.

- [ ] **Decommission the leftover Azure resources** (unblocked 2026-08-31)
  - The hosting migration is complete and all device traffic is served by the container host. What
    still runs on Azure is the `trmnl-plugins-api` Function App, now a **thin forwarder** rather
    than a second implementation of frozen v1 - see `api/docs/legacy-host-proxy.md`. Also still
    standing: `trmnl-plugins-api-staging`, both Application Insights resources, and both storage
    accounts.
  - **Exit condition, stated exactly:** delete the prod app when `weather.via_legacy_host:1` goes
    quiet in Datadog. That tag is set by `DD_TRACE_HEADER_TAGS=x-legacy-proxy:weather.via_legacy_host`
    on the receiving service and is the only place fork traffic can be counted, since the old host
    strips query strings and client IPs. Search the tag name, not the header name.
  - **Deletable now, no waiting:** `trmnl-plugins-api-staging`. It has no traffic and runs a proxy
    nobody calls.
  - **Still open and now the largest single thing left:** the once-a-minute caller sending invalid
    parameters, unchanged through the cutover at ~10 per ten minutes, roughly a quarter of the load
    on a host being retired. Ruled out already: App Insights availability tests and a Function App
    healthcheck. Find it and stop it.
  - Low priority, but unresolved: **the proxy has never run end to end on a developer machine.**
    `func start` and `dotnet run` both die with an `Unavailable` gRPC handshake error; the cause is
    genuinely unknown and two earlier explanations were guesses that did not hold. Only worth time
    if the proxy ever needs a change.
  - The repo side is already clean: the Azure-App-Service Datadog configs
    (`dd-appsettings.{production,staging}.json`), the Azure Functions VS Code extension
    recommendation, and the leftover `TrmnlApi.Functions` namespace have all been removed. The
    forwarder lives in `legacy-proxy/`.

- [ ] **P1 — Shared L2 cache** (largely superseded; now a contingency)
  - Original premise: `WeatherCache` uses `IMemoryCache` (per-process), and on a multi-instance Consumption plan the cache was cold most of the time, neutralizing the 3h `StaleTtl` defense. Migrating to a single always-on container fixed the fragmentation directly, so the L2 cache is no longer the main lever.
  - Still open as a contingency for **restart-driven** cache loss: every deploy or restart wipes `IMemoryCache` and produces a warm-up burst of upstream calls. Only worth building if `/metrics` shows those bursts materially degrading the hit rate.
  - Implementation sketch if it becomes necessary: Redis as a second service on the same private network, `Microsoft.Extensions.Caching.StackExchangeRedis` as `IDistributedCache` L2 behind the `IMemoryCache` L1, Redis key TTL = `StaleTtl`, same key schema as `WeatherCache.CacheKey` (`weather:{provider}:{lat:F2}:{lon:F2}:{metric|imperial}`). On a restart L1 is cold but L2 is warm: the first request hits L2 and repopulates L1.

- [x] **P2 — Tighten the circuit breaker so it can actually trip (done 2026-08-24)**
  - `WeatherResilience.Configure` now sets `FailureRatio=0.5`, `MinimumThroughput=3`,
    `SamplingDuration=60s`, `BreakDuration=30s`, replacing defaults of `0.1`/`100`/`30s`/`5s` that
    could never fire: opening the stock breaker needs 100 failures in a 30s window and only ~4
    requests a minute reach a provider, so a sustained outage cost a live call on every request.
  - **Two facts measured 2026-08-24, now locked in as tests** (`WeatherResilienceTests`):
    - The breaker keeps its **default predicate**, which counts 429 as a failure even though
      `ShouldRetry` excludes it on purpose (`api/src/TrmnlApi/Services/WeatherResilience.cs:55-60`).
      Retrying a rate limit inside one request is pointless; suppressing it across requests is not.
      So the breaker trips on the 2026-08-19 double-429 that motivated this item.
    - The standard handler orders strategies Retry -> CircuitBreaker -> AttemptTimeout, so the
      breaker samples **attempts, not requests**. Verified: one 500 request produces three failed
      attempts and opens the circuit by itself (test runs in 6ms); a 429 needs three requests, about
      45s at the measured rate (75ms). The slow failure mode is suppressed instantly and the cheap
      fail-fast one within a minute, which is the right way round.
  - `BuildUpstreamFromException` maps `BrokenCircuitException` to 503 "provider circuit open"
    (`WeatherForecastOrchestrator.cs:189`), so `meta.upstream` reports the open circuit instead of
    falling through to the generic `null`-status branch. Fallback wiring needed no change:
    `BrokenCircuitException` derives from `ExecutionRejectedException`, which `IsTransient` already
    tests for, so an open circuit falls through to the next provider and then to stale cache with
    zero upstream calls. The tests also confirm it propagates out of `HttpClient` unwrapped.
  - The breaker is scoped per named `HttpClient`, and `Program.cs:11-14` registers one per provider,
    so `open-meteo` and `pirate-weather` get independent breakers.
  - No extra observability was added, deliberately: an open circuit already shows as 503 in
    `meta.upstream`, a warning log per request, and the `weather.first_failure.status` span tag.
  - **If it reads as too twitchy in production**, `MinimumThroughput=4` requires two failing requests
    instead of one for the 500 case. Left at 3; let the APM spans argue otherwise.

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
  - **Do not key any of this on the API's own status code.** v2 answers every device-visible failure with HTTP 200 and a renderable body, so a 5xx from v2 now means the API itself broke, not that the weather did. Error rate and error tracking read the span's error tags, which are set independently of the status. Any alerting carried over from v1 status codes has to be rewritten against those tags.

- [x] **Turn the old Azure app into a reverse proxy in front of the current host (done 2026-08-31)**
  - Shipped: `legacy-proxy/` forwards `/api/v1/forecast` from `trmnl-plugins-api.azurewebsites.net`
    to the current host and returns 200, rather than redirecting - a 3xx would have bet every forked
    install on the TRMNL poller following redirects, which is unverified. `/api/v1/screen` is gone.
    Full write-up in `api/docs/legacy-host-proxy.md`.
  - Staging first, then production. The result-code mix did not move across the cutover, so the
    forked installs kept getting answers straight through the swap. **`meta.time_format` is the test
    for whether the proxy is live**, not the deployment status.
  - What it bought: one implementation of frozen v1 instead of two that can drift, the old host no
    longer making its own upstream provider calls, and that traffic finally visible in Datadog
    through `weather.via_legacy_host`.
  - What it cost, deliberately accepted: the old host was an accidental hot standby with its own
    cache and credentials, and is not one any more. Also, a proxied request costs *more* per request
    than serving from the old host's own cache, not less.
  - The once-a-minute invalid-parameter caller was **not** found and is carried forward on the
    decommission item above.

## Geographic data & place input

Open items from `api/docs/geographic-telemetry.md` and `api/docs/place-input.md`. The datasets ship
and both environments are pinned to `geo-data-20260829`; what is left is a measurement, a deferred
data fix, and an ongoing maintenance chore.

- [ ] **Read `weather.geocoder` on a full week of traffic, then delete `OpenMeteoGeocodingClient`**
  - This is the last step of the geo rollout and the whole point of the exercise: a quiet
    `open-meteo` count in the `ForecastServed` logs is what licenses removing the vendor geocoder
    (`api/src/TrmnlApi/Services/OpenMeteoGeocodingClient.cs`), which is still wired in as the
    fallback for a local miss. Do not delete it before the reading.
  - Unlike the `hint=` reading, which was binary and was taken early, this one is a *rate* question
    and wants the full week.
  - Deleting it saves code and a failure mode, **not money**: geocoding is included in the
    Open-Meteo weather subscription already being paid for.

- [ ] **Fix the stripped-punctuation postal collisions (deferred, needs a full dataset cycle)**
  - `GeoText.NormalizePostal` removes spaces and hyphens, so Poland's `02-180` and a US `02180`
    collapse to one key. Poland, Japan, Portugal, Brazil, Czechia, Slovakia and Sweden punctuate
    **100%** of their codes, so every one of them collides with a bare code nobody would confuse it
    with. It is 5.3% of US ZIP collisions; the other 82% are genuine and unavoidable.
  - The fix is a raw-code column on `postal`, a `GeoSchema.Version` bump to 3, a dataset rebuild, a
    new GitHub release, and a re-pin of `GEO_DATA_URL`/`GEO_DATA_SHA256` on both environments - a
    full cycle for a twentieth of the problem, which is why the country-hint work went first.
  - The time-zone hint makes it **more** visible, not less: a Polish user typing `02-180` with no
    time zone now loses to the US+EU home region where they used to win on Warsaw's population.

- [ ] **Refresh `api/src/TrmnlApi.Geo/zone.tab` when a new IANA tzdb release lands** (currently
  **2026c**)
  - Nothing will tell you it has gone stale. The only thing that would notice a bad copy is
    `TimeZoneCountryTests.TheEmbeddedTableLoads`.
  - Recurring chore rather than a project; check it whenever the geo dataset is rebuilt anyway.

- [ ] **Retire `/api/v1/forecast` when fork traffic stops**
  - v1 is frozen, not dead code, and retires when its traffic stops rather than on a schedule (see
    `CLAUDE.md`). Every non-forked install has moved to v2, so whatever still reaches v1 is fork
    traffic by definition.
  - Now measurable for the first time: fork traffic arriving through the old host carries
    `weather.via_legacy_host:1`. Same signal as the decommission item above, and the two retire
    together.

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

- [ ] **P0 — The Pirate Weather API key is being written into Datadog span resource names**
  - Found 2026-08-31 while proving the fallback trace coverage above. `PirateWeatherClient` puts the
    key in the **URL path**, not a header or query string
    (`api/src/TrmnlApi/Services/PirateWeatherClient.cs:30`), and the tracer names the client span
    after the path. So every pirate-weather call produces a span whose resource is
    `GET api.pirateweather.net/forecast/<the key>/<lat>,<lon>`.
  - **Confirmed in both environments**, not just staging: a `resource_name:*pirateweather*` search
    over 7 days returned 50 production spans and 4 staging spans, every one carrying the 32-character
    key. It has been landing in Datadog since APM was enabled on 2026-08-24.
  - **`DD_HTTP_CLIENT_TAG_QUERY_STRING=false` does not help and never did.** It strips query strings;
    this key is a path segment. The setting being present is probably why nobody looked.
  - Two things to do, in order: **rotate the Pirate Weather key** (it should be treated as disclosed
    to anyone with read access to the Datadog org), then stop the new one leaking. Options for the
    second: a client-side span-name/resource override, tracer URL obfuscation for that host, or
    moving auth off the path if Pirate Weather accepts a header. Check the vendor's auth options
    before assuming the path is required.
  - Note the irony worth remembering: the key is a *sealed* Railway variable, deliberately
    unreadable in the dashboard and CLI, and it was being published in plaintext to APM the whole
    time. Sealing the variable bought nothing on its own.

- [x] **Instrument the API with Datadog APM (deferred Phase 3 fast-follow)**
  - Done 2026-08-24, both environments. `api/Dockerfile` installs a pinned `datadog-dotnet-apm` tarball into `/opt/datadog` and sets the `CORECLR_*` profiler vars; `Datadog.Trace` tracks the same version. A Datadog Agent runs as its own service per environment, reached at `datadog-agent.railway.internal`. Full setup in `api/docs/observability.md`. No application code changed.
  - Verified: `aspnet_core.request` -> `weather.forecast` -> `http.request` in one trace, with the cache-status and provider tags on the middle span, and `GET /health` filtered out at the agent.
  - **Correction to an earlier claim in this item:** the `Datadog.Trace` package does *not* set `CORECLR_PROFILER` for you. It is the manual instrumentation API only, so without a separate install there would be one span per trace and no HTTP spans either side. The `Datadog.Trace.Bundle` package would also work but was rejected: its nupkg is ~176MB and copies every runtime identifier into the publish output.
  - Three things cost real time and are worth remembering. The agent does not listen on the injected `PORT`, so its deploy hangs and, with `restartPolicyType: NEVER`, the previous container keeps serving, making a config change look like it had no effect. A sealed variable cannot be copied between environments, since the value is unreadable by design, so syncing one produces a variable that is present by name but empty. And the tracer reports `runtime_metrics_enabled: true` by default, contrary to the documented default.

- [x] **Exercise the fallback path while Pirate Weather has quota (done 2026-08-31 — trace coverage confirmed)**
  - **Result: the fallback is fully legible in APM.** Forced by setting `OPEN_METEO_API_KEY` to a
    junk value on staging, which sends `OpenMeteoClient` to `customer-api.open-meteo.com` and earns
    a 400 `"The supplied API key is invalid."` One trace (`6a95a7c2000000006ab8f4ae280da5af`)
    contained all five spans: the entry `GET /api/v2/forecast` at `status: ok` / 200, the **failed**
    `GET customer-api.open-meteo.com/v1/forecast` at `status: error` / 400, the successful
    `GET api.pirateweather.net/...` at 200, and the two geo SQLite lookups.
  - The tag pair reads exactly as the item asked: `weather.requested_provider: open-meteo` vs
    `weather.winning_provider: pirate-weather`, with `weather.first_failure.status: 400` and
    `first_failure.error` carrying the upstream message. `meta.upstream` matched on the response.
    A control request with `provider=pirate-weather` showed the tags *agreeing* and no
    `first_failure`, so the disagreement is a real signal and not an artifact.
  - **The 400 produced exactly one open-meteo span, not three.** `ShouldRetry` only retries 408/5xx,
    so an invalid key fails on the first attempt. Worth knowing: a fault injected this way does
    **not** exercise the retry path, and it does not trip the breaker either (its default predicate
    ignores 400), so the circuit was left closed and no cleanup was needed.
  - Cost: two staging redeploys. `OPEN_METEO_API_KEY` was restored from the 1Password item
    `open-meteo api key` and verified against the customer API *before* it was overwritten.
    **The seal survived the round trip and there was nothing to restore by hand.** Sealed means
    write-only, not write-once: a CLI/API write updates the value and the variable stays sealed,
    confirmed afterwards by its continued absence from the value listing.
  - Finding this needed one thing nobody had written down: **sealed variables are missing from
    `railway variables` and from the MCP `list-variables` output entirely, but their names *do*
    appear in `get-service-config`'s `variableNames`.** That is the way to prove a sealed variable
    exists rather than guessing from behavior.
  - Not done, and still open below: the monthly-cap question this item also raised.

- [ ] **Decide whether the Pirate Weather monthly cap makes the fallback best-effort**
  - **Corrects an earlier wording of this item, which was wrong on its premise.** It said the
    Pirate Weather key was shared with the old Azure prod/staging apps. It never was: Pirate Weather
    has always had its own key. The 429s were the plan's **monthly request cap** being reached, which
    makes this a capacity question rather than a key-provisioning one. The distinction matters
    because the wrong version made the fix sound like a one-time five-minute chore.
  - **Not currently rate limited.** A production request with `provider=pirate-weather` on 2026-08-30
    returned `provider=pirate-weather`, `cache=fresh_fetch`, `upstream=null` - a clean upstream call,
    not a fallback. Still true on 2026-08-31, when the fallback test above drove several real
    pirate-weather calls without hitting a cap.
  - **The open question.** If a monthly cap is reachable at all, `pirate-weather` is
    absent as a fallback for whatever part of the month follows it - exactly when an open-meteo
    outage would need it. Worth deciding deliberately: raise the tier, or accept that the fallback
    is best-effort and say so. Recheck whether the cap is actually being hit each month before
    paying for anything.
  - Related: the fixed `BreakDuration` on the circuit breaker cannot express "come back next month",
    which is the `Retry-After` case deliberately left in the pocket above. A monthly quota 429 is the
    scenario that would justify taking it out.

- [ ] **`GET /health` traces are no longer filtered out**
  - `DD_APM_IGNORE_RESOURCES` dropped them on the full agent; the compat agent now in use has no
    equivalent, so the healthcheck's own spans arrive with `status: ok` and no `weather.*` tags.
  - The only lever left is a tracer-side sampling rule on the resource, which loses them entirely
    rather than merely un-indexing them. Noise and ingest cost, not a correctness problem - decide
    whether it is worth the tradeoff.

- [ ] **Possible carve-out: return a non-2xx for `weather_unavailable` only**
  - v2 answers every device-visible failure with HTTP 200 and a renderable body, deliberately:
    `place_missing`, `place_invalid` and `place_not_found` are persistent by construction, so a
    status code would walk the plugin into TRMNL's degraded state and demand a manual reset.
  - `weather_unavailable` is the one exception - genuinely transient and rare - so a status code
    there would let TRMNL keep the last good forecast instead of replacing it with a message. Left
    out of the original design because it buys one code and costs a second render path.
  - **Whether TRMNL parses the body of a non-2xx at all is undocumented and unverified.** If this is
    ever revisited, test it on a scratch plugin and on real hardware; `trmnlp serve` has already
    proven to differ from a device on custom field resolution.

## GitHub Issues (lucaspimentel/trmnl-plugins)

- [x] **Weather: abbreviated day option (done 2026-08-31, shipped to prod)** ([#1](https://github.com/lucaspimentel/trmnl-plugins/issues/1))
  - An `Abbreviate Day Names` select in `plugins/weather/src/settings.yml` shortens `Wednesday` to
    `Wed` in the daily forecast. Defaults to `no`, so an existing install sees no change. Reported
    against a BYOD Kindle PW 7th Gen (1448x1072), where the full name ran into the weather icon.
  - **It could not be done in the plugin alone**, which is the part worth remembering. A template
    cannot read its own custom field, so a display-only setting still needs an API round trip: v2
    parses `abbreviate_days` and echoes `meta.abbreviate_days`, exactly as `time_format` and
    `show_place` already do, and the four layouts pass it into `weather_daily_bars_vertical`.
  - v1 does not grow the key even when a caller passes the parameter. `Meta.AbbreviateDays` is
    `bool?`, left null by v1 and dropped by `WhenWritingNull`; verified against both deployed
    environments, not just in tests. `WeatherEndpointTests` pins it the way the `place` block is
    pinned.
  - Only literal `yes` enables it, so an install predating the setting (sending an empty value)
    keeps full names rather than silently switching.
  - **Still fixed-width.** `.day-label` remains 68px / 90px (`shared.liquid:71,73`), so this is an
    escape hatch rather than a label that adapts to its screen. A BYOD size that fits neither width
    is still a manual setting away from looking right; revisit if more reports arrive.

- [ ] **Support additional weather data sources alongside Open-Meteo** ([#2](https://github.com/lucaspimentel/trmnl-plugins/issues/2))
  - User reports Open-Meteo's `precipitation_probability` over-reports rain vs. MET Norway and
    wttr.in for their location (Amsterdam, 2026-05-02: Open-Meteo flagged 53-55% overnight rain,
    others near zero, and Open-Meteo's own mm field agreed with the others). Requests a
    user-selectable alternate provider; suggests MET Norway (free, no API key).
  - The API already supports multiple providers via the `WeatherProviders` env var and a fallback
    chain (see `api/src/TrmnlApi`), but that's operator-side fallback ordering, not a per-user
    provider choice. Would need a user-facing provider selector (custom field) plumbed through
    `polling_url`, plus a new provider implementation for MET Norway.

- [ ] **Support for Fluid Mashup layouts** ([#7](https://github.com/lucaspimentel/trmnl-plugins/issues/7))
  - TRMNL's new Fluid Mashup feature allows many more grid sizes (3x3 fluid grid) than the plugin's
    current fixed layouts (`full`, `half_horizontal`, `half_vertical`, `quadrant` in
    `plugins/weather/src/`). User provided screenshots showing cropping/overlap issues at 3x1, 1x1,
    and 1x3 sizes on TRMNL X.
  - Needs investigation into what Fluid Mashup requires (likely new/more responsive layout variants,
    possibly using framework responsive utilities) — see also the existing TODO item above about
    investigating new TRMNL framework features, which may be relevant here.

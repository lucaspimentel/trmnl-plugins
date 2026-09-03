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
  - **Nothing here is deletable yet (corrected 2026-09-02).** The prod proxy is not a trickle: it
    is carrying about **33% of all traffic, ~18K requests per day**, so `weather.via_legacy_host:1`
    is nowhere near quiet and the exit condition above is a long way off. And
    `trmnl-plugins-api-staging` is **not** idle-and-forgotten as previously recorded - it is the
    pre-production test target, exercised by hand before changes go to prod. Its traffic is low
    because only one person generates it, not because nobody calls it. It retires with the prod
    app, not before.
  - **Still open and now the largest single thing left:** the once-a-minute caller sending invalid
    parameters, unchanged through the cutover at ~10 per ten minutes, roughly a quarter of the load
    on a host being retired. Ruled out already: App Insights availability tests and a Function App
    healthcheck. Find it and stop it.
  - **Re-measured 2026-09-02 and the description above still holds exactly.** Legacy-host 400s are
    1,436/day against that host's 5,814/day, so **24.7%** - a quarter, and the same 24.6% over 7
    days. It is 8.0% of all traffic (17,879/day total, of which the legacy host is 32.5%). The rate
    is 0.997/minute, so "once a minute" is literal and has not drifted. Note the quarter is of the
    *legacy host*, not of all traffic; both numbers are above so the next reader does not have to
    re-derive which is which.
  - **New and useful: every single 400 arrives through the old host.** Zero 400s reach the
    container host directly (measured over the same day). So the caller is pointed at the **Azure
    URL specifically**, not at the current one - it is not a fork that followed the migration, and
    it will disappear on its own the moment that app is deleted. That reframes it: the caller does
    not have to be *found*, it has to be *outlived*. It stops being anyone's problem when the
    prod Function App goes, which is the same event this whole item is waiting on.
  - Still not identifiable from the receiving end, as stated above: no `@http.useragent` is tagged
    on those spans and `@weather.input_kind` is absent (the request fails before that tag is set),
    on top of the stripped query strings and client IPs.
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

- [x] **P2 — Alert on upstream 429 rates for api.open-meteo.com and api.pirateweather.net (done 2026-08-31)**
  - A Datadog monitor now watches upstream rate-limiting for both providers, so quota exhaustion or
    an upstream outage is caught before devices see it rather than found reactively.
  - No alerting before this. The 2026-08-19 double-429 was found reactively via `meta.upstream` on `stale_served` responses, not by an alert.
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

- [ ] **Decide whether to delete `OpenMeteoGeocodingClient`** (was "read `weather.geocoder` on a
  full week"; **the reading is done, the decision is not** - retitled 2026-09-02)
  - This is the last step of the geo rollout and the whole point of the exercise: a quiet
    `open-meteo` count in the `ForecastServed` logs is what licenses removing the vendor geocoder
    (`api/src/TrmnlApi/Services/OpenMeteoGeocodingClient.cs`), which is still wired in as the
    fallback for a local miss.
  - Unlike the `hint=` reading, which was binary and was taken early, this one is a *rate* question
    and wanted the full week.
  - Deleting it saves code and a failure mode, **not money**: geocoding is included in the
    Open-Meteo weather subscription already being paid for.
  - **First reading taken 2026-09-02. Answer: do not delete. The count is not quiet.** Production
    v2, last 7 days, grouped on `@Geocoder` in the `ForecastServed` logs:
    `none` 43,696 / `local` 2,803 / `open-meteo` **120**. `none` is the coordinate path that never
    forward-geocodes, so the number that matters is the 2,923 requests that did: **local 95.9%,
    vendor 4.1%**.
  - **The window is only ~4.5 days, not the week this item asks for.** The rollout reached prod on
    **2026-08-29** - before that, v2 `ForecastServed` lines carry no `Geocoder` attribute at all
    (15,136 of them, all on 08-28/08-29, which is the whole of an apparent count gap and not a
    dropped field). Retake the reading **on or after 2026-09-05** for a clean seven days.
  - **The vendor count is flat, not decaying**, which is the part that actually blocks deletion:
    29 / 31 / 33 per full day (08-30, 08-31, 09-01). It is not a warm-up tail that will fall to
    zero on its own.
  - **All 120 are one install.** One coordinate (`44.4,-72.3`), one label - Hardwick, `US-VT`.
    ~30/day is one device on its refresh interval.
  - **It is not the obvious dataset gap.** Probed staging with `Hardwick`, `Hardwick, VT`,
    `Hardwick, Vermont`, `Hardwick, US` and `05843`: **every one resolves `geocoder=local`**, to
    44.5,-72.4 (and bare `Hardwick` finds a Georgia one). So the dataset holds the town and the
    comma-qualifier path works. The vendor answers 44.4,-72.3 - a *different* point - and `city=`
    is our own reverse lookup labelling it, so the user is typing something we do not have, and
    Open-Meteo is landing them near Hardwick. Most likely a place under the `cities1000`
    population floor (a village or a neighbouring hamlet).
  - **Deliberately not knowable from here:** the raw place string is not logged, by design. Asked
    and answered 2026-09-02 - **do not add it**, and the reasoning matters because this will be
    re-proposed. `place` is free text with no granularity bound: a user can type a street address
    or a landmark, so it breaks the F1 ceiling the whole telemetry design rests on (see the PII
    section of `api/docs/geographic-telemetry.md`, whose one argument for allowing the city label
    is that a city name is *coarser* than `42.4,-71.1`). Direct log submission also skips the
    Agent's scrubbing, so there is no second line of defense, and the aggregate is already a
    home-location dataset with GDPR exposure. It would buy one string from one install.
  - If this one install is worth chasing, the lever is the population floor in
    `api/tools/GeoDataBuilder` - rebuild against `cities500` and re-probe the five spellings
    above - not more telemetry.
  - **If the vendor rate ever stops being one device** and the *shape* of the failing input is
    genuinely needed, the bounded version stays under the F1 ceiling: on the local-miss-then-
    vendor-hit path only, emit bucketed input length, comma-segment count,
    `GeoText.LooksPostal`, and the distance from the vendor's answer to the nearest local
    candidate. That separates misspelling / unqualified-ambiguous / below-the-floor without
    emitting anything personal. Do not build it before the rate justifies it.
  - **Revised exit condition.** "Quiet" needs restating, because 4.1% will not reach zero: the
    vendor is the *misspelling and long-tail* fallback and something will always miss. Either
    accept a small standing fallback rate and delete anyway (accepting that those users get a
    `place_not_found` instead), or keep the client. This is now a judgement call, not a
    measurement one - the measurement is done.
  - **The `lat_lon` item below may settle this without a decision.** It resolves the location in
    TRMNL's own autocomplete before our API is called, so free-text place input goes away and
    the forward geocoder - local *and* vendor - has no job left on that path. Worth checking
    whether that item lands before spending anything on this one.

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

- [x] **A declared country that matches nothing swallows the time-zone hint (fixed 2026-09-02)**
  - `CountryHint.Resolve` returned on the first branch that *parsed*, and
    `PostalJurisdictions.Accepting` never returns an empty set, so a readable Country dropdown
    value always won the slot - even when it intersected no candidate for the code being looked
    up. The intersection then emptied, every candidate survived as designed, and population
    decided: `country=aq_` answered **Guri-si, KR** for `02180` on a device whose time zone is
    `America/New_York`, where `hint=tz` had been answering Stoneham, MA. The log line read
    `declared=AQ ... hint=declared`.
  - **The fix is a chain rather than a first match.** `CountryHint.Candidates` returns the
    caller's signals strongest first; the geocoder appends the postal-only floors (a ZIP+4's
    implied US, then `HomeRegion`) and takes the first level whose set intersects the surviving
    candidates. A level that matches nothing is skipped, so declared -> time zone -> region floor
    all get their turn. Precedence between signals is unchanged: declared still beats the time
    zone whenever it matches something, and a typed qualifier still beats both.
  - **The reported hint now names the level that ranked**, carried back on `GeoMatch.Hint` rather
    than re-derived at the log line. The floors report `none`, which is what they reported before,
    so the existing `hint=` facet keeps its meaning.
  - **The measurement this item asked for comes free**, and was never taken beforehand
    deliberately: the case is now countable in the `ForecastServed` logs as `declared` set while
    `hint` is something else. Worth reading a week after this ships - the only observation so far
    is the synthetic one above, using a country nobody can select.
  - `PostalJurisdictions` already fixed one instance of this class - declaring `US` for a Puerto
    Rico ZIP once answered Warsaw - by widening the accepted set. The fall-through covers the
    general case, and would have covered that one too.
  - Two existing tests take a different route to the same answer and say so now: `75001` with `DE`
    or `PR` declared reaches the region floor, which holds both France and the US, so population
    still settles it.

- [ ] **Adopt `field_type: lat_lon` for the Location field (found 2026-09-02)**
  - TRMNL has a geographic field type the plugin is not using:
    [`lat_lon`](https://help.trmnl.com/en/articles/10513740-custom-plugin-form-builder.md) renders an
    autocomplete over cities, addresses and postal codes, and stores the pick as a comma-separated
    `"lat,lon"` string. The user may also type coordinates directly. The same article lists
    `xhrSelect` and `xhrSelectSearch`, which are also new to us.
  - **Why it is worth doing:** it kills the failure mode the current design most fears - a swapped
    coordinate pair, or a city name quietly matching the wrong continent, rendering the wrong
    weather with no error. Picking from a list makes that nearly impossible, which is the same
    problem `show_place` exists to let people catch only *after* the fact.
  - **The article's parsing TIP is fine.** It suggests
    `?lat={{ lat_lon | split: ',' | first }}` in the `polling_url`, and Liquid filters *do* run
    there (verified 2026-09-02; see `CLAUDE.md`). The earlier claim to the contrary came from
    `country`, a `select` whose value was slugified before the filter saw it, so the filter matched
    nothing - a slugification bug misread as a filter bug. `lat_lon` is not a select, so its comma
    survives and the split works. A filter does put `: ` in the YAML scalar, so the line has to be
    quoted, with single quotes around any filter argument. Sending `&latlon={{ lat_lon }}` raw and
    splitting server-side works too, and is what `country` does.
  - **Sequencing, which is the actual cost.** Not a drop-in swap for `place`:
    1. `lat_lon` resolves the location before our API is called, so it bypasses `TrmnlApi.Geo`'s
       *forward* geocoding entirely. ~~Finish the `weather.geocoder` reading above **first**, or
       that week of data is muddied mid-measurement.~~ **Unblocked 2026-09-02: the reading is
       taken.** A clean seven-day retake is still available on or after 2026-09-05, but the
       finding it would confirm (a flat ~4% vendor rate from a single install) is not one this
       work would muddy. Reverse lookup (coordinates to an on-screen label) still earns the
       dataset's keep.
    2. It also removes the reason for the **Country** field, since the autocomplete disambiguates
       interactively, and moots the deferred postal-collision item - Poland's `02-180` versus a US
       `02180` stops being ours to solve.
    3. Migration is the real work. The plugin is public and forked, existing installs have `place`
       set as a string, and a new keyname does not inherit those values. `place` has to stay as a
       fallback for a while, so the plugin briefly carries *three* input modes (`lat_lon`, `place`,
       deprecated `latitude`/`longitude`) before it carries two. Silently resolving to nothing is
       the one outcome to avoid.
    4. v2 only. v1 is frozen.

## Weather display & accuracy

- [ ] **Allow enabling/disabling the different subviews (current status, hourly forecast, daily forecast) and adjust layout accordingly**
  - The weather plugin renders three subviews: current conditions (`weather_current` / `weather_current_compact` templates in `plugins/weather/src/shared.liquid`), the hourly chart (`weather_hourly_chart`), and the daily forecast (`weather_daily_bars_vertical`). `full.liquid` renders all three in a fixed two-column layout (current + hourly on the left, daily bars on the right); `half_horizontal.liquid`, `half_vertical.liquid`, and `quadrant.liquid` each render subsets.
  - Add toggle custom fields to `plugins/weather/src/settings.yml` (e.g. `show_current`, `show_hourly`, `show_daily` — boolean/checkbox-style, defaulting on) alongside the existing `hours`/`days` fields, then conditionally `{% if show_x %}...{% endif %}` each `render` call in the layout `.liquid` files.
  - "Adjust layout accordingly" is the meatier part: when one or two subviews are disabled, the remaining view(s) should expand to fill the freed space rather than leave a gap — e.g. with only current+hourly enabled, the hourly chart should widen to full width; with only daily enabled, the daily bars should span the whole screen. Likely needs per-combination layout branches (or a flex container that reflows) and may require touching the Highcharts `chart_height`/width and the daily bars' vertical-vs-horizontal orientation.
  - Consider which layouts make sense for each combination (full vs half vs quadrant) and whether to gate some combinations as invalid.

- [x] **Investigate new TRMNL framework features and assess what could improve the weather plugin (done 2026-08-31)**
  - Surveyed release notes 3.0.0 through 3.3.0 and `docs/V4_BREAKING.md` in the now-open-source
    framework (cloned at `D:\source\usetrmnl\trmnl-framework`; the notes live in
    `public/framework/releases/`, which is the only complete changelog - there is no `CHANGELOG.md`
    and GitHub carries a release entry for 3.3.0 only).
  - **The plugin is now on `framework_version: 3.2.0`, not 2.3.7.**
  - ~~3.3.0 is unusable because the `trmnl_preview` gem allowlists framework versions in
    `db/data/framework_versions.yml` and stops at 3.2.0~~ - **wrong, corrected 2026-09-02.** That
    bundled file is only an *offline fallback*. `FrameworkVersion.config`
    (`lib/trmnlp/framework_version.rb:22`) first fetches the live manifest from
    `usetrmnl/trmnl-framework`, so the accepted list was never tied to the gem's release cadence.
    Verified by running the installed 0.11.0: 3.3.0 and 3.3.1 both resolve, 3.4.0 is correctly
    rejected, and `latest` reports 3.3.1. **No gem bump, no PR, no local patch is needed.**
  - **3.3.1 is the current latest** (released 2026-09-01; 3.3.0 landed 2026-08-27, i.e. before this
    survey was written). Upstream `trmnlp` `main` goes further still - the `fix-unknown-framework-version`
    work (#126) drops the manifest check for a `Gem::Version.correct?` gate, so any well-formed
    number the CDN serves is accepted - but that is unreleased and not required here.
  - `trmnlp lint` does *not* check `framework_version`, so lint passing still proves nothing about it.
  - **What actually moved on screen, measured by rendering the same data at 2.3.7 and at 3.2.0**
    (`trmnlp build --png`, OG 1-bit): the title bar's background dither got lighter and finer, its
    border went from crisp to nearly invisible, and text renders a shade lighter. All three trace to
    3.2.0 rebuilding borders/outlines and color patterns from image tiles to generated vector art,
    on top of the known 14-step dither rescale. Nothing broke; `bg--gray-30` on the daily bars is
    the one deliberate shade shift (25% -> 31.25% lightness; `bg--gray-25` restores the old look).
  - **The one thing the preview cannot answer, and the reason to look at a real OG device:**
    3.1.0 flipped the implicit low-density font bundle from Classic to the TRMNL pixel fonts
    (TRMNL12/16/21), so an OG screen changes typeface on this bump. `trmnlp` renders the same face
    at both versions, so preview is blind to it. **A plugin cannot opt out**: the escape hatch is
    `screen--fonts-classic` on the *screen root*, which TRMNL emits and a plugin's Liquid renders
    inside of. High-density (TRMNL X) is unaffected and stays on Inter Variable.
  - Deprecation sweep against the templates came back nearly clean: no `font--*`, no numbered
    `border--h-{1..7}`, no `divider--on-*`, no `gap--space-between`, no `dark:`. All three
    `text-stroke--*` uses already pair with the base `text-stroke` class, which is the contract V4
    unifies on. Only `shrink-0` (8 uses) is unsettled upstream - `no-shrink` vs `shrink-0`, one of
    the two goes - and there is nothing to do until they decide.

- [x] **Adopt `TRMNLCharts` for the hourly chart** (done 2026-08-31, `c99e642`)
  - `weather_hourly_chart` now builds on `TRMNLCharts.options()` / `.merge()` inside
    `TRMNLCharts.watch()`. Every `#000` became `.paint('black')`, both frozen pattern-image URLs
    (`images/grayscale/gray-{4,7}.png`) became `.paint('gray-45')` / `.paint('gray-70')` resolved
    from the pinned framework CSS, axis typography comes from `.textStyle('chart-label')`, and the
    `fixChartFonts()` MutationObserver is gone.
  - **The observer was the real bug.** It forced `font-family: Inter !important` onto every axis
    label, which since 3.1 fights the framework: an OG screen renders in TRMNL12 and 3.2's
    `_chart.scss` styles `.highcharts-axis-labels text` from `--font-small-*`. The chart was the
    one part of the screen still in Inter.
  - **Unplanned win on TRMNL X**: `paint()` returns flat 4-bit grays there instead of the 1-bit
    dither tiles the old hardcoded URLs forced onto a 16-shade screen.
  - `TRMNLCharts` turned out to be **in 3.2.0**, verified against the shipped
    `https://trmnl.com/js/3.2.0/plugins.js`; 3.3.0 added maps only. The blocked 3.3.0 bump was not
    a prerequisite, and `trmnlp` loads the same pinned runtime, so preview matched the device.
  - **Three things this item claimed that did not hold up:**
    1. *It does not retire the linter workarounds.* The plugin sits at 4 of 6 and always did, all
       four `padding` from the `.marker-pad` shim (`shared.liquid:75,406`). The chart contributes
       zero: `['mar'+'gin']` / `['pad'+'ding']` are split strings and `fontSize` is camelCase.
       `options()` supplies no `chart.margin`, so the computed keys stay. Freeing budget is still
       the `.marker-pad` item below, and nothing else.
    2. *`TRMNLPaint.px()` cannot replace the `isLg` flag.* `--content-scale` resolves to 1 on both
       OG and TRMNL X; `--device-ui-scale: 0.8` only applies to Kindle 2024 and Palma. The scale
       cascade is for user display-scale settings and BYOD oddities, not for device size. Dropping
       `isLg` for `px()` alone silently gave the X chart OG-sized margins. The device split is now
       a `screen--lg` class check on the resolved screen, which still beats `matchMedia`: it reads
       the screen rather than the browser viewport, so it stays right in a partial-width view, and
       `watch()` re-reads it. `px()` still wraps every number, which is the genuinely new
       capability the old code had none of.
    3. *The `chart-` id prefix buys almost nothing.* `_chart.scss` is only
       `[id^="chart-"] { height: auto; overflow: visible }`, and the height half is overridden by
       the inline style and by `full.liquid`'s `!important` anyway. Renamed to `chart-hourly-*`
       regardless, for the `overflow: visible`.
  - Two more things worth knowing before touching this chart again: the lint rule greps the markup
    for a literal `animation: false` and cannot see the value `options()` sets at runtime, so it is
    restated explicitly; and the framework defaults a **vertical** x-grid on, which is redundant
    here and is turned off.
  - **Still unverified on hardware**: the axis labels are now TRMNL12 at 12px where they were 16px
    Inter. Preview says legible; a real OG screen has the last word, and the same look settles the
    3.1 font-flip question left open by the 3.2 bump. Pushed to the staging plugin (316595).

- [x] **Replace the hand-rolled row limits with a measured fit** (done 2026-08-31)
  - The three `nth-child` cutoffs and all four `num_days` constants are gone. Every layout renders
    all `days` entries and a script at the end of `weather_daily_bars_vertical` measures the column,
    then hides one row at a time from the bottom until the last visible row is inside. Today's row
    always stays, since it carries the current-temperature marker.
  - **Every count the constants enforced was pessimistic.** Measured, before -> after: quadrant
    1 -> 2 (OG) and 3 -> 5 (X), half_horizontal 3 -> 4 and 4 -> 6, half_vertical 5 -> 6 and 4 -> 6.
    Those rows fit the whole time. At `days: 14` nothing overflows anywhere (OG full settles on 12,
    X full takes all 14, X portrait 8); at `days: 2` nothing is hidden.
  - **This item named the wrong engine, and neither framework engine fits.** `data-list-limit`
    filters a container's children to `.item`/`.label` (`plugins.js:948`), so it would have ignored
    every `.daily-row` while still writing `max-height` on the container and clipping a row
    mid-glyph. `data-content-limiter` does handle arbitrary children, but its auto height
    measurement resolves from the nearest `.layout` (`plugins.js:2793-2846`) and our rows sit
    several flex levels below one, so it needs an explicit pixel budget - and it also shrinks text
    via `content--small` and clamps the last row mid-content rather than dropping it whole.
  - **No static budget can express it either**, which is why the fit is measured in the browser.
    The space the layout leaves differs per device *and* orientation: half_vertical wants 172px
    subtracted on OG but 486px on X, because the chart above it is `hidden lg:flex`; full wants 112
    in landscape and 694 in portrait, where the column reflows below the chart. As a fraction it is
    no better (full is 0.77 / 0.83 / 0.33).
  - Three things hold the measurement up, and removing any of them breaks it silently:
    1. `.daily-row` carries `shrink-0`. A flex column shrinks its children by default, so without it
       the rows squash to fit instead of overflowing and there is nothing left to measure.
    2. The fit applies `justify-content: flex-start` inline and clears it afterwards, so the
       column's `flex--evenly` spread returns. Measuring under `space-evenly` counts the distributed
       gaps against the budget and drops a row that fits. At `days: 6` the renders are pixel-alike
       to before, so this change is purely functional.
    3. It waits for `window.TRMNL_PLUGINS_READY` before measuring, because the framework's own
       layout pass moves row heights, and re-runs through `TRMNLPaint.watch` on a screen class
       change.
  - **Fixed a pre-existing flex bug on the way**: two ancestors in `full.liquid` had `min-height:
    auto`, so at 14 days the daily column grew past its share instead of staying in it, and reported
    its own overflow as available space. `min-height: 0` on both. The layout was already wrong
    there, independently of this change.

- [ ] **Use the framework's progress-bar component for the daily temperature range bars**
  **(watch item, not scheduled - 2026-09-02)**
  - The bars are hand-built: `border: 1px solid #000` on the track and a `bg--gray-30` fill
    (`shared.liquid`, `weather_daily_bars_vertical`), with `rounded lg:rounded--full` on both track
    and fill to fake the clipping a real component gets from `overflow: hidden`. Same class of
    hardcoding the hourly chart just shed.
  - `.progress-bar > .track > .fill` paints from `--framework-slot-progress-track-bg-*` and
    `-fill-bg-*`, so it dithers on a 1-bit screen and goes solid gray on a 4-bit one, and its border
    comes from `--framework-semantic-border-strong` instead of `#000`.
  - **The catch**: our bars are *ranges* (low to high), and the component's fill is anchored
    `left: 0`. It only works by overriding `left` inline per row. That functions today - inline beats
    the stylesheet - but it is off-label, so a future change to how `.fill` positions (`transform`,
    `inset`) would break the offset without breaking anything the framework tests.
  - **The cost**: height. The bars are `h--5 lg:h--7` today (20px OG / 28px X). The component offers
    6 / 12 / 24 / 32px x `--ui-scale`, and `--ui-scale` resolves to 1 on both OG and X (the same
    finding that shaped the chart work), so one size class means one height on both devices. Either
    take 24 everywhere or keep a height override and lose part of the point.
  - **Exit condition, stated exactly:** adopt when the framework stops anchoring `.fill` at
    `left: 0` - either an `inset`/offset-based fill or a range variant. Recheck on each
    framework release; 3.3.0 did not touch the component (its release notes cover maps,
    theming, Position utilities and outlines), and the Position utilities do not help here
    because their offsets reject percentages, which is exactly what a range offset needs.
  - **Do not adopt before then.** It costs an off-label inline `left` override per row plus a
    20px -> 24px height change on OG, which is more than the styling win is worth today.
  - **Promote it early if** the hand-built bars actually look wrong on a device: the 1-bit
    dithering is the thing the component would fix, and a present-tense defect outranks this.
  - Kept rather than deleted because the goal is right and the blocker is someone else's to
    lift: `#000` and `bg--gray-30` are exactly the hardcoding that made the 2.3.7 -> 3.2.0 bump
    shift things unexpectedly, and a release that changes how `.fill` positions would break an
    override adopted early - so this wants watching either way.

- [x] **Make the daily-forecast column widths measured instead of fixed pixels (done)**
  - The widths are gone from CSS. The script that already fits the rows now also measures what the
    day name and temperature actually need at whatever type the device resolved, takes the widest
    across the list, and writes it on every row.
  - **The bug was already live on the flagship, not just on a BYOD panel.** "Wednesday" measures
    92px at 16px type inside a 90px box on the TRMNL X, and `.day-label` has `overflow: visible`, so
    a single word that cannot wrap ran under the weather icon on every X screen showing a Wednesday.
    On OG it measured 68px in a 68px box: exactly zero slack. Boxes are now 70 / 94.
  - **Container query units, which this item named as the fix, cannot do it**, and the reasoning
    that got written down here was wrong twice over:
    1. **There is no container context to resolve against.** A probe with `width: 100cqw` inside
       `.daily-list` comes back as the *browser viewport* (1280px in preview); `containerType` is
       `normal` on every ancestor, because these templates never render a `.layout` element, which is
       what establishes it. The existing `w--[36cqw]`/`w--[64cqw]` in `full.liquid:8,12` land on the
       right pixel by luck: flex-shrink produces 275.0px from a viewport basis and 275.0px from a
       percentage basis, and the measured value is 276.6px. Anything that relied on `cqw` meaning
       "share of the slot" would be reading the window instead.
    2. Even with a `.layout` added, `cqw` is a share of the *slot*, not of the daily column, and the
       column is 36% of the slot in `full` and effectively all of it in `quadrant`. One number cannot
       serve both.
  - **Auto-abbreviation, added at the same time**, closes [#1](https://github.com/lucaspimentel/trmnl-plugins/issues/1):
    if the bar would be left under a quarter of the column, the names fall back to their short form
    and the widths are re-measured once. **Abbreviate Day Names** stays as a manual override, and the
    swap only ever goes full to short, so setting it still means something on a wide screen.
  - **The ▼ marker had to be reworked in the same change, not by choice**: it was aligned by a
    hand-summed `padding-left:148px` (68 + 2 + 36 + 2 + 34 + 4 + 1) plus a `screen--lg` copy, which
    measured widths invalidate. It now reads the first bar's rect. That offset was already 5px wrong
    on X and only looked right because the two errors cancelled.
  - **Side effect: the recipe linter budget went from 4 of 6 to 0.** Those four `padding` substrings
    were the marker shim and were the plugin's entire spend.

- [x] **~~Move the marker to the framework's Position utilities~~ (dropped 2026-09-02 - nothing left to win)**
  - The item existed for the recipe-linter budget, and that is already banked: the `padding-left`
    shim and its `screen--lg` copy are gone as a side effect of the measured column widths above,
    and the count is 0 of 6.
  - What remained was swapping two inline declarations for 3.3's Position utilities while the
    inline `left:{{ pct }}%` stays regardless, since those offsets take neither `[Npx]` nor a
    percentage. The marker is placed by `placeMarker` in JS (`shared.liquid:503-510`), so nothing
    observable changes. Churn in working code, with a live risk of regressing the placement, for no
    gain on screen.
  - Unlike the progress-bar item above, no future framework release makes this worth more than it is
    today, which is why it is dropped outright rather than kept as a watch item.

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

- [x] **P0 — Rotate the Pirate Weather API key (done 2026-08-31)**
  - Rotated after the header-auth fix was live in both environments, so the new key never lands in a
    span resource name. The old key is still disclosed to anyone with Datadog read access until the
    spans carrying it age out of retention; nothing further can be done about that.
  - The leak is closed as of 2026-08-31: `PirateWeatherClient` now sends the key in the `apikey`
    header with a constant `header-auth` placeholder in the path, so the span resource name reads
    `GET api.pirateweather.net/forecast/header-auth/{lat},{lon}` and carries no secret.
  - Header auth is documented in prose only, is contradicted by an earlier line in the same file,
    and is absent from their OpenAPI spec. `PirateWeatherClientTests` pins it; a 401 in production
    is the signal it went away. Details in the code comment on `ApiKeyHeaderName`.
  - **Verifying this taught a lesson worth keeping: `api.pirateweather.net` flaps.** Inside one
    ten-minute window the same key returned 200, then 401, then 404, then 200 again, on *both* auth
    methods. A single curl proves nothing about this API. What separated "my change broke it" from
    "upstream is unwell" was that path auth was failing too, and then 12/12 at 200 for both methods
    once it settled. Measure a rate, never a single call.

- [ ] **P2 — Pirate Weather spans carry exact coordinates in the resource name**
  - Fallout found while fixing the key leak, and not addressed by that fix. The resource name is
    `GET api.pirateweather.net/forecast/header-auth/44.17,-72.53`: the coordinates are a path
    segment, so they are still there.
  - Two problems, neither fatal. It contradicts the repo's own rule that coordinates are PII and get
    rounded to `F1` before reaching a span. And every distinct coordinate is a distinct APM
    resource, which is a cardinality problem in its own right.
  - Open-Meteo is unaffected: its coordinates ride in the query string, which
    `DD_HTTP_CLIENT_TAG_QUERY_STRING=false` already strips. Pirate Weather is the only one putting
    them in the path, and the API requires it there.
  - No clean lever found yet. Worth checking whether the tracer can be told to rewrite or drop the
    resource name for that one host before reaching for anything more invasive.

- [x] **~~P0 — The Pirate Weather API key is being written into Datadog span resource names~~ (fixed 2026-08-31)**
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
  - **Investigated 2026-08-31, and the answer is better than feared: there are no new layout files
    to write.** Fluid Mashup is `mashup--3x3`, a 3x3 CSS grid whose cells are placed with
    `mashup-cell--col-{1..3}` / `--col-span-{1..3}` / `--row-{1..3}` / `--row-span-{1..3}`
    (`app/assets/stylesheets/framework/base/_mashup.scss:156-216` in the framework clone). A plugin
    still supplies the same four views; core wraps the chosen one in a `.mashup-cell`.
  - **The cause of the reported cropping is now nameable.** A view inside a cell always fills the
    cell, and `w--*` / `h--*` utilities on it are **ignored** - the cell, not the view, owns the
    size. So every fixed pixel width and every `nth-child` row cutoff in this plugin is a guess made
    against a size that no longer holds, and at 3x1 or 1x3 the guess is simply wrong.
  - Which means this issue is mostly the same two fixes as the items above: proportional widths via
    container query units, and the Content Limiter measuring rows instead of a hardcoded count.
    `.layout` sets `container-type: size` inside a mashup cell exactly as it does in a full view, so
    `cqw`/`cqh` units resolve against the real slot. Do those two first, then re-test the reported
    3x1 / 1x1 / 1x3 sizes before designing anything mashup-specific.
  - One detail to know when testing: a cell always gets the compact title bar
    (`_title_bar.scss:131-144` matches `.mashup-cell .title_bar`), regardless of which view class
    the plugin supplied.
  - **Re-tested 2026-09-02, with the two prerequisite fixes in.** `tools/build-mashup-preview.sh`
    now renders any view inside a `mashup--3x3` cell of a given size, so this is repeatable:
    `bash tools/build-mashup-preview.sh plugins/weather --device x --screenshot --output _build/shots`.
    All nine cell sizes on X, which is the only device the platform offers them on.
  - **Two of the three reported sizes are already fixed.** On X, **1x1 is clean** (current
    conditions plus two daily rows, nothing clipped) and **1x3 is clean** (chart plus all six daily
    rows). The measured row fit and the measured column widths did the job the investigation
    predicted they would. **3x1 is still broken.**
  - **X results, all nine sizes.** Clean: 1x1, 1x3, 3x2, 2x3, 3x3. Broken: **3x1, 2x1, 1x2, 2x2**.
  - **The rule behind that split, which is the useful finding:** a cell fails exactly when it is
    *smaller in some dimension than the standalone view the layout was tuned for*. 3x2, 2x3 and 3x3
    are full-width or full-height, so they escape. What is left is not a mashup problem at all -
    it is the same fixed-pixel sizing, in the two places the earlier work did not reach:
    1. **The hourly chart height is a fixed pixel value** (`chart_height` -> `height:{{ }}px` at
       `plugins/weather/src/shared.liquid:148`; 230 in `full`, 200 in `half_horizontal`, 280 in
       `half_vertical`). It does not shrink, so in a short cell the current-conditions block above
       it is squashed to nothing and its text paints over the chart. That is the reported
       "overlap", and it is what 3x1 (260px tall on X, against the 390px `half_horizontal` was
       tuned for), 2x1 and 1x2 all show.
    2. **The current-conditions block has no shrink budget** - a 110px icon plus the large
       temperature plus the detail column, with no `min-width: 0` and no scaling. In 2x2 the left
       column measures 419px against the ~653px a standalone `full` gives it, so the detail text
       runs out over the daily bars.
  - **Widths are fine; that guess was wrong and is worth writing down.** Measured in the browser on
    the 2x2 page: cell 677px, `w--[64cqw]` -> 419px, `w--[36cqw]` -> 238px. `cqw` resolves against
    the cell, not the viewport, even though the plugin's root element is not a `.layout`. So the
    container-query work already holds up inside a mashup cell.
  - **OG does not enter into this: Fluid Mashup is a TRMNL X feature.** An earlier round of this
    item recorded OG cell findings (clipped detail text at 1x3, a truncated title-bar timestamp at
    1x1). They are struck, not fixed: the framework's mashup CSS is device-agnostic, so the harness
    will happily render an OG fluid cell that the platform will never serve, and those readings were
    a fiction. The harness now defaults to X and says so if asked for OG. OG still matters as the
    standalone-layout regression check, which is what it is used for below.
  - **Fix (1) is done, 2026-09-02. 3x1, 2x1 and 1x2 now render correctly.** The chart's flex
    ancestors got `min-height: 0` (`half_horizontal.liquid`, `half_vertical.liquid` and the
    template's own wrapper), which is what a flex item needs before it will go below its content
    height - the default `min-height: auto` is why a fixed-px chart refused to shrink at all. That
    alone was not enough: a shrunk chart is still drawn, and at ~50px Highcharts piles the hour
    icons and their labels onto a flat line. So `weather_hourly_chart`'s build now measures the
    container and, below a floor (150px dense / 110px otherwise - the vertical axis margin plus
    enough plot to read a curve against), hides the wrapper and returns `null` instead of building.
    That is the same call OG already makes below `lg`, where the chart is never rendered.
    `TRMNLCharts.watch` stores `buildFn() || null` and guards its own destroy, so returning `null`
    is safe. `weather_current_compact` also got `shrink-0`, so the block above the chart keeps its
    natural height rather than being squashed until its icon and text spill over the chart.
  - **Verified after the fix**: X `full` / `half_horizontal` / `half_vertical` / `quadrant` and OG
    `full` / `half_horizontal` / `half_vertical` / `quadrant` all unchanged, `trmnlp lint` clean.
  - **A regression found and backed out along the way, worth knowing.** The first attempt also put
    `style="min-height:0;"` on `full.liquid`'s left column - the element carrying `w--[64cqw]`.
    That silently broke the arbitrary-value width on OG `full`: the detail column collapsed to one
    character per line and the daily bars fell back to short day names. **Do not add an inline
    `style` attribute to an element that carries an arbitrary-value class**; put the rule in a
    `<style>` block instead. `full.liquid` needed no change anyway - the cells it lands in (2x2,
    3x2, 3x3) fail on width, not height.
  - **Fix (2) is done too, 2026-09-02. Every size in the sweep now renders correctly.** The
    current-conditions block fits horizontally the way the daily rows fit vertically:
    `weather_current_fit` measures what the details column may occupy - the block's width less the
    icon and the temperature, both `shrink-0` because they are the reading itself - and hides
    detail lines from the bottom until the widest one left fits. Wind first, then humidity, then
    feels-like; the condition is last, and if nothing fits the column goes entirely.
    `.current-details` also carries `min-width: 0; overflow: hidden`, so a script failure clips
    inside the column rather than running out over the daily bars. Measuring the column's own
    `scrollWidth` was tried first and is wrong: the column is flex-sized, so its width is an effect
    of the text in it and it cannot be asked whether the text fits - it dropped every line on OG
    1x1 where two fitted.
  - `fitDailyRows` changed with it: today's row is still the last to go but it does now go. A slot
    too short for even one row used to keep it and leave the ▼ marker floating over a row clipped
    away under the title bar, which is what the 1x1 cell showed once the details stopped filling that
    space.
  - **Verified across the sweep**: X 3x1 / 2x1 / 2x2 / 1x2 / 1x1 / 1x3 all clean, and all eight
    standalone layouts (four per device, OG included) unchanged. `trmnlp lint` clean. X 2x1 is the one
    that shows the graduated drop working - it keeps `Overcast` and `Feels 74°` and sheds the other
    two.
  - **A methodology note that cost a false alarm**: an early round of "regressions" was stale
    pages. Reusing one browser and navigating with `playwright-cli goto` leaves the previous page
    up when a navigation fails, so the screenshot is of the wrong view with nothing saying so. Both
    preview scripts now open a fresh browser per shot; see `CLAUDE.md`.
  - **Re-tested against the issue's own three configurations, 2026-09-02, and all three are
    clean.** Their screenshots were pulled down and compared shape by shape:
    - **the wide banner** (their "1x3", 1023x259) is the one captioned *significant overlap*, and
      the reported screenshot shows exactly the failure since fixed: the current-conditions block
      drawn on top of the chart. It now renders as current conditions with all four detail lines,
      three daily rows, and no chart - which is the legibility floor doing its job at 260px.
    - **the tall column** (their "3x1", 337x761), captioned *minimal cropping*, now carries full
      details, a readable chart and six daily rows where the reported shot managed four.
    - **the single cell** (their "1x1", 342x258), captioned *minimal padding needs*, renders
      cleanly with full details and two daily rows.
  - **Mind the axis order: the issue labels these rows x columns and this repo labels them columns
    x rows.** So its "1x3" is the wide banner and its "3x1" is the tall column - the exact opposite
    of the `--cell` values above, where 3x1 is three columns wide. Every finding in this item uses
    the repo's order. Check the shape, not the label; the harness usage text says so too.
  - **One difference from the reported screenshots that is not a defect.** The single cell shows
    two daily rows where the reporter's older build fitted three. Measured: the list gets 111px,
    the two rows cost 46 and 35, and a third would end at 131px. It genuinely does not fit, so the
    fit is right. The space went into a taller current-conditions block, not into slack. Worth
    revisiting only as a deliberate density decision about that block's padding in `quadrant`.
  - **Left alone deliberately**: the OG title bar truncates its timestamp (`Wed 1:15`) in a
    one-column cell. That is `title_bar`'s own text budget, not the current-conditions block, and
    it is a clipped label rather than content running over its neighbour. Worth a look only if a
    narrow cell turns out to be a shape anyone actually uses.

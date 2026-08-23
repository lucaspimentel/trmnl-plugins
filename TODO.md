# TODO

## Weather API reliability & availability

Improvements identified during a review of the caching and fallback workflow in the Azure Functions weather API. Ordered by impact-to-effort ratio (highest first).

- [ ] **P0 — Drop Open-Meteo back to the free tier once caching is strong enough to cover it**
  - Decision (2026-08-22): cancel the Open-Meteo paid subscription (see the now-reverted "Escape upstream per-IP daily quotas" item below) and go back to the free host. Compensate for the lost quota headroom with stronger caching instead of paying for quota.
  - **Diagnosis (2026-08-22):** API traffic is ~600 req/hr (~14,400/day) and growing as more users install the plugin. Datadog reported 23,330 upstream Open-Meteo calls in the last 48h (~11,665/day) — already above the free tier's 10,000/day cap, implying only a ~19% cache hit rate. `WeatherCacheOptions.FreshTtl` in Azure app config was already set to 30 min, matching the plugin's `refresh_interval` exactly, so a single warm instance should hit cache almost every poll — the ~19% hit rate instead lines up closely with `1/N` for Consumption plan running ~5 concurrent instances with no session affinity (each device's next poll has roughly a 1-in-5 chance of landing back on the instance holding its cache entry). This points to **instance fragmentation, not TTL, as the primary cause** of the low hit rate.
  - **Action taken (2026-08-22):** bumped `WeatherCache:FreshTtl` app setting from 30 to 35 min (cheap jitter-margin tweak, live in prod). Not expected to meaningfully fix the hit rate on its own — re-check upstream call volume after this has run a while; if it's still ~19-20%, that confirms fragmentation (not TTL) is the blocker and the fix has to be the shared L2 cache or migrating off Consumption to a persistent single instance (see hosting-migration discussion; either directly eliminates the fragmentation).
  - Blocking prerequisites, in order: the **P1 shared L2 cache** item (or equivalently, migrating to an always-on single-instance host) — this is the fix that actually addresses the diagnosed root cause — and the **P2 longer TTLs** item (so a rate-limited free tier degrades to stale-served instead of 502). Negative caching and background refresh (both P2 below) further reduce live-call volume and are worth doing before the switch too.
  - Steps to actually revert: remove the `OPEN_METEO_API_KEY` app setting from prod and staging (`OpenMeteoClient` already falls back to the free host when it's absent — no code change needed), then cancel the Open-Meteo paid subscription.
  - Do not flip this before the caching/fragmentation work lands — the paid tier was the fix for the 2026-08-19 double-429 outage, and reverting without a fix for the fragmentation would likely reproduce it, especially with traffic still growing.

- [x] **P1 — Pick freshest stale entry, not first found**
  - `api/src/TrmnlApi/Services/WeatherForecastOrchestrator.cs:94-97` uses `staleFallback ??= (cached, provider.Name)`, which locks in the first stale entry encountered (the requested provider's, since it's `chain[0]`). If the secondary provider has a more recent stale entry, we still serve the older one.
  - Fix: track all stale entries seen in the loop and pick the one with the highest `FetchedAt`.
  - Quick win — ~5 lines, no infra changes, directly improves availability when both providers are down.

- [ ] **P1 — Shared L2 cache (Azure Table Storage)**
  - `WeatherCache` uses `IMemoryCache` (per-process). On Y1 Consumption with multiple instances and frequent cold starts, the cache is effectively cold most of the time, which neutralizes the 3h `StaleTtl` defense.
  - Suggested: Table Storage as L2 (reuses the existing Function storage account, ~20ms reads, cheap). Keep `MemoryCache` as L1 in front.
  - Schema sketch: PartitionKey = provider, RowKey = `{lat:F2}_{lon:F2}_{units}`, serialized `WeatherResponse` + `FetchedAt`.
  - Biggest reliability lift — makes cold starts and scale-out events stop being failure modes. Bigger change than P1A but still scoped.

- [ ] **P2 — Negative caching for failing providers**
  - Today, every request to a sustained-failing provider eats the full retry budget (~30s) before falling back. The standard resilience handler's circuit breaker has `MinimumThroughput=100` which is too high to trip on this app's traffic.
  - Cache a sentinel like `weather:fail:{provider}` for ~60s after a provider returns a non-429 failure; skip the live call while the sentinel is present.
  - Medium effort. Worth doing if Datadog shows sustained upstream outages slowing responses.

- [ ] **P2 — Background refresh on stale-served**
  - When `WeatherForecastOrchestrator.GetAsync` serves a stale entry (`api/src/TrmnlApi/Services/WeatherForecastOrchestrator.cs:127-141`), the next request still has to wait for live retries again. A fire-and-forget refresh after returning stale would warm the cache.
  - Gotcha: fire-and-forget in Azure Functions Consumption is unsafe — the host may scale in and drop the task. Implement via a Durable Function activity or a queue-triggered refresh function instead.
  - Medium effort, decent value, depends on P1B (shared cache) for the warmed entry to be useful across instances.

- [ ] **P3 — Single-flight / request coalescing**
  - If N concurrent requests for the same `(provider, lat, lon, units)` hit a cold instance, all N call the upstream. A `SemaphoreSlim` per cache key would collapse them.
  - Low priority given current low-concurrency traffic from TRMNL devices, but cheap to add.

- [ ] **P3 — Cleanup: make `TimeProvider` required in `WeatherCache`**
  - `api/src/TrmnlApi/Services/WeatherCache.cs:7` declares `TimeProvider? timeProvider = null` and defaults to `TimeProvider.System`. DI always supplies one (registered in `api/src/TrmnlApi/Program.cs:40`), so the null-default is dead code.
  - Make the parameter required; drop the null coalesce.

- [x] **P1 — Escape upstream per-IP daily quotas (Open-Meteo paid key or self-host)**
  - Diagnosed 2026-08-19: prod Function App's outbound IP hit Open-Meteo's per-IP daily limit — `meta.upstream` on `stale_served` responses showed `Open-Meteo returned 429 TooManyRequests: {"reason":"Daily API request limit exceeded. Please try again tomorrow."}`. Resets at UTC midnight. Pirate Weather was also 429'd (`"API rate limit exceeded"`), so neither provider was usable and the orchestrator correctly returned 502.
  - Options: (a) sign up for an Open-Meteo API key on a paid tier (no daily limit, higher quota), or (b) self-host Open-Meteo (it's open source) to escape per-IP limits entirely. Also verify the Pirate Weather key's plan/limits.
  - This is the root-cause fix for the 502s; the cache/fallback items above only mask it.
  - **Resolved 2026-08-19** via option (a): subscribed to Open-Meteo's paid tier. `OpenMeteoClient` now sends requests to `customer-api.open-meteo.com` with `&apikey=` when the `OPEN_METEO_API_KEY` app setting is present, falling back to the free host when it is not. Key set in both prod and staging; deployed and verified (`meta.cache: fresh_fetch`, `meta.provider: open-meteo`). The customer host rejects unkeyed requests with 401 and invalid keys with 400, so a successful fetch confirms the key is in use.
  - Still open: Pirate Weather remains on its free tier and was observed 429ing on 2026-08-19; its plan/limits have not been verified. It is now the fallback rather than the primary, so this is lower impact.
  - **Being reverted (2026-08-22):** decided to drop back to the free tier and rely on stronger caching instead of paying for quota headroom. See the new P0 item above for the reversal plan and its prerequisites.

- [ ] **P2 — Dedicated outbound IP (NAT Gateway) for prod Function App (on hold — premised on staying on a paid/quota-sensitive tier)**
  - Open-Meteo's daily quota is per source IP. A dedicated/consistent outbound IP (Azure NAT Gateway) prevents prod's limit from being shared with other Azure tenants and gives a stable IP to reason about / allowlist.
  - Lower priority than the paid-key fix above (which removes the quota ceiling entirely), but worth pairing with it for predictability.
  - On hold pending the free-tier reversion above: if caching brings live-call volume down enough, a dedicated IP may not be worth the Azure NAT Gateway cost. Revisit only if the free tier proves insufficient even with stronger caching.

  - [x] **P2 — Reduce upstream load by raising plugin `refresh_interval`**
  - `plugins/weather/src/settings.yml` sets `refresh_interval: 30` (minutes). Every TRMNL device × poll hits the API and counts against upstream per-IP quotas. Raising it directly cuts upstream call volume.
  - Trade-off: less fresh on-screen data. Consider 30 → 60 as a low-risk middle ground, or make it adaptive once the shared L2 cache (P1 above) is in place.

- [ ] **P2 — Tighten resilience handler: circuit breaker + jittered exponential backoff**
  - `api/src/TrmnlApi/Services/WeatherResilience.cs` only customizes the retry *predicate* (`ShouldRetry` skips 429 so the orchestrator falls back fast). The rest of `AddStandardResilienceHandler` uses Polly defaults — circuit breaker `MinimumThroughput=100` is too high to ever trip at this app's traffic (noted in the negative-caching item above), and the retry backoff is the default non-jittered exponential.
  - Add jitter to the retry backoff and lower the circuit-breaker thresholds (or switch to a custom `AddResilienceHandler`) so a sustained upstream failure trips the breaker instead of every request eating the full retry budget.
  - Pairs well with the negative-caching item (P2 above); both reduce wasted upstream calls when a provider is down.

- [ ] **P2 — Lengthen forecast cache TTLs so 429s don't surface as customer-visible 502s**
  - `api/src/TrmnlApi/Services/WeatherCache.cs` keys on `FreshTtl`/`StaleTtl` (absolute expiration set to `StaleTtl`). When both providers are rate-limited (as on 2026-08-19), once the stale entry expires the orchestrator returns 502.
  - Raising `StaleTtl` (and optionally `FreshTtl`) widens the window during which a rate-limited provider is masked by a stale-served response instead of surfacing a 502.
  - Trade-off: staler on-screen data during prolonged outages. Depends on P1B (shared L2 cache) for the longer TTL to actually help across instances/cold starts.

- [ ] **P2 — Alert on upstream 429 rates for api.open-meteo.com and api.pirateweather.net**
  - No alerting today on dependency rate-limiting. The 2026-08-19 double-429 was found reactively via `meta.upstream` on `stale_served` responses, not by an alert.
  - Add a monitor/alert on 429 response rates (and upstream failure rates generally) for both providers so quota exhaustion or upstream outages are caught before users see 502s.
  - Likely via Application Insights / Azure Monitor custom metrics emitted from `WeatherForecastOrchestrator` or the resilience handler.

- [ ] **P3 — Verify graceful stale-cache response when both providers are unavailable**
  - `WeatherForecastOrchestrator.GetAsync` serves stale entries when a provider fails (`api/src/TrmnlApi/Services/WeatherForecastOrchestrator.cs:127-141`), but the behavior when *both* providers are down and the stale entry has expired (502) is the customer-visible failure mode seen on 2026-08-19.
  - Confirm via test or manual repro that the fallback path serves the freshest stale entry while any non-expired one exists, and that the 502 returned after expiry is well-formed (not a raw exception/500). Add a regression test if none covers the both-providers-down path.
  - Related to the resolved P1 "Pick freshest stale entry" item; this is the verification/coverage counterpart.

- [ ] **Remove the `WeatherProviders` config — always default to open-meteo first**
  - `api/src/TrmnlApi/Program.cs:29` reads `builder.Configuration["WeatherProviders"]` and `ParseWeatherProviders` (Program.cs:39-58) throws if it's missing/empty. The order from this setting defines the default + fallback order in `WeatherProviderResolver` (`api/src/TrmnlApi/Providers/WeatherProviderResolver.cs`).
  - Goal: drop the config entirely and hardcode open-meteo as the primary (default), with pirate-weather as the only fallback. This removes a required app setting and a startup-failure mode (app refuses to start if `WeatherProviders` is unset).
  - Touch points: remove `ParseWeatherProviders` + the `configuredProviders` local in Program.cs; pass a fixed `[OpenMeteoProvider.ProviderName, PirateWeatherProvider.ProviderName]` order to `WeatherProviderResolver` (or simplify the resolver to derive order from DI registration). Update `WeatherProviderResolverTests` (several tests assert on `configuredOrder` behavior — e.g. `Resolve_NullOrEmptyName_ReturnsFirstConfiguredProvider`, `ResolveChain_FollowsConfiguredOrderNotRegistrationOrder`, `Resolve_NameRegisteredButNotConfigured_ThrowsArgumentException`). Also remove `WeatherProviders` from `local.settings.json` / app settings in prod & staging.

## Weather display & accuracy

- [x] **Show clock time instead of "Now" for the first hourly entry**
  - `api/src/TrmnlApi/Services/WeatherTransformer.cs:51` sets `label = loopIndex == 0 ? "Now" : HourLabel.Format(time)`. The first hourly bucket carries the model temperature for the current hour, which can differ from `current.temperature` (e.g. 68° hourly vs 72° current observed for the same moment), so labeling it "Now" reads as inconsistent next to the current temp.
  - Fix: drop the special-case and just use `HourLabel.Format(time)` for index 0 so it shows the actual hour (e.g. "10am") like the rest of the chart.

- [ ] **Allow enabling/disabling the different subviews (current status, hourly forecast, daily forecast) and adjust layout accordingly**
  - The weather plugin renders three subviews: current conditions (`weather_current` / `weather_current_compact` templates in `plugins/weather/src/shared.liquid`), the hourly chart (`weather_hourly_chart`), and the daily forecast (`weather_daily_bars_vertical`). `full.liquid` renders all three in a fixed two-column layout (current + hourly on the left, daily bars on the right); `half_horizontal.liquid`, `half_vertical.liquid`, and `quadrant.liquid` each render subsets.
  - Add toggle custom fields to `plugins/weather/src/settings.yml` (e.g. `show_current`, `show_hourly`, `show_daily` — boolean/checkbox-style, defaulting on) alongside the existing `hours`/`days` fields, then conditionally `{% if show_x %}...{% endif %}` each `render` call in the layout `.liquid` files.
  - "Adjust layout accordingly" is the meatier part: when one or two subviews are disabled, the remaining view(s) should expand to fill the freed space rather than leave a gap — e.g. with only current+hourly enabled, the hourly chart should widen to full width; with only daily enabled, the daily bars should span the whole screen. Likely needs per-combination layout branches (or a flex container that reflows) and may require touching the Highcharts `chart_height`/width and the daily bars' vertical-vs-horizontal orientation.
  - Consider which layouts make sense for each combination (full vs half vs quadrant) and whether to gate some combinations as invalid.

- [x] **Add a 24-hour clock format option (am/pm vs 24h)**
  - User feedback via Discord (MischaBoender, 2026-06-16): wants times shown as 24h instead of am/pm.
  - Hour labels are formatted server-side in `api/src/TrmnlApi/Mappings/HourLabel.cs:5-15` (`HourLabel.Format`), used for the hourly chart's x-axis labels. Sunrise/sunset times and the "Updated"/"Cached" timestamp in `title_bar` may also need the same treatment; audit all places times are rendered.
  - Needs a new setting (e.g. `time_format` select: `12h` / `24h`) in `plugins/weather/src/settings.yml`, passed through `polling_url` to the API, and a second format branch in `HourLabel.Format` (or an overload taking the format).

- [ ] **Investigate the 6-day forecast limit on TRMNL X (user feedback: more days requested)**
  - User feedback via Discord (MischaBoender, 2026-06-16): TRMNL X has visible space for more than 6 days of forecast, wants the limit raised.
  - Currently hardcoded at 6: `plugins/weather/src/settings.yml:58-66` (`days` field, `max: 6`) and the API's `days` query param is capped 1-6 (per `plugins/weather/CLAUDE.md`). Need to check whether Open-Meteo's daily response actually supports more than 6 days (it does, typically up to 16) — the 6-day cap looks like a plugin/API design choice, not a data source limitation.
  - `weather_daily_bars_vertical` (`plugins/weather/src/shared.liquid`) renders a fixed count per layout (full: 6, half_horizontal: 4, half_vertical: 5, quadrant: 3) — raising the max would need layout/width testing on the TRMNL X's larger canvas (`screen--lg`) specifically, since OG may not have room.

- [ ] **Investigate new TRMNL framework features and assess what could improve the weather plugin**
  - The TRMNL UI framework is now open-source at https://github.com/usetrmnl/trmnl-framework ("TRMNL ePaper design system", a Rails engine), with updated docs at https://trmnl.com/framework. The plugin currently pins `framework_version: 2.3.7` in `plugins/weather/src/settings.yml`.
  - Research scope: review recent framework releases/commits (the repo went open-source ~2026-08, latest commits reference a 3.x line and a 3.2.0 re-cut) and the updated docs (Guides, Arrangement, Responsive utilities, Styling, Typography, Runtime, Paint, Sass, Themes, Variables, Foundation, Elements, Components) for new components/utilities that the weather plugin could adopt.
  - Candidate areas to evaluate: anything that simplifies the current hand-rolled layout work in `plugins/weather/src/{full,half_horizontal,half_vertical,quadrant}.liquid` and `shared.liquid` (e.g. better responsive utilities, arrangement primitives, or chart/data-viz helpers), new runtime features relevant to the Highcharts hourly chart, and whether bumping `framework_version` is safe/worthwhile. Cross-reference against the existing subview-toggle task (current/hourly/daily enable/disable + layout adjustment) since new layout primitives could make that work easier.
  - Output: a short findings note (here or in a follow-up set of TODO items) of concrete, actionable improvements.

- [ ] **Improve cross-user cache dedup for nearby coordinates (low priority — likely drop)**
  - `api/src/TrmnlApi/Services/WeatherCache.cs:31-32` keys on the requested `lat`/`lon` rounded to `F2`. Idea was to share entries between requests that resolve to the same provider grid cell.
  - **Coarsening the key (round to a fixed grid) does not work** — tested F1 (0.1 deg) vs raw F2 against Open-Meteo at `42.36,-71.06` (2026-06-06): the two requests land in different grid cells **4.23 km apart**, with current temp differing **1.4 F** and hourly temps up to **3.2 F**. Open-Meteo serves a high-res grid (~1-2 km) here, finer than F2, so F1 jumps several cells and degrades accuracy. The F2 key already matches the raw request closely.
  - **Snapping to the provider's resolved coords has a chicken-and-egg problem**: the snapped coords only come back *in the response*, so you can't key a cache *read* on them without first calling the provider (defeating the cache). The only correct form is a `requested-key -> snapped-key` alias map (two-level lookup), which still costs one provider call per distinct requested coordinate to learn its mapping.
  - **Conclusion**: not worth it for this workload. TRMNL devices poll with fixed per-device coords, which already hit the F2 cache; the only upside (cross-user dedup) requires many geographically-clustered users and costs measurable accuracy. Revisit only if usage shows coordinate clustering, and only via the alias-map approach.

## Docs & tooling

- [x] **Document `trmnlp build --png` in CLAUDE.md or the trmnl-dev skill**
  - `trmnlp build` (added PNG support in trmnl_preview 0.8.1; we're now on 0.8.7) renders templates to static HTML, and `--png` also rasterizes each view to a PNG. Flags: `--width`, `--height`, `--color-depth` (1-8, e.g. `1` for OG 1-bit e-ink).
  - It's the lightweight built-in alternative to this repo's `tools/build-preview.sh` (which wraps output in real TRMNL CSS/JS and screenshots variants via Playwright). Note the relationship so it's clear when to reach for each.
  - **Outcome (keep both):** compared the two on the weather plugin. `trmnlp build --png` runs JS (Highcharts renders) and quantizes bit-depth correctly, but its wrapper is a bare `<div class="screen">` — it never applies `screen--lg`/`screen--4bit`/`screen--portrait`, so the TRMNL X responsive layout and portrait don't render and `--width`/`--height` only resize the canvas. Not a replacement for `build-preview.sh`; they're complementary. Documented in root `CLAUDE.md` "Build Preview" and the trmnl-dev skill's `local-development.md`.

## Observability

- [ ] **Instrument the Railway API with Datadog APM (deferred Phase 3 fast-follow)**
  - The Railway migration (`api/docs/railway-migration.md`) shipped without Datadog traces (Phase 3 skipped): the `Datadog.Trace` tracer no-ops when no agent is reachable, and the old Azure mechanism does not carry over. The Azure App Service ran Datadog via the Windows site extension (`dd-appsettings.{production,staging}.json` — profiler DLL paths, named pipes, `DD_TRACE_TRANSPORT=DATADOG-NAMED-PIPES`). That is App-Service-Windows-specific and irrelevant on the Linux Railway container.
  - What's already in place: `Datadog.Trace` 3.43.0 is referenced in `api/src/TrmnlApi/TrmnlApi.csproj:10`, and `WeatherForecastOrchestrator.GetAsync` already creates a manual span (`Tracer.Instance.StartActive("weather.forecast")` with `TagCoord`, `WeatherForecastOrchestrator.cs:58-61`). So once an agent is reachable the app code needs little-to-no change; this is primarily a hosting/config task.
  - Approach: run the Datadog Agent as a separate Railway service in the same project (Railway private networking gives each service an internal hostname) rather than bundling it into the app image. The tracer defaults to `127.0.0.1:8126`; set `DD_AGENT_HOST` (and `DD_TRACE_AGENT_PORT` if non-default) on the app service to the agent service's internal hostname so traces ship to the sidecar.
  - Set unified-service-tagging env vars on the app service: `DD_SERVICE` (e.g. `trmnl-api`), `DD_ENV` (`production`/`staging`), `DD_VERSION` (git SHA or semver). The Dockerfile is Linux (`mcr.microsoft.com/dotnet/aspnet:10.0`), so no Windows profiler/pipes config is needed — the Linux tracer attaches via `CORECLR_PROFILER` env that the Datadog.Trace package sets automatically when `DD_DOTNET_TRACER_HOME`/agent env is present; verify auto-instrumentation of the ASP.NET Core HTTP pipeline and the two `HttpClient` providers (Open-Meteo, Pirate Weather).
  - Verify in Datadog: traces appear under the service, the `/api/v1/forecast` and `/health` endpoints are captured as spans, the `weather.forecast` manual span nests under the inbound HTTP span, and the upstream Open-Meteo/Pirate Weather calls show as separate spans. Add a monitor on upstream 429/failure rate (overlaps with the existing "Alert on upstream 429 rates" TODO item) and on the cache hit/miss split now that `meta.cache` is observable.
- [ ] **Pirate Weather needs its own API key (blocking the fallback-path test and any fallback trace coverage)**
  - Currently the Pirate Weather key is shared across Azure prod/staging and Railway and is returning 429, so `pirate-weather` requests silently fall back to open-meteo and the fallback path (and its trace coverage) is untestable. See `api/docs/railway-migration.md` open questions.

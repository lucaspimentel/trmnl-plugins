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

- [ ] **P2 — Negative caching for failing providers**
  - Today, every request to a sustained-failing provider eats the full retry budget (~30s) before falling back. The standard resilience handler's circuit breaker has `MinimumThroughput=100` which is too high to trip on this app's traffic.
  - Cache a sentinel like `weather:fail:{provider}` for ~60s after a provider returns a non-429 failure; skip the live call while the sentinel is present.
  - Medium effort. Worth doing if Datadog shows sustained upstream outages slowing responses.

- [ ] **P2 — Background refresh on stale-served**
  - When `WeatherForecastOrchestrator.GetAsync` serves a stale entry (`api/src/TrmnlApi/Services/WeatherForecastOrchestrator.cs:127-141`), the next request still has to wait for live retries again. A fire-and-forget refresh after returning stale would warm the cache.
  - The original "fire-and-forget is unsafe on Consumption" gotcha no longer applies now that the API runs as a single always-on container: a background task survives between requests, so a plain `Task.Run`/`IHostedService` refresh is workable. Still guard against stampedes (pairs with the single-flight item below).
  - Medium effort, decent value. No longer depends on the shared L2 cache, since one process owns the whole cache.

- [ ] **P3 — Single-flight / request coalescing**
  - If N concurrent requests for the same `(provider, lat, lon, units)` hit a cold instance, all N call the upstream. A `SemaphoreSlim` per cache key would collapse them.
  - Low priority given current low-concurrency traffic from TRMNL devices, but cheap to add.

- [ ] **P3 — Cleanup: make `TimeProvider` required in `WeatherCache`**
  - `api/src/TrmnlApi/Services/WeatherCache.cs:7` declares `TimeProvider? timeProvider = null` and defaults to `TimeProvider.System`. DI always supplies one (registered in `api/src/TrmnlApi/Program.cs:28`), so the null-default is dead code.
  - Make the parameter required; drop the null coalesce.

- [ ] **P2 — Dedicated outbound IP (on hold — only matters if the free tier is ever revisited)**
  - Open-Meteo's *free* daily quota is per source IP, and the API now egresses through a shared NAT address, so the free tier is unusable without a dedicated egress IP.
  - The current host offers no dedicated egress IP (its "static outbound IPs" are documented as possibly shared). A true dedicated IP would require egressing through a self-hosted forward proxy on a cheap VPS.
  - Moot while the paid Open-Meteo key is in use (it removes the quota ceiling entirely). Revisit only if the paid key is ever dropped.

- [ ] **P2 — Tighten resilience handler: circuit breaker + jittered exponential backoff**
  - `api/src/TrmnlApi/Services/WeatherResilience.cs` only customizes the retry *predicate* (`ShouldRetry` skips 429 so the orchestrator falls back fast). The rest of `AddStandardResilienceHandler` uses Polly defaults — circuit breaker `MinimumThroughput=100` is too high to ever trip at this app's traffic (noted in the negative-caching item above), and the retry backoff is the default non-jittered exponential.
  - Add jitter to the retry backoff and lower the circuit-breaker thresholds (or switch to a custom `AddResilienceHandler`) so a sustained upstream failure trips the breaker instead of every request eating the full retry budget.
  - Pairs well with the negative-caching item (P2 above); both reduce wasted upstream calls when a provider is down.

- [ ] **P2 — Lengthen forecast cache TTLs so 429s don't surface as customer-visible 502s**
  - `api/src/TrmnlApi/Services/WeatherCache.cs` keys on `FreshTtl`/`StaleTtl` (absolute expiration set to `StaleTtl`). When both providers are rate-limited (as on 2026-08-19), once the stale entry expires the orchestrator returns 502.
  - Raising `StaleTtl` (and optionally `FreshTtl`) widens the window during which a rate-limited provider is masked by a stale-served response instead of surfacing a 502.
  - Trade-off: staler on-screen data during prolonged outages. No longer blocked on a shared L2 cache — the single always-on process keeps one warm cache, so a longer TTL takes effect directly. Raising `FreshTtl` past the plugin's 60-min `refresh_interval` is also the main remaining lever for cutting upstream call volume.

- [ ] **P2 — Alert on upstream 429 rates for api.open-meteo.com and api.pirateweather.net**
  - No alerting today on dependency rate-limiting. The 2026-08-19 double-429 was found reactively via `meta.upstream` on `stale_served` responses, not by an alert.
  - Add a monitor/alert on 429 response rates (and upstream failure rates generally) for both providers so quota exhaustion or upstream outages are caught before users see 502s.
  - Application Insights was dropped in the hosting migration, so this now depends on the Datadog APM instrumentation item under "Observability" (or on scraping `GET /metrics`, which already exposes upstream-failure and cache-split counters, though they reset every restart).

- [ ] **P3 — Verify graceful stale-cache response when both providers are unavailable**
  - `WeatherForecastOrchestrator.GetAsync` serves stale entries when a provider fails (`api/src/TrmnlApi/Services/WeatherForecastOrchestrator.cs:127-141`), but the behavior when *both* providers are down and the stale entry has expired (502) is the customer-visible failure mode seen on 2026-08-19.
  - Confirm via test or manual repro that the fallback path serves the freshest stale entry while any non-expired one exists, and that the 502 returned after expiry is well-formed (not a raw exception/500). Add a regression test if none covers the both-providers-down path.

- [ ] **Remove the `WeatherProviders` config — always default to open-meteo first**
  - `api/src/TrmnlApi/Program.cs:29` reads `builder.Configuration["WeatherProviders"]` and `ParseWeatherProviders` (Program.cs:39-58) throws if it's missing/empty. The order from this setting defines the default + fallback order in `WeatherProviderResolver` (`api/src/TrmnlApi/Providers/WeatherProviderResolver.cs`).
  - Goal: drop the config entirely and hardcode open-meteo as the primary (default), with pirate-weather as the only fallback. This removes a required app setting and a startup-failure mode (app refuses to start if `WeatherProviders` is unset).
  - Touch points: remove `ParseWeatherProviders` + the `configuredProviders` local in Program.cs; pass a fixed `[OpenMeteoProvider.ProviderName, PirateWeatherProvider.ProviderName]` order to `WeatherProviderResolver` (or simplify the resolver to derive order from DI registration). Update `WeatherProviderResolverTests` (several tests assert on `configuredOrder` behavior — e.g. `Resolve_NullOrEmptyName_ReturnsFirstConfiguredProvider`, `ResolveChain_FollowsConfiguredOrderNotRegistrationOrder`, `Resolve_NameRegisteredButNotConfigured_ThrowsArgumentException`). Also remove the `WeatherProviders` environment variable from the prod & staging service config.

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

- [ ] **Improve cross-user cache dedup for nearby coordinates (low priority — likely drop)**
  - `api/src/TrmnlApi/Services/WeatherCache.cs:31-32` keys on the requested `lat`/`lon` rounded to `F2`. Idea was to share entries between requests that resolve to the same provider grid cell.
  - **Coarsening the key (round to a fixed grid) does not work** — tested F1 (0.1 deg) vs raw F2 against Open-Meteo at `42.36,-71.06` (2026-06-06): the two requests land in different grid cells **4.23 km apart**, with current temp differing **1.4 F** and hourly temps up to **3.2 F**. Open-Meteo serves a high-res grid (~1-2 km) here, finer than F2, so F1 jumps several cells and degrades accuracy. The F2 key already matches the raw request closely.
  - **Snapping to the provider's resolved coords has a chicken-and-egg problem**: the snapped coords only come back *in the response*, so you can't key a cache *read* on them without first calling the provider (defeating the cache). The only correct form is a `requested-key -> snapped-key` alias map (two-level lookup), which still costs one provider call per distinct requested coordinate to learn its mapping.
  - **Conclusion**: not worth it for this workload. TRMNL devices poll with fixed per-device coords, which already hit the F2 cache; the only upside (cross-user dedup) requires many geographically-clustered users and costs measurable accuracy. Revisit only if usage shows coordinate clustering, and only via the alias-map approach.

## Observability

- [ ] **Instrument the Railway API with Datadog APM (deferred Phase 3 fast-follow)**
  - The hosting migration shipped without Datadog traces: the `Datadog.Trace` tracer no-ops when no agent is reachable, and the old Azure mechanism does not carry over. The Azure App Service ran Datadog via the Windows site extension (profiler DLL paths, named pipes, `DD_TRACE_TRANSPORT=DATADOG-NAMED-PIPES`), configured by the since-deleted `dd-appsettings.{production,staging}.json`. That approach is App-Service-Windows-specific and irrelevant on the Linux container.
  - What's already in place: `Datadog.Trace` 3.43.0 is referenced in `api/src/TrmnlApi/TrmnlApi.csproj:10`, and `WeatherForecastOrchestrator.GetAsync` already creates a manual span (`Tracer.Instance.StartActive("weather.forecast")` with `TagCoord`, `WeatherForecastOrchestrator.cs:58-61`). So once an agent is reachable the app code needs little-to-no change; this is primarily a hosting/config task.
  - Approach: run the Datadog Agent as a separate Railway service in the same project (Railway private networking gives each service an internal hostname) rather than bundling it into the app image. The tracer defaults to `127.0.0.1:8126`; set `DD_AGENT_HOST` (and `DD_TRACE_AGENT_PORT` if non-default) on the app service to the agent service's internal hostname so traces ship to the sidecar.
  - Set unified-service-tagging env vars on the app service: `DD_SERVICE` (e.g. `trmnl-api`), `DD_ENV` (`production`/`staging`), `DD_VERSION` (git SHA or semver). The Dockerfile is Linux (`mcr.microsoft.com/dotnet/aspnet:10.0`), so no Windows profiler/pipes config is needed — the Linux tracer attaches via `CORECLR_PROFILER` env that the Datadog.Trace package sets automatically when `DD_DOTNET_TRACER_HOME`/agent env is present; verify auto-instrumentation of the ASP.NET Core HTTP pipeline and the two `HttpClient` providers (Open-Meteo, Pirate Weather).
  - Verify in Datadog: traces appear under the service, the `/api/v1/forecast` and `/health` endpoints are captured as spans, the `weather.forecast` manual span nests under the inbound HTTP span, and the upstream Open-Meteo/Pirate Weather calls show as separate spans. Add a monitor on upstream 429/failure rate (overlaps with the existing "Alert on upstream 429 rates" TODO item) and on the cache hit/miss split now that `meta.cache` is observable.

- [ ] **Pirate Weather needs its own API key (blocking the fallback-path test and any fallback trace coverage)**
  - Currently the Pirate Weather key is shared across the old Azure prod/staging apps and the current host, and is returning 429, so `pirate-weather` requests silently fall back to open-meteo and the fallback path (and its trace coverage) is untestable.

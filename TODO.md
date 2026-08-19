# TODO

## Weather API reliability & availability

Improvements identified during a review of the caching and fallback workflow in the Azure Functions weather API. Ordered by impact-to-effort ratio (highest first).

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

- [ ] **P2 — Dedicated outbound IP (NAT Gateway) for prod Function App**
  - Open-Meteo's daily quota is per source IP. A dedicated/consistent outbound IP (Azure NAT Gateway) prevents prod's limit from being shared with other Azure tenants and gives a stable IP to reason about / allowlist.
  - Lower priority than the paid-key fix above (which removes the quota ceiling entirely), but worth pairing with it for predictability.

- [ ] **P2 — Reduce upstream load by raising plugin `refresh_interval`**
  - `plugins/weather/src/settings.yml` sets `refresh_interval: 30` (minutes). Every TRMNL device × poll hits the API and counts against upstream per-IP quotas. Raising it directly cuts upstream call volume.
  - Trade-off: less fresh on-screen data. Consider 30 → 60 as a low-risk middle ground, or make it adaptive once the shared L2 cache (P1 above) is in place.

## Weather display & accuracy

- [x] **Show clock time instead of "Now" for the first hourly entry**
  - `api/src/TrmnlApi/Services/WeatherTransformer.cs:51` sets `label = loopIndex == 0 ? "Now" : HourLabel.Format(time)`. The first hourly bucket carries the model temperature for the current hour, which can differ from `current.temperature` (e.g. 68° hourly vs 72° current observed for the same moment), so labeling it "Now" reads as inconsistent next to the current temp.
  - Fix: drop the special-case and just use `HourLabel.Format(time)` for index 0 so it shows the actual hour (e.g. "10am") like the rest of the chart.

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

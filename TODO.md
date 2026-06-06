# TODO

## Weather API reliability & availability

Improvements identified during a review of the caching and fallback workflow in the Azure Functions weather API. Ordered by impact-to-effort ratio (highest first).

- [ ] **P1 — Pick freshest stale entry, not first found**
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

## Weather display & accuracy

- [ ] **Show clock time instead of "Now" for the first hourly entry**
  - `api/src/TrmnlApi/Services/WeatherTransformer.cs:51` sets `label = loopIndex == 0 ? "Now" : HourLabel.Format(time)`. The first hourly bucket carries the model temperature for the current hour, which can differ from `current.temperature` (e.g. 68° hourly vs 72° current observed for the same moment), so labeling it "Now" reads as inconsistent next to the current temp.
  - Fix: drop the special-case and just use `HourLabel.Format(time)` for index 0 so it shows the actual hour (e.g. "10am") like the rest of the chart.

- [ ] **Cache on the provider's snapped coordinates, not the requested ones**
  - `api/src/TrmnlApi/Services/WeatherCache.cs:31-32` builds the cache key from the requested `lat`/`lon` rounded to `F2`. Open-Meteo snaps requests to its nearest grid cell and returns the resolved coordinates in the response body (e.g. requested `42.37,-71.04` resolves to `42.35753,-71.02687`), so nearby requests that map to the same grid cell currently miss the cache.
  - Fix: key the cache on the provider's snapped coordinates (parsed from the upstream response) so requests resolving to the same cell share a cache entry. Coordinate with the L2 cache key design in the P1 Table Storage item above.

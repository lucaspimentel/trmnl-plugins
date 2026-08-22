# Migrating the Weather API from Azure Functions to Railway

Status: draft, not started. Branch: `lpimentel/railway-migration`.

## Motivation

`TrmnlApi` (`api/src/TrmnlApi`) currently runs as an Azure Functions app on the Y1 Consumption
plan. Diagnosis on 2026-08-22 (see `TODO.md`, "Weather API reliability & availability") traced
the app's low cache hit rate (~19%) to instance fragmentation: Consumption spreads requests
across multiple concurrent instances with no session affinity, so `WeatherCache`'s per-process
`IMemoryCache` rarely has the entry a given device's next poll needs, even though
`WeatherCache:FreshTtl` (30-35 min) already matches the plugin's `refresh_interval` (30 min).

At ~600 req/hr (~14,400/day) and growing, this pushed upstream Open-Meteo calls to ~11,665/day —
already above the free tier's 10,000/day cap, which is why the app is currently on Open-Meteo's
paid customer-API tier. The plan (`TODO.md` P0 item) is to drop back to the free tier once the
cache hit rate is fixed, instead of paying for quota headroom indefinitely.

Running the app as a single, persistent, always-on process eliminates the fragmentation
directly — no cold starts, no scale-out, one `IMemoryCache` that every request sees. This is a
cheaper and simpler fix than building a shared L2 cache (Azure Table Storage) on top of the
existing Consumption plan, and Railway (or an equivalent persistent-container host) is the
target discussed for that reason.

## Goals

- Serve `GET /api/v1/forecast` from a single always-on container, so `WeatherCache` behaves like
  a real warm cache instead of being fragmented across instances.
- Get upstream Open-Meteo call volume low enough, with margin for continued growth, to safely
  drop back to the free tier (10,000 calls/day).
- Keep the change surgical: `Services/`, `Providers/`, `Models/`, `Mappings/` are already
  framework-agnostic and should not need to change. Only the hosting layer changes.

## Non-goals (for this migration)

- The shared L2 cache TODO item is **not ruled out**. A single persistent instance fixes
  fragmentation but not restart-driven cache loss (see "L2 cache contingency" below). Build
  the initial migration without L2; add it as a contingency if Phase 5 shows restart-driven
  cold bursts materially degrading the hit rate.
- Negative caching, background refresh, circuit breaker tuning — independent TODO items, not
  blocking this migration.
- Multi-region / edge deployment — traffic is low and single-region is fine.

## Current architecture (recap)

- .NET 10 isolated-worker Azure Functions app (`Microsoft.Azure.Functions.Worker*`).
- Two routes today: `GET /api/v1/forecast` (anonymous). (`/api/v1/screen` was removed
  2026-08-22 — see git history — so it's out of scope here.)
- DI graph (`Program.cs`): `IMemoryCache`, `WeatherCacheOptions` bound from config,
  `IOpenMeteoClient`/`IPirateWeatherClient` HTTP clients with `AddStandardResilienceHandler`,
  `WeatherProviderResolver` built from the `WeatherProviders` config value,
  `WeatherCache`, `WeatherForecastOrchestrator`, `TimeProvider.System`.
- Observability: Application Insights (`AddApplicationInsightsTelemetryWorkerService` +
  `ConfigureFunctionsApplicationInsights`) and Datadog APM via the Azure App Service Windows
  site extension (`dd-appsettings.{production,staging}.json` — profiler DLL paths, named
  pipes, etc. This mechanism is Azure-App-Service-specific and does not carry over).
- Deploy: `func azure functionapp publish trmnl-plugins-api[-staging]` from a dev machine —
  no CI/CD workflow deploys this today (`.github/workflows/tests.yml` only builds and tests).
- Config: Azure App Settings (`WeatherProviders`, `OPEN_METEO_API_KEY`, `PIRATE_WEATHER_API_KEY`,
  `WeatherCache:FreshTtl`/`StaleTtl`).
- Base URL is hardcoded in five places: `plugins/weather/src/settings.yml` (`polling_url`),
  `plugins/weather/README.md`, `plugins/weather/CLAUDE.md`, `plugins/weather/fields.txt`, and
  root `README.md`.

## Target architecture

- Plain ASP.NET Core minimal API (`WebApplication.CreateBuilder`), containerized, deployed to
  Railway as a single service pinned to **one replica** (no autoscaling — the whole point is
  one warm process).
- Same DI graph as today, verbatim, minus the two Functions-specific telemetry calls.
- `Functions/WeatherFunction.cs` becomes `Endpoints/WeatherEndpoint.cs`: same validation
  (`RequestValidator`) and orchestrator call, `HttpRequestData`/`HttpResponseData` swapped for
  ASP.NET Core's `HttpRequest`/`IResult`.
- `Functions/RequestValidator.cs` moves alongside it (it has no Functions dependency). Keep the
  `TrmnlApi.Functions` namespace to avoid touching `RequestValidatorTests` (`using
  TrmnlApi.Functions;`) — the folder/namespace mismatch is cosmetic and not worth a test-file
  change in this migration.
- New `GET /health` endpoint for Railway's health checks.
- Config via environment variables (Railway's equivalent of Azure App Settings) — same names,
  no renaming needed (`WeatherProviders`, `OPEN_METEO_API_KEY`, `PIRATE_WEATHER_API_KEY`,
  `WeatherCache__FreshTtl`/`WeatherCache__StaleTtl` using ASP.NET Core's `__` section separator).
  **Pitfall**: `WeatherCacheOptions.FreshTtl`/`StaleTtl` are `TimeSpan` — a bare value like `35`
  parses as 35 **days**, not 35 minutes. Use `hh:mm:ss` form (e.g. `00:35:00`) exactly as the
  current Azure App Setting does. Verify the value verbatim during migration.

## L2 cache contingency (Redis on Railway)

A single persistent instance fixes fragmentation but not restart-driven cache loss. Every
restart — deploy, platform maintenance, health-check failure, OOM — wipes the in-memory
`IMemoryCache`, producing a warm-up burst of upstream calls equal to the distinct
`(provider, lat, lon, units)` keys polled in the first TTL window. The working set is tiny
(`SizeLimit=200`, ~600 req/hr) so OOM is unlikely and deploys are user-triggered, keeping the
practical risk low — but the burst is the same quota-exhaustion failure mode this migration
exists to prevent, just rarer.

If Phase 5 shows restart-driven cold bursts pushing upstream calls toward the 10,000/day cap,
add an L2 cache backed by Redis on Railway:

- **Cost**: Railway doesn't offer a fixed-price managed Redis tier; you deploy Redis as a
  regular service and pay per resource (~$10/GB RAM, ~$20/vCPU). A minimal instance (0.1 vCPU,
  256 MB RAM) runs ~$5-7/month — well within the Hobby plan ($5/month + $5 usage credit).
- **Topology**: Redis as a second service in the same Railway project, reachable via Railway's
  private networking (each service gets an internal hostname). No public exposure needed.
- **Implementation**: `Microsoft.Extensions.Caching.StackExchangeRedis` for `IDistributedCache`
  as L2 behind the existing `IMemoryCache` L1. On a restart, L1 is cold but L2 is warm — the
  first request hits L2, populates L1, subsequent requests hit L1. Redis key TTL set to
  `StaleTtl` so expired entries evict automatically.
- **Key schema**: same as `WeatherCache.CacheKey` — `weather:{provider}:{lat:F2}:{lon:F2}:{metric|imperial}`.
- **When to add**: only if Phase 5's real-world hit rate shows restart-driven cold bursts
  materially degrading it. Not part of the initial migration — the single instance is the
  simpler fix and should be validated first.

## Migration plan

### Phase 1 — Code changes (done)

All items complete on branch `lpimentel/railway-migration` (commits `bf02e5f`, `ff4c0ac`).

1. [x] Rewrite `Program.cs` as a minimal API host; drop
   `Microsoft.Azure.Functions.Worker*`/`Microsoft.Azure.Functions.Worker.Sdk`/
   `Microsoft.Azure.Functions.Worker.ApplicationInsights`/
   `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`/
   `Microsoft.ApplicationInsights.WorkerService` package references from `TrmnlApi.csproj`;
   drop `AzureFunctionsVersion`/`OutputType Exe` properties; delete `host.json`.
   `local.settings.json` is also Functions-specific (likely gitignored) — noted stale; does not exist in repo.
2. [x] Convert `Functions/WeatherFunction.cs` → `Endpoints/WeatherEndpoint.cs`. Keep the exact same
   validation order, error responses (400/502/499), and JSON shaping logic — this is a
   transport-layer change only. Target is **schema/shape parity** for the success path and
   matched status codes for error paths. Byte-for-byte parity is impossible: `Meta.ServedAt`
   and `AgeSeconds` are per-request non-deterministic, and error-path `Content-Type` differs
   between Azure Functions `WriteStringAsync` and ASP.NET Core `Results.Text`.
   Note: `WeatherEndpoint` is a non-static class (not `static class` as originally planned)
   because `ILogger<WeatherEndpoint>` cannot use a static type as a type argument. All methods
   remain `static`; the class has no instance members.
3. [x] Delete `Functions/ScreenFunction.cs` references if any remain (already removed from the repo
   as of 2026-08-22; confirmed zero code references in this branch).
4. [x] Add a `Dockerfile` (multi-stage: `sdk:10.0` build → `aspnet:10.0` runtime). Railway injects
   `PORT` and expects the app to listen on it. Read it in `Program.cs`:
   `if (Environment.GetEnvironmentVariable("PORT") is { } p) builder.WebHost.UseUrls($"http://*:{p}");`
   — falls back to the .NET default (8080) if unset. No open question here; this resolves the
   port-binding item in "Open questions" below.
5. [x] Update `api/src/TrmnlApi/Properties/launchSettings.json` for local `dotnet run` (drop the
   Functions-specific profile).
6. [x] Decide what replaces Application Insights. Decision: (a) drop it, rely on Datadog.Trace only
   (it already auto-instruments ASP.NET Core, not just Functions); (b) add OpenTelemetry +
   Railway/Grafana/whatever if App Insights parity is wanted. Recommendation: (a), since
   Datadog is already the primary observability tool per `CLAUDE.md` conventions.

### Phase 2 — Local validation (done)

1. [x] `dotnet run` locally, hit `/api/v1/forecast` with real coordinates for both providers,
   diff the JSON response against the current prod Azure endpoint for the same request
   (same lat/lon/units/hours/days) to confirm **schema parity** — same field names, types,
   nesting, and array lengths. Ignore `Meta.ServedAt`/`AgeSeconds` (per-request non-deterministic).
   Also verify matched status codes for the 400/502/499 error paths.
   Result: schema parity PASS (identical keys, types, nesting, array lengths). Error path parity
   PASS (6 cases: missing coords, bad units, bad provider, bad coord range, bad hours, bad days;
   all 400 with identical body text). `fake=true` path: PASS (structure matches, randomized
   precipitation values differ as expected, last-day `high == low` transformation confirmed).
   502/499 not directly testable without simulating upstream failure or mid-request cancellation.
2. [x] `docker build` + `docker run` locally, repeat the same checks through the container.
   Result: Docker image built successfully; container started on port 8080; all endpoints
   (health, success, error paths) return identical results to local `dotnet run`.
3. [x] `dotnet test api/TrmnlApi.slnx` — expect no changes needed; all 13 test files under
   `api/tests/TrmnlApi.Tests/` target `Services`/`Providers`/`Mappings`/`Functions/RequestValidator`
   directly and have no Azure Functions Worker dependency.
   Result: 206 tests passed, 0 failed, 0 skipped.

### Phase 3 — Datadog APM (skipped)

Skipped per user decision. Ship without the Datadog Agent sidecar; the `Datadog.Trace` tracer
no-ops gracefully without a reachable agent. Add the Agent sidecar as a fast-follow once the
migration is stable if trace visibility is needed.

### Phase 4 — Railway setup (done)

1. [x] Create the Railway project, connect the GitHub repo, set the service root to `/api`.
   Done: project `trmnl-weather`, service `trmnl-plugins`, repo `lucaspimentel/trmnl-plugins`,
   branch `lpimentel/railway-migration`, `dockerfilePath: /api/Dockerfile`,
   `rootDirectory: /api`. Initial build failed because the Dockerfile uses paths relative to
   `api/` but Railway defaulted to the repo root as build context; fixed by setting
   `rootDirectory: /api`.
2. [x] Set environment variables to mirror current Azure App Settings (see Target architecture
   above). **`WeatherProviders` is required** — `ParseWeatherProviders` throws
   `InvalidOperationException` at startup if it's missing (set it to `open-meteo,pirate-weather`).
   Use `hh:mm:ss` form for `WeatherCache__FreshTtl`/`WeatherCache__StaleTtl` (see the TimeSpan
   pitfall in Target architecture above). Note: per the `TODO.md` P0 plan, don't set
   `OPEN_METEO_API_KEY` here at all if the free-tier reversion happens as part of this
   migration — `OpenMeteoClient` already falls back to the free host when it's unset.
   Set: `WeatherProviders=open-meteo,pirate-weather`, `PIRATE_WEATHER_API_KEY` (copied from
   Azure), `WeatherCache__FreshTtl=00:35:00`, `WeatherCache__StaleTtl=03:00:00`.
   `OPEN_METEO_API_KEY` intentionally not set (free-tier reversion).
3. [x] Pin the service to 1 replica explicitly (confirm Railway's default doesn't autoscale a
   simple web service by default — verify before relying on it).
   Confirmed: service config shows `numReplicas: 1` in `multiRegionConfig`. No autoscaling.
4. [x] Deploy to a Railway *staging* environment first (Railway supports environments per project).
   Deployed to staging: `https://trmnl-plugins-staging.up.railway.app`. All endpoints verified:
   health (200), success (200 with JSON weather data), error paths (400 with correct body text
   for missing coords, bad units, bad provider). 502 path observed transiently when Open-Meteo
   upstream was briefly unavailable — correct behavior.

### Phase 5 — Staging validation

1. Point a temporary/manual test against the Railway staging URL with the same request matrix
   as Phase 2, comparing against Azure staging (`trmnl-plugins-api-staging.azurewebsites.net`).
2. Let it soak for a few days of real device-like polling (or replay recent request patterns)
   and check the actual cache hit rate via logs/Datadog — this is the number that validates the
   whole migration's premise. Target: hit rate high enough that daily upstream calls comfortably
   clear 10,000/day with headroom for growth (see `TODO.md` P0 for the full target rationale).

### Phase 6 — Cutover

1. Update `polling_url` in `plugins/weather/src/settings.yml` to the new Railway URL (or a
   custom domain mapped to it — decide whether a custom domain is worth the setup for this
   internal-ish plugin backend, or whether Railway's `*.up.railway.app` default is fine).
2. Update the other four places the Azure URL is referenced: root `README.md`,
   `plugins/weather/README.md`, `plugins/weather/CLAUDE.md` (both the prod URL at line 24 and
   the staging URL at line 19), `plugins/weather/fields.txt`.
3. `trmnlp push --force` to redeploy the plugin with the new `polling_url`.
4. Update root `CLAUDE.md`'s "API Backend" section: replace the `func azure functionapp
   publish` deploy commands with the Railway deploy flow (likely just "push to the branch,
   Railway auto-deploys" — confirm once Railway is set up).
5. Once the free-tier reversion criteria in `TODO.md` P0 are met, drop `OPEN_METEO_API_KEY` and
   cancel the Open-Meteo paid subscription (can happen same day as cutover or after a short
   soak period — decide based on how confident Phase 5's numbers are).
6. Decide the branch-to-Railway-environment mapping (e.g. `main` → Railway prod, a staging
   branch → Railway staging). Confirm Railway auto-deploys on push to the connected branch.

### Phase 7 — Decommission Azure resources

1. After a stability window (recommend at least 1-2 weeks with no regressions), delete the
   `trmnl-plugins-api` and `trmnl-plugins-api-staging` Function Apps, their Application
   Insights resources, and their storage accounts.
2. Remove `dd-appsettings.production.json`/`dd-appsettings.staging.json` (Azure-specific) from
   the repo once no longer referenced by any deploy process.
3. Remove Azure-specific packages/config already dropped in Phase 1 if anything was left
   temporarily for a parallel-run period.

## Rollback plan

Keep the Azure Function Apps running and untouched through Phases 1-6. `polling_url` is the
single cutover switch — reverting it to the `azurewebsites.net` host and re-running `trmnlp
push --force` fully reverts traffic to Azure with no code rollback needed. Don't decommission
Azure (Phase 7) until Railway has proven stable in prod.

## Open questions (resolved before Phase 4)

- [x] ~~Datadog APM story on Railway (Phase 3)~~ — skipped; ship without traces, add Agent
      sidecar as fast-follow if needed.
- [ ] Custom domain vs. Railway's default domain for the cutover.
- [x] ~~Railway's port-binding convention~~ — resolved in Phase 1 step 4: Railway injects
      `PORT`, read it in `Program.cs` via `builder.WebHost.UseUrls`.
- [x] ~~Replica count~~ — confirmed 1 replica in service config (`numReplicas: 1`), no
      autoscaling. Railway sleep behavior for idle services still unverified; will surface
      during Phase 5 soak.
- [ ] Decide whether the free-tier reversion (`TODO.md` P0) happens as part of this migration or
      as a separate follow-up once Railway's real-world hit rate is confirmed.

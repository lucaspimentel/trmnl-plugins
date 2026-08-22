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

- The shared L2 cache (Azure Table Storage) TODO item — expected to become unnecessary once a
  single persistent instance removes the fragmentation. Revisit only if post-migration metrics
  show the hit rate still isn't high enough.
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
- Base URL is hardcoded in three places: `plugins/weather/src/settings.yml` (`polling_url`),
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
- `Functions/RequestValidator.cs` moves alongside it unchanged (it has no Functions dependency).
- New `GET /health` endpoint for Railway's health checks.
- Config via environment variables (Railway's equivalent of Azure App Settings) — same names,
  no renaming needed (`WeatherProviders`, `OPEN_METEO_API_KEY`, `PIRATE_WEATHER_API_KEY`,
  `WeatherCache__FreshTtl`/`WeatherCache__StaleTtl` using ASP.NET Core's `__` section separator).

## Migration plan

### Phase 1 — Code changes (this branch)

1. Rewrite `Program.cs` as a minimal API host; drop
   `Microsoft.Azure.Functions.Worker*`/`Microsoft.Azure.Functions.Worker.Sdk`/
   `Microsoft.Azure.Functions.Worker.ApplicationInsights`/
   `Microsoft.ApplicationInsights.WorkerService` package references from `TrmnlApi.csproj`;
   drop `AzureFunctionsVersion`/`OutputType Exe` properties; delete `host.json`.
2. Convert `Functions/WeatherFunction.cs` → `Endpoints/WeatherEndpoint.cs`. Keep the exact same
   validation order, error responses (400/502/499), and JSON shaping logic — this is a
   transport-layer change only, response bytes for a given request should be identical.
3. Delete `Functions/ScreenFunction.cs` references if any remain (already removed from the repo
   as of 2026-08-22; confirm nothing in this branch resurrects it).
4. Add a `Dockerfile` (multi-stage: `sdk:10.0` build → `aspnet:10.0` runtime), exposing port
   8080 (Railway's default `PORT` convention — read `PORT` env var in `Program.cs` via
   `builder.WebHost.UseUrls` or `ASPNETCORE_URLS` if Railway sets it for you; confirm which).
5. Update `api/src/TrmnlApi/Properties/launchSettings.json` for local `dotnet run` (drop the
   Functions-specific profile).
6. Decide what replaces Application Insights. Options: (a) drop it, rely on Datadog.Trace only
   (it already auto-instruments ASP.NET Core, not just Functions); (b) add OpenTelemetry +
   Railway/Grafana/whatever if App Insights parity is wanted. Recommendation: (a), since
   Datadog is already the primary observability tool per `CLAUDE.md` conventions.

### Phase 2 — Local validation

1. `dotnet run` locally, hit `/api/v1/forecast` with real coordinates for both providers,
   diff the JSON response against the current prod Azure endpoint for the same request
   (same lat/lon/units/hours/days) to confirm byte-for-byte parity in the response shape.
2. `docker build` + `docker run` locally, repeat the same checks through the container.
3. `dotnet test api/TrmnlApi.slnx` — expect no changes needed; all 13 test files under
   `api/tests/TrmnlApi.Tests/` target `Services/`/`Providers/`/`Mappings/`/`Functions/RequestValidator`
   directly and have no Azure Functions Worker dependency.

### Phase 3 — Datadog APM (open question, needs a decision before Phase 4)

The current Windows/App-Service-specific Datadog wiring (`dd-appsettings.*.json`) doesn't apply
to a Linux container. For Datadog APM on a containerized .NET app you typically need either:
- A Datadog Agent container reachable via `DD_AGENT_HOST` — on Railway this would mean running
  the Agent as a second service in the same project and using Railway's private networking
  (each service gets an internal hostname) to point `DD_AGENT_HOST` at it. Adds a second
  container to operate and pay for.
- Or submit traces directly via the Datadog Agentless/OTLP-to-Datadog-intake path, if
  `Datadog.Trace`'s current version supports it without an Agent.

This needs a decision before deploying to Railway for real — otherwise APM visibility regresses
silently. Low-effort fallback: ship without Datadog APM initially and rely on Railway's built-in
logs/metrics, then add tracing back as a fast-follow.

### Phase 4 — Railway setup

1. Create the Railway project, connect the GitHub repo, set the service root to `api/` (or
   point it at the Dockerfile directly — check whether Railway needs a root-relative Dockerfile
   path or a `railway.toml` build config).
2. Set environment variables to mirror current Azure App Settings (see Target architecture
   above). Note: per the `TODO.md` P0 plan, don't set `OPEN_METEO_API_KEY` here at all if the
   free-tier reversion happens as part of this migration — `OpenMeteoClient` already falls back
   to the free host when it's unset.
3. Pin the service to 1 replica explicitly (confirm Railway's default doesn't autoscale a
   simple web service by default — verify before relying on it).
4. Deploy to a Railway *staging* environment first (Railway supports environments per project).

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
2. Update the other three places the Azure URL is referenced: root `README.md`,
   `plugins/weather/README.md`, `plugins/weather/CLAUDE.md`, `plugins/weather/fields.txt`.
3. `trmnlp push --force` to redeploy the plugin with the new `polling_url`.
4. Update root `CLAUDE.md`'s "API Backend" section: replace the `func azure functionapp
   publish` deploy commands with the Railway deploy flow (likely just "push to the branch,
   Railway auto-deploys" — confirm once Railway is set up).
5. Once the free-tier reversion criteria in `TODO.md` P0 are met, drop `OPEN_METEO_API_KEY` and
   cancel the Open-Meteo paid subscription (can happen same day as cutover or after a short
   soak period — decide based on how confident Phase 5's numbers are).

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

## Open questions to resolve before Phase 4

- [ ] Datadog APM story on Railway (Phase 3) — Agent sidecar vs. dropping APM vs. another path.
- [ ] Custom domain vs. Railway's default domain for the cutover.
- [ ] Confirm Railway's port-binding convention (`PORT` env var vs. `ASPNETCORE_URLS`) before
      writing the Dockerfile's `ENTRYPOINT`/health check.
- [ ] Confirm whether a Railway "service" defaults to 1 replica or needs explicit pinning.
- [ ] Decide whether the free-tier reversion (`TODO.md` P0) happens as part of this migration or
      as a separate follow-up once Railway's real-world hit rate is confirmed.

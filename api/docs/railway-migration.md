# Migrating the Weather API from Azure Functions to Railway

Status (2026-08-23): Phases 1-4 done, Phase 5 validating on prod, Phase 6 step 0 done.
Staging plugin `316595` is pointed at the production Railway URL
(`trmnl-plugins-prod.lucasp.net`) as the test device; prod plugin `249564` still points at
Azure. The dedicated staging soak was skipped in favor of validating directly on prod with
the staging plugin as the canary, with `polling_url` as the rollback switch.

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
4. [x] Add a `Dockerfile` (multi-stage: `sdk:10.0` build → `aspnet:10.0` runtime).
   Correction (2026-08-22): this step originally had `Program.cs` read a `PORT` env var, on the
   assumption that Railway injects one. It does not — `PORT` is absent from the service's
   variables, so that branch never ran. The custom reading was removed; the app now uses the
   standard ASP.NET Core mechanism, with `ENV ASPNETCORE_HTTP_PORTS=8080` in the Dockerfile
   making the listening port explicit and matching `EXPOSE 8080` and the custom domain's
   `targetPort: 8080`. Override with `ASPNETCORE_HTTP_PORTS` or `ASPNETCORE_URLS` if a host
   ever needs a different port.
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
   Correction (2026-08-22): `OPEN_METEO_API_KEY` was initially left unset for the free-tier
   reversion, and every forecast request 502'd — Open-Meteo returned 429 "Daily API request
   limit exceeded" within minutes of deploy, before any real traffic existed. The free tier
   is rate-limited **per source IP**, and Railway egresses through a shared NAT address whose
   daily quota other tenants had already consumed. The key was added and the provider
   recovered immediately. This undercuts the free-tier reversion premise (see Phase 5 step 4
   and the open question below): on a shared-egress host the free tier may be unusable
   regardless of how good the cache hit rate gets.
   Also note: a variable change auto-triggers a redeploy, during which the old container keeps
   serving for roughly 30-60s. Verify against the new deployment, not immediately after saving.
3. [x] Pin the service to 1 replica explicitly (confirm Railway's default doesn't autoscale a
   simple web service by default — verify before relying on it).
   Confirmed: service config shows `numReplicas: 1` in `multiRegionConfig`. No autoscaling.
4. [x] Deploy to a Railway *staging* environment first (Railway supports environments per project).
   Deployed to staging: `https://trmnl-plugins-staging.up.railway.app`. All endpoints verified:
   health (200), success (200 with JSON weather data), error paths (400 with correct body text
   for missing coords, bad units, bad provider).
   Correction (2026-08-22): the "502 observed transiently" note was wrong — that 502 was the
   Open-Meteo quota failure described in step 2, which persisted until the API key was set,
   not a transient upstream blip.
5. [x] Branch and domain wiring (2026-08-22).
   - Staging deploys from the `staging` branch (branched off `lpimentel/railway-migration`,
     which has since been deleted). `.github/workflows/tests.yml` runs on pushes to that
     branch so CI covers what gets deployed.
   - Custom domains: `trmnl-plugins-staging.lucasp.net` (staging),
     `trmnl-plugins-prod.lucasp.net` (production, bound but not yet serving).
   - `healthcheckPath: /health` set on staging; Railway now gates a deploy on the app
     reporting ready rather than on the container merely starting.
   - Caveat: `railway service source connect` silently resets the trigger's `checkSuites`
     flag to false. Re-enable it via the GraphQL `deploymentTriggerUpdate` mutation (the MCP
     `update-service` tool does not cover source settings) after any source reconnect.
   - Not yet verified: whether `checkSuites` actually gates the build. On the 2026-08-22
     push, Railway created the deployment one second *before* CI started and finished after
     it, consistent with building concurrently. Settle it with a deliberately failing test
     if the gate matters.

### Phase 5 — Staging validation

Scope decision (2026-08-22, revised 2026-08-23): the soak was originally scoped to the
**staging plugin alone** (plugin `316595`, one device polling every 30 min, ~48 req/day
against a single cache key, a hit rate arithmetically guaranteed to be high and not
projectable to prod). That made Phase 5 a *stability and correctness* soak plus an
*analytical* projection (step 4). **Revised:** the dedicated staging soak was skipped in
favor of validating directly on **prod** Railway with the staging plugin as the canary
device, the real prod config giving a more honest signal than a per-key staging soak ever
could. `polling_url` remains the rollback switch.

1. [x] Instrument the cache outcome — nothing recorded it, so the hit rate this phase exists to
   measure was unmeasurable. Application Insights was dropped in Phase 1 and Datadog skipped in
   Phase 3, and `meta.cache` was returned to the client but never logged.
   Added: a per-request log line (cache status, winning/requested provider, `F1`-rounded
   coordinates) and `GET /metrics` returning process-lifetime counters — served count, the
   `fresh_fetch`/`fresh_hit`/`stale_served` split, derived hit rate, upstream failures, and
   per-provider counts. Uptime in the snapshot resets on restart, which is how restart-driven
   cache loss is distinguished from a genuinely low hit rate.
   **The counters are per-process and reset on every deploy.** Avoid deploying to staging during
   the soak, or snapshot `/metrics` before each deploy.
2. [ ] Point a temporary/manual test against the Railway staging URL with the same request matrix
   as Phase 2, comparing against Azure staging (`trmnl-plugins-api-staging.azurewebsites.net`).
   Partially covered: success path and the 400 error paths were verified against deployed
   staging, and Phase 2 proved schema parity locally and in-container. The full side-by-side
   matrix has not been run. The fallback path is untestable until Pirate Weather has its own
   key — the current key is shared with Azure prod/staging and is returning 429, so a request
   for `pirate-weather` silently falls back to open-meteo.
3. [x] ~~Point staging plugin `316595` at `trmnl-plugins-staging.lucasp.net` and soak for
   several days.~~ Pivoted (2026-08-23): the dedicated staging soak was skipped in favor of
   validating directly on **prod** Railway (`trmnl-plugins-prod.lucasp.net`) with the staging
   plugin as the canary device. The staging soak's per-key hit rate was never going to project
   to prod anyway (its own scope decision, above), and validating on the real prod endpoint
   with the real prod config gives a more honest signal. `/metrics` is watched for restart
   count and cause (uptime resets, the number that decides the L2 cache contingency), upstream
   failures, and hit rate. The `watchPatterns` were also narrowed to exclude `/api/docs/` so
   doc-only commits no longer trigger redeploys and reset the `/metrics` counters.
4. [ ] Analytical projection, retained as a fallback if prod `/metrics` data is insufficient.
   Expected prod steady state: ~14,400 req/day ÷ 48 polls/device/day ≈ **~300 distinct cache
   keys** (fewer where devices share coordinates/units/provider). With `FreshTtl=00:35:00`
   against a 30-min `refresh_interval`, an entry is fresh for exactly one subsequent poll and
   stale on the next, so each key refetches roughly hourly — ~24/day/key, **~7,200 upstream
   calls/day even with a perfect single-instance cache**. That clears 10,000/day but with
   only ~28% headroom against traffic P0 describes as growing. Raising `FreshTtl` past the
   poll-interval beat (e.g. `01:05:00` → a fetch every ~90 min → ~4,800/day) is the cheap
   lever.
   Deliverable: a recommendation on `FreshTtl`. The free-tier reversion is already resolved
   (see open questions) as not viable on Railway — no dedicated egress IP is available, and
   Railway's Static Outbound IPs are explicitly not guaranteed dedicated. The `FreshTtl` bump
   is therefore the only remaining cache-side lever for reducing upstream call volume. With
   the pivot to prod validation (step 3), the real `/metrics` hit rate now informs this
   recommendation alongside the arithmetic projection, not the projection alone.

### Phase 6 — Cutover

0. [x] **Configure the production environment first.** Done (2026-08-23): production deploys
   from `main` at `63bc139`, `RUNNING`, custom domain `trmnl-plugins-prod.lucasp.net` serving
   200 on `/health`, `/metrics`, and `/api/v1/forecast` (real weather JSON returned, `Server:
   railway-hikari`, edge `mia1`). Prod is up but not yet receiving device traffic, the
   `polling_url` cutover (step 1) is the switch that throws real load at it.
1. Update `polling_url` in `plugins/weather/src/settings.yml` to `trmnl-plugins-prod.lucasp.net`.
2. Update the other four places the Azure URL is referenced: root `README.md`,
   `plugins/weather/README.md`, `plugins/weather/CLAUDE.md` (both the prod URL at line 24 and
   the staging URL at line 19), `plugins/weather/fields.txt`. Also
   `.claude/settings.local.json`, which allowlists the Azure host for `WebFetch`.
3. `trmnlp push --force` to redeploy the plugin with the new `polling_url`.
4. Update root `CLAUDE.md`'s "API Backend" section: replace the `func azure functionapp
   publish` deploy commands with the Railway deploy flow (likely just "push to the branch,
   Railway auto-deploys" — confirm once Railway is set up).
5. **Keep the paid Open-Meteo customer-API key in production** — do not drop `OPEN_METEO_API_KEY`
   or cancel the paid subscription. The free-tier reversion is resolved as not viable on Railway
   (no dedicated egress IP; see open questions and Phase 5 step 4). The paid key stays as a
   permanent part of the prod config, not a temporary one.
6. [x] Branch-to-environment mapping: staging deploys from `staging` (done, Phase 4 step 5);
   production deploys from `main` (done, 2026-08-23). `main` was fast-forwarded to `staging`
   (commit `63bc139`) and pushed, satisfying the "PR into `main` before cutover" step via
   direct FF (solo repo, CI runs on push regardless). Auto-deploy on push confirmed, subject
   to the service's `watchPatterns: ["/api/**"]`, a commit touching only plugins or CI is
   skipped. `origin/staging` intentionally left 1 commit behind `main` to avoid redeploying
   staging and resetting the Phase 5 `/metrics` counters; push it when the soak ends.

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
- [x] ~~Custom domain vs. Railway's default domain for the cutover~~ — custom domains:
      `trmnl-plugins-staging.lucasp.net` and `trmnl-plugins-prod.lucasp.net`.
- [x] ~~Railway's port-binding convention~~ — Railway does not inject `PORT`; the app pins 8080
      via `ASPNETCORE_HTTP_PORTS` in the Dockerfile. See the correction in Phase 1 step 4.
- [x] ~~Replica count~~ — confirmed 1 replica in service config (`numReplicas: 1`), no
      autoscaling. Railway sleep behavior for idle services still unverified; will surface
      during Phase 5 soak.
- [x] ~~Decide whether the free-tier reversion (`TODO.md` P0) happens as part of this
      migration or as a separate follow-up once Railway's real-world hit rate is confirmed.~~
      Resolved (2026-08-22): **the free-tier reversion is out of scope for this migration and
      not worth pursuing on Railway at all.** Railway offers no dedicated egress IP. Its
      "Static Outbound IPs" feature (Pro plan, $20/mo) assigns 3 stable IPv4 addresses per
      service but the docs state verbatim that they "may be shared with other customers" —
      the same root cause that 429'd staging in Phase 4 step 2, so it does not fix the per-IP
      quota exhaustion. A true dedicated IP requires egressing through a self-hosted forward
      proxy on a cheap VPS or Fly.io (~$3-5/mo), which adds operational complexity for a tier
      that Phase 5 step 4 projects only ~28% headroom against at current traffic anyway.
      Keep the paid Open-Meteo customer-API key and drop `OPEN_METEO_API_KEY` reversion from
      the migration scope; the cheaper, host-independent lever is raising `FreshTtl` (Phase 5
      step 4).
- [ ] Pirate Weather needs its own API key. The current key is shared across Azure prod, Azure
      staging, and Railway, and is returning 429, so the fallback provider is unavailable and
      the fallback path is untestable.
- [ ] Naming: project `trmnl-weather` contains service `trmnl-plugins` — the narrower name wraps
      the broader one. Suggest project `trmnl-plugins`, service `weather-api`.

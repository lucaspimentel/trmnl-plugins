# Observability

The API ships APM traces and a small, deliberately chosen set of log events to Datadog. Traces go
through the Agent; logs go straight to the intake, sent by the same tracer. See [Logs](#logs) for why
the two differ.

Traces are collected by a Datadog Agent running as its own
Railway service in the same project and environment, reached over Railway's private network.

## How the tracer gets into the image

`api/Dockerfile` downloads a pinned `datadog-dotnet-apm-<version>.tar.gz` from the
[dd-trace-dotnet releases](https://github.com/DataDog/dd-trace-dotnet/releases) in the build stage
and copies the extracted tracer home to `/opt/datadog` in the runtime stage, then sets the
`CORECLR_*` profiler variables so the CLR loads it at startup.

The `Datadog.Trace` NuGet package alone does **not** do this: it is the manual instrumentation API
only. Without the native tracer there would be a single `weather.forecast` span per trace and no
inbound or outbound HTTP spans.

**The version lives in two places and they must be bumped together:**

| Place | What it controls |
|---|---|
| `DD_TRACER_VERSION` in `api/Dockerfile` | Automatic instrumentation (the native tracer) |
| `Datadog.Trace` in `api/src/TrmnlApi/TrmnlApi.csproj` | Manual instrumentation API |

The manual package version must be less than or equal to the tracer version. Keeping them equal
avoids the drift where a newer manual API call is not understood by an older loader.

## Agent service

Deploy a second Railway service from the public image `gcr.io/datadoghq/agent:7`. It is
private-network only: do **not** generate a public domain for it.

| Variable | Value | Why |
|---|---|---|
| `DD_API_KEY` | *(from 1Password)* | Required |
| `DD_SITE` | `datadoghq.com` | US1 |
| `DD_APM_ENABLED` | `true` | Enable the trace receiver |
| `DD_APM_NON_LOCAL_TRAFFIC` | `true` | Bind beyond localhost so the app service can reach port 8126 |
| `DD_APM_IGNORE_RESOURCES` | `GET /health` | The platform healthcheck polls `/health` constantly and it does produce a span; keep it out of ingestion |
| `DD_HOSTNAME` | `trmnl-api-agent-<environment>` | No host metadata is available, so set it explicitly rather than letting hostname resolution fail |
| `DD_DOGSTATSD_NON_LOCAL_TRAFFIC` | `true` | The tracer reports `runtime_metrics_enabled: true` by default and sends them over DogStatsD (8125). Without this they are silently dropped; set `DD_RUNTIME_METRICS_ENABLED=false` on the app instead if you would rather not collect them. |

The agent does not listen on the injected `PORT`, so the platform healthcheck never passes and the
deploy hangs in `DEPLOYING`. Because the restart policy is `NEVER`, the previous container keeps
serving in the meantime, which makes a variable change look like it had no effect. Point the
healthcheck at the trace receiver instead:

| Setting | Value |
|---|---|
| `PORT` | `8126` |
| Healthcheck path | `/info` (the trace agent's info endpoint) |

Private networking is scoped per environment, so `staging` and `production` each need their own
agent service. Staging cannot share the production agent.

Set `DD_API_KEY` directly in each environment. It is a sealed variable, so its value cannot be
read back, which also means it cannot be copied by syncing the service from another environment:
a sync produces a variable that is present by name but empty, and the container then fails init
with `01-check-apikey.sh: exited 1` while every listing still shows `DD_API_KEY` as set.

## App service variables

| Variable | Value |
|---|---|
| `DD_AGENT_HOST` | `datadog-agent.railway.internal` (the agent service's internal hostname) |
| `DD_SERVICE` | `trmnl-api` |
| `DD_ENV` | `staging` or `production` |
| `DD_VERSION` | not a variable; set by the start command, see below |

`DD_TRACE_AGENT_PORT` stays at its default `8126`. `DD_LOGS_INJECTION` already defaults to `true`.

### DD_VERSION and the commit SHA

`DD_VERSION` cannot be set as a variable here. The obvious spelling,
`DD_VERSION=${{RAILWAY_GIT_COMMIT_SHA}}`, renders empty: the git variables are injected into the
container at runtime but are not resolvable as dashboard variable references. `RAILWAY_DEPLOYMENT_ID`
behaves the same way, so this is a property of the reference mechanism rather than of the git
variables specifically.

Do not leave such a variable in place. Three deploys failed with completely empty deploy logs while a
`DD_VERSION` variable holding an unresolvable reference existed, and the start command below then
succeeded unchanged once that variable was deleted. The link was never proven, so treat it as a lead
rather than a rule: if a deploy fails with no container output at all, an unresolvable variable
reference is worth ruling out early.

Set it through the service's **start command** instead, which runs early enough for the tracer:

```sh
/bin/sh -c "export DD_VERSION=$RAILWAY_GIT_COMMIT_SHA; exec dotnet TrmnlApi.dll"
```

The `/bin/sh -c` wrapper is required, not decoration. For a service built from a Dockerfile the start
command replaces the image's `ENTRYPOINT` in **exec form**: no shell, so no variable expansion and no
inline `VAR=value` prefix. `exec` is equally required, or the shell stays PID 1 and swallows SIGTERM,
costing a graceful shutdown on every redeploy.

This keeps the host-specific variable name in the host's own configuration rather than in the image.

**The cost of this approach:** a start command overrides the Dockerfile's `ENTRYPOINT`, so
`dotnet TrmnlApi.dll` is now written down in two places, and the override is per environment. If the
entrypoint in `api/Dockerfile` ever changes, both start commands have to change with it or they will
silently keep launching the old one.

To turn tracing off in an environment without changing the image, set `DD_TRACE_ENABLED=false`.

## Verifying the span tree locally, without an agent

The tracer logs every span it closes when `DD_TRACE_DEBUG` is on, so the whole trace shape can be
checked from a laptop before any agent exists. The tracer will log connection-refused warnings the
whole time; that is expected and does not affect responses.

```bash
docker build -f api/Dockerfile -t trmnl-api:ddapm api/
docker run -d --name trmnl-apm-test -p 18080:8080 \
  -e WeatherProviders=open-meteo -e DD_SERVICE=trmnl-api -e DD_ENV=local -e DD_TRACE_DEBUG=true \
  trmnl-api:ddapm
curl "http://localhost:18080/api/v1/forecast?latitude=42.36&longitude=-71.06&hours=6&days=3"
docker exec trmnl-apm-test sh -c \
  "grep 'Span closed' /var/log/datadog/dotnet/dotnet-tracer-managed-dotnet-1.log"
```

Each line prints `s_id`, `p_id`, and `t_id`. Note that `s_id` is hex and `p_id` is decimal, so
convert before comparing. The expected tree for one forecast request, all sharing a `t_id`:

```
aspnet_core.request   GET /api/v1/forecast        (p_id: null)
  weather.forecast    open-meteo                  (p_id = the aspnet_core.request s_id)
    http.request      GET api.open-meteo.com/v1/forecast
```

## Span tags on `weather.forecast`

Set in `api/src/TrmnlApi/Services/WeatherForecastOrchestrator.cs`. All are string tags, including
the numeric-looking ones, so they stay facets rather than measures.

| Tag | Meaning |
|---|---|
| `weather.latitude`, `weather.longitude` | the request coordinates as separate `F1` tags, rounded to ~11 km |
| `weather.units` | `metric` or `imperial` |
| `weather.hours`, `weather.days` | requested forecast limits |
| `weather.requested_provider` | provider asked for (or the configured default) |
| `weather.winning_provider` | provider that actually served |
| `weather.cache_status` | `fresh_fetch`, `fresh_hit`, `stale_served`, or `all_failed` |
| `weather.fallback` | `true` when the winning provider is not the requested one |
| `weather.age_seconds` | age of the served data |
| `weather.first_failure.status`, `weather.first_failure.error` | set only when a provider failed |

Coordinates are rounded to `F1` before tagging, the same rule the logs follow.

## Verifying in Datadog

After deploying, hit `/api/v1/forecast` a few times and confirm for `service:trmnl-api`:

- the same three-span tree as above
- `weather.forecast` carrying the tags listed above, in particular `weather.cache_status`,
  `weather.winning_provider`, and `weather.fallback`
- no spans for `GET /health`

The cache-status distribution should agree with the counters `GET /metrics` already exposes.

## Logs

Logs do **not** go through the Agent. Container log collection needs the Docker socket or a shared
filesystem, and an Agent running as its own service has neither, so there is nothing for it to tail.
Instead the native tracer's **direct log submission** posts to the Datadog log intake itself. This is
the same tracer already installed for APM, so it costs no NuGet package and no application code.

It is switched on per environment with `DD_LOGS_DIRECT_SUBMISSION_INTEGRATIONS=ILogger`, set on the
app service alongside `DD_API_KEY`. Neither is baked into the image, so an environment without them
ships no logs, and local runs and `dotnet test` need no secret.

### What gets sent

Direct submission registers a logging provider aliased `Datadog`, so which events reach it is
ordinary `Microsoft.Extensions.Logging` filtering, configured in `api/src/TrmnlApi/appsettings.json`
under `Logging:Datadog:LogLevel`. `Default` is `None`, so nothing ships unless it is named:

| Category | Min level | Events |
|---|---|---|
| `TrmnlApi.Observability.ForecastServed` | Information | one line per served forecast |
| `TrmnlApi.Endpoints.WeatherEndpoint` | Warning | every provider failed, caller got a 502 |
| `TrmnlApi.Services.WeatherForecastOrchestrator` | Warning | a provider failed; stale cache served instead |
| `TrmnlApi.Services.WeatherResilience` | Warning | a provider's circuit opened or closed |
| `TrmnlApi.Observability.UnhandledExceptionLogger` | Error | an exception no endpoint handled |

Console output is unaffected by any of this, so stdout and the platform's own log view keep showing
everything exactly as before.

Widening the list is one line in that file. `DatadogLogAllowlistTests` runs the real
`appsettings.json` against a provider aliased the same way, asserting both that each category above
ships and that `Microsoft.*`, `System.*` and `Polly` never do at any level.

Two exclusions are deliberate: the client-cancelled (499) log shares `WeatherEndpoint`'s category and
is filtered out by level alone, and routine per-request framework logs are never wanted.

`ForecastServed` is a marker type that exists only to give the served-forecast log its own category,
separable from the 499 log beside it. `UnhandledExceptionLogger` similarly owns the log site for
unhandled exceptions: left to the framework they surface under a Kestrel category that has moved
between releases and also carries unrelated connection-level errors, which is a poor thing to pin an
allowlist to.

`DD_LOGS_DIRECT_SUBMISSION_MINIMUM_LEVEL` is a second, coarser gate applied before these rules. It
defaults to `Information`, which is at or below everything in the table, so it is left alone. Lower
it only if a `Debug` category is ever added above.

### Variables

| Variable | Value |
|---|---|
| `DD_API_KEY` | *(from 1Password)* - required; without it direct submission stays off |
| `DD_SITE` | optional, defaults to `datadoghq.com` |
| `DD_LOGS_DIRECT_SUBMISSION_INTEGRATIONS` | `ILogger` - required; this is what turns direct submission on |

`DD_SERVICE`, `DD_ENV` and `DD_VERSION` are already set for APM and tag the logs too. As with the
Agent's copy of `DD_API_KEY`, the variable is sealed per environment and cannot be copied by syncing
a service from another environment.

Unlike Agent-collected logs, direct submission does not get the Agent's sensitive-data scrubbing.
Nothing in the allowlist logs a full coordinate (they are rounded to `F1`) or a query string, so this
is a constraint to respect when adding events rather than a current problem.

### Verifying log shipping

After deploying, hit `/api/v1/forecast` a few times and confirm in the Logs Explorer for
`service:trmnl-api`:

- one `Served forecast for ...` line per request, tagged with the right `env` and `version`
- no `Microsoft.*`, `System.*`, or `Polly` lines at all
- `dd.trace_id` present, and matching the `aspnet_core.request` span for the same request

Correlation should be automatic here: the same tracer produces both the span and the log record. If
logs do not arrive at all, check the tracer's own log (see below) for a direct-submission startup
line, since a rejected API key does not surface anywhere in the application's output.

## Debugging a failed attach

Set `DD_TRACE_DEBUG=true` on the app service and read the profiler's own logs inside the
container, under the directory created by `/opt/datadog/createLogPath.sh`
(`/var/log/datadog/dotnet`). An empty directory means the profiler never loaded: re-check
`CORECLR_PROFILER_PATH`.

If the tracer starts but no traces arrive, the likely cause is agent connectivity. Set
`DD_TRACE_AGENT_URL=http://datadog-agent.railway.internal:8126` explicitly and check whether the
agent bound IPv4-only while private DNS resolved to IPv6.

# Observability

The API ships APM traces to Datadog. Traces are collected by a Datadog Agent running as its own
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

Private networking is scoped per environment, so `staging` and `production` each need their own
agent service. Staging cannot share the production agent.

## App service variables

| Variable | Value |
|---|---|
| `DD_AGENT_HOST` | `datadog-agent.railway.internal` (the agent service's internal hostname) |
| `DD_SERVICE` | `trmnl-api` |
| `DD_ENV` | `staging` or `production` |
| `DD_VERSION` | `${{RAILWAY_GIT_COMMIT_SHA}}` |

`DD_TRACE_AGENT_PORT` stays at its default `8126`. `DD_LOGS_INJECTION` already defaults to `true`.

`DD_VERSION` is set through a dashboard variable reference rather than in the Dockerfile so the
repo stays free of host-specific variable names.

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

## Verifying in Datadog

After deploying, hit `/api/v1/forecast` a few times and confirm for `service:trmnl-api`:

- the same three-span tree as above
- `weather.forecast` carrying `weather.cache_status`, `weather.winning_provider`,
  `weather.fallback`, and `weather.coord`
- no spans for `GET /health`

The cache-status distribution should agree with the counters `GET /metrics` already exposes.

## Debugging a failed attach

Set `DD_TRACE_DEBUG=true` on the app service and read the profiler's own logs inside the
container, under the directory created by `/opt/datadog/createLogPath.sh`
(`/var/log/datadog/dotnet`). An empty directory means the profiler never loaded: re-check
`CORECLR_PROFILER_PATH`.

If the tracer starts but no traces arrive, the likely cause is agent connectivity. Set
`DD_TRACE_AGENT_URL=http://datadog-agent.railway.internal:8126` explicitly and check whether the
agent bound IPv4-only while private DNS resolved to IPv6.

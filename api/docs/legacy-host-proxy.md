# The legacy host proxy

The first deployment of this API lived on a different host. That host is still running, still
serving `/api/v1/forecast`, and **still has real users**. This document says why it cannot simply be
deleted, and what replaced its implementation.

The code is at [`legacy-proxy/`](../../legacy-proxy/), deliberately outside `api/` so that editing
it does not match the deployment watch patterns and trigger a rebuild of the main service.

```
legacy-proxy/
  src/TrmnlLegacyProxy/      the function app: Program.cs, ForecastProxy.cs, host.json
  tests/TrmnlLegacyProxy.Tests/
```

```bash
dotnet build legacy-proxy/src/TrmnlLegacyProxy/TrmnlLegacyProxy.csproj
dotnet test  legacy-proxy/tests/TrmnlLegacyProxy.Tests/TrmnlLegacyProxy.Tests.csproj
```

It is **not** in `api/TrmnlApi.slnx`, so `dotnet test api/TrmnlApi.slnx` does not cover it. CI runs
it separately from `.github/workflows/legacy-proxy.yml`.

## Why the old host still matters

It was kept as a rollback target after the hosting migration and then left running. The assumption
was that it was a husk. It is not: measured 2026-08-30, it served **5,893 requests in 24 hours**,
and its own telemetry showed live upstream provider calls with a warm cache of its own.

The traffic is real. Its successful requests arrive carrying Sentry tracing baggage with
`sentry-environment=production`, a single `sentry-public_key`, and four distinct `sentry-release`
values over one day. The plugin platform fetches `polling_url` **server-side**, so that is the
platform's own backend polling on behalf of installs. Those installs are forked copies of the
plugin, they carry their own `settings.yml` pointing at the old host, and **they cannot be updated
by us**. Deleting the app breaks them with no recourse.

This is the same reasoning that freezes `/api/v1/forecast` itself, in `CLAUDE.md`. The old host is
the other half of that promise.

### What the traffic actually is

| | With Sentry baggage | Without | Total |
|---|---|---|---|
| 200 | 1,419 | 2,937 | 4,356 |
| 400 | 0 | 1,439 | 1,439 |
| 499 | 57 | 38 | 95 |

Two things in that table are worth carrying forward.

**A quarter of it is junk.** The 400s arrive at *exactly* 60 per hour, one per minute, every hour,
with no tracing baggage. Ruled out as sources: there are no availability tests configured on the
telemetry resource, and the app has no `healthCheckPath` (it is Consumption with `alwaysOn: false`).
So it is an external once-a-minute caller sending invalid parameters, most likely a forgotten uptime
monitor. **Find and stop it.** It is 25% of the load on a host being retired, and the proxy will
faithfully forward every one of those requests.

**The install count is not measurable where the traffic lands today.** Query strings are not logged
(`EnableQueryStringTracing: false`) and the client address is recorded as `0.0.0.0`. Early readings
of "1 distinct IP" and "1 distinct query string" were artifacts of that stripping rather than
findings, and would have supported exactly the wrong conclusion. Routing through the proxy is what
closes this gap, because the request then reaches a service whose telemetry does record it.

### What is not in scope

- **`/api/v1/screen`** exists on the deployed app but has **zero requests in seven days**, and was
  removed from this repository in `5054204`. It is not proxied. The function simply disappears.
- **The staging copy of the old host** has zero traffic. It needs no proxy; delete it.

## A proxy, not a redirect

The old hostname belongs to the platform it is deployed on and cannot be pointed elsewhere by DNS,
so *something* there has to keep answering regardless. That leaves two ways to hand the request on.

A `301`/`302` would bet every forked install on the plugin platform's fetcher following redirects,
which is unverified and unverifiable from here. If it does not follow them, every fork breaks at
once, silently, and their owners cannot fix it.

So the proxy forwards server-side and returns a normal `200`. Nothing is assumed about the client.

## Fidelity rules

The point of the exercise is that a caller cannot tell the difference. These rules exist because
each one has a specific way of going wrong:

- **The query string is forwarded byte-for-byte, unparsed.** Reconstructing it from parsed values
  risks dropping undocumented parameters - `fake=true` is the one that would be missed.
- **The body is relayed as bytes**, never deserialized and re-serialized. A round-trip through a
  JSON model can reorder keys or reformat floats. Both are invisible when comparing parsed objects
  and visible to a client that compares bytes.
- **The status code passes through unchanged**, including `400` and upstream failures.
- **No cache-control or content headers are invented.**

The two implementations were compared live before this was written, with identical parameters: same
top-level keys, same hourly and daily entry counts. The only difference is that the current host
adds `meta.time_format`, which the old one lacks. That is additive, and a caller reading named keys
will not notice.

## Headers

Forwarded when present: `traceparent`, `tracestate`, `baggage`, `sentry-trace`. Preserving these
keeps the caller's own tracing intact - and it was exactly this baggage that revealed who the caller
was, so it is worth not destroying.

Dropped: `Host`, and the hop-by-hop headers.

Added: `X-Legacy-Proxy: 1`, so the receiving service can tag proxied traffic and finally count what
is arriving through the old hostname.

## Resilience

A static `HttpClient` with a 15 second timeout, which sits **above** the backend's own 10 second
total request timeout so that the backend's resilience policy decides the outcome rather than the
proxy truncating it.

**No retries in the proxy.** The backend already retries and falls back between providers. Adding a
second retry layer multiplies upstream load at exactly the moment upstream is struggling.

## Two costs, stated plainly

**This trades redundancy for consistency.** Today the old host is a genuinely independent
deployment, with its own cache and its own provider credentials. If the current host goes down right
now, forks pointing at the old one keep working. After this change they do not: an accidental hot
standby becomes a single point of failure. That is accepted deliberately, because two silently
diverging implementations of a contract that can never be changed is the worse long-term risk - but
it is a real loss and not a free win.

**Proxying costs more per request, not less.** Consumption billing is per GB-second of wall-clock
time. Observed execution time serving from the old host's own cache is about 2.7 ms; a proxied
request is dominated by the network round-trip and will be one to two orders of magnitude longer.
At roughly 6,000 requests a day the absolute cost stays trivial, but the intuition that a thin proxy
is cheaper than doing the work is backwards here.

## Tests, and why they are the ones that exist

Every test in `ForecastProxyTests` checks one property: **a caller cannot tell it is being proxied.**
They were each confirmed to fail when the behaviour they describe is broken, rather than assumed to
be meaningful:

- Rebuilding the target from parsed query values instead of passing the raw string fails four cases,
  including the `fake=true` one. That is the mutation a reasonable person would actually make.
- Relaying the body through a JSON round-trip instead of copying the stream fails the byte-fidelity
  test. This is the case worth having: the round-tripped body is *equal as a parsed object* and
  different as bytes, so a test comparing parsed JSON would have passed it.

## Local runs do not work with the current tooling

`func start` reports that it cannot correctly load extensions for an isolated project and says to
use `dotnet run`. Both then fail the same way: the worker process exits and the host reports an
`Unavailable` gRPC error, `"the server did not complete the HTTP/2 handshake"`. Configuration is not
the cause - the worker gets past reading `FORECAST_ORIGIN` before it dies - so this reads as a
Core Tools 4.14.0 against .NET 10 worker problem rather than anything in this code.

**The proxy has therefore never been run end to end on a developer machine.** That is why the
rollout below starts on the copy with no users, and why the tests cover the fidelity rules directly
rather than relying on an integration run that cannot currently be performed.

## Rollout

1. **Deploy to the staging copy first.** It has zero traffic, which makes it a canary that costs
   nothing to get wrong. Point it at the staging origin.
2. **Diff the responses.** Same query strings against old and new: valid input, missing parameters,
   `fake=true`, out-of-range values. Compare status codes and body bytes, not parsed objects.
3. **Deploy to production, then watch the result-code mix.** It should stay 200/400/499 in the same
   proportions. A shift means the proxy is failing something the old implementation handled.
4. **Then, and only then**, the decommission item in `TODO.md` becomes reachable: what remains is a
   thin forwarder rather than a second implementation of a frozen contract.

## Configuration

| Setting | Meaning |
|---|---|
| `FORECAST_ORIGIN` | Scheme and host to forward to, no trailing slash. Required; the app fails fast without it |

The origin is configuration rather than source so that no deployment hostname is written into this
repository.

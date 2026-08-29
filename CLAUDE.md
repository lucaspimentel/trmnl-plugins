# CLAUDE.md

TRMNL e-ink display plugins. Plugins live under `plugins/`, shared API backend under `api/`.

For TRMNL docs: https://docs.trmnl.com/go/llms.txt (append `.md` to any `docs.trmnl.com/go/...` URL for leaner Markdown).

## Critical Gotchas

- `settings.yml` must be at `src/settings.yml` — trmnlp ignores one at the plugin root
- Plugin ID is stored in `src/settings.yml` under the `id:` key (not in `.trmnlp.yml`)
- `polling_url` interpolation: use `{{ keyname }}` (plain Liquid), not `##{{ keyname }}`
- **`select` option values are slugified, and Liquid filters in `polling_url` are not applied.** An option written `US - United States of America` is submitted as `us_-_united_states_of_america` (lowercased, non-alphanumerics to `_`), and a `| split | first` filter meant to trim it never runs. A strict parser rejected the result, so a user who had set the field was served as though they had not. Put the key first in the option text, send the raw `{{ keyname }}`, and parse the leading token server-side. A filter also puts `: ` in the YAML scalar, so the value would have to be quoted or the file will not parse
- Flex children that should shrink need `min-width: 0` — `plugins.js` measures widths before layout, so without it they expand to full container width
- Recipe linter counts raw substrings of `font-size`, `padding`, `margin`, etc. across ALL markup (including JS, comments, variable names) — max 6 total. See `.claude/skills/trmnl-dev/references/framework/updates.md` for workarounds.

## Deploy a Plugin

```bash
bash tools/push-plugin.sh plugins/<name>                  # lint + push to the staging plugin
bash tools/push-plugin.sh plugins/<name> --dry-run       # show the overrides, push nothing
bash tools/push-plugin.sh plugins/<name> --env prod      # lint + push to the prod plugin
```

Each plugin exists twice on TRMNL, as a prod and a staging plugin with different ids. The script
applies the staging overrides (id, `polling_url` host, ` (staging)` name suffix) to
`src/settings.yml`, pushes, then restores the file. It refuses to start if that file has
uncommitted changes, since the restore would discard them.

The underlying commands, if you need them directly:

```bash
cd plugins/<name>
trmnlp lint            # same check .github/workflows/plugins.yml runs on push/PR
trmnlp push --force    # --force skips confirmation prompt
```

Note `trmnlp push` rewrites the local `src/settings.yml` with the server's copy of the settings,
so revert it afterwards when running these by hand.

`plugins.yml` pins `trmnl_preview` to a specific gem version so a new lint rule upstream can't
turn the repo red on its own; bump the pin deliberately.

## Build Preview

```bash
bash tools/build-preview.sh plugins/<name>                                  # build all variants (og, x, x-portrait)
bash tools/build-preview.sh plugins/<name> --device x                       # TRMNL X only (landscape + portrait)
bash tools/build-preview.sh plugins/<name> --device x --orientation portrait # X portrait only
bash tools/build-preview.sh plugins/<name> --screenshot                     # + screenshot all variants × all layouts
bash tools/build-preview.sh plugins/<name> --screenshot --1bit              # + 1-bit B&W conversion
bash tools/build-preview.sh plugins/<name> --screenshot --device x --layout full  # screenshot X full only
```

Output goes to `_build/{og,x,x-portrait}/`. Each subdirectory gets the TRMNL wrapper:
- `https://trmnl.com/css/latest/plugins.css` + `https://trmnl.com/js/latest/plugins.js`
- Inter font (Google Fonts)
- OG: `<div class="screen screen--1bit screen--ogv2 screen--md screen--1x">`
- X: `<div class="screen screen--4bit screen--v2 screen--lg screen--1x">`
- X portrait: same + `screen--portrait`

### Quick look: `trmnlp build --png`

Built-in lightweight alternative — renders all four layouts to HTML + PNG in one command, no HTTP server or Playwright. Runs JS (Highcharts renders correctly).

```bash
cd plugins/<name>
trmnlp build --png --width 800 --height 480 --color-depth 1   # OG 1-bit quick check
trmnlp build --png --width 1040 --height 780 --color-depth 4  # 4-bit (16 grays)
```

Use it for fast OG sanity checks while iterating. It is **not** a replacement for `build-preview.sh`: the wrapper is a bare `<div class="screen">`, so it never applies `screen--lg`/`screen--4bit`/`screen--portrait` — the TRMNL X responsive layout and portrait do **not** render (`--width`/`--height` only resize the canvas, leaving the OG layout top-left with empty space). Reach for `build-preview.sh --screenshot` when you need a true X / portrait / in-slot preview.

## API Backend (`api/`)

.NET 10 ASP.NET Core minimal API (`TrmnlApi`) behind the Weather plugin, containerized and deployed to Railway as a single pinned replica (keep it at one replica so the in-memory cache stays warm; do not enable autoscaling). Solution: `api/TrmnlApi.slnx`.

```bash
dotnet build api/TrmnlApi.slnx
dotnet test api/TrmnlApi.slnx                     # also run by .github/workflows/tests.yml
dotnet run --project api/src/TrmnlApi             # local run (http://localhost:8080)
```

- Deploy: push to `main` (prod) or `staging`; Railway auto-builds from `api/Dockerfile` (service root `/api`) and deploys the single replica. Healthcheck: `GET /health`.
- The build only runs for commits matching the watch patterns `/api/**` and `!**/*.md`, so a **Markdown-only commit never deploys** and shows as `SKIPPED`. Pushing a docs change is not a way to pick up a changed environment variable; that needs a code change under `/api` or a manual redeploy.
- Routes: `GET /api/v1/forecast`, `GET /api/v2/forecast` (what the plugin uses), `GET /health`, `GET /metrics` (process-lifetime cache counters)
- **`/api/v1/forecast` is frozen: never change anything a caller can observe.** The plugin moved to v2, so v1 looks like dead code ripe for cleanup. It is not - the plugin is public and has been forked, and forked copies still poll v1 with their own `settings.yml`. They cannot be updated. Treat v1's routes, query parameters (including the undocumented `fake=true`), response bytes, and status codes as a public contract; refactors that move implementation behind it are fine. New debug or test affordances go on v2 only. v1 retires when its traffic stops, not before - see `api/docs/place-input.md`
- `WeatherProviders` env var is **required** (comma-separated, e.g. `open-meteo,pirate-weather`); the first entry is the default provider and the list defines the fallback order
- Provider keys: `OPEN_METEO_API_KEY`, `PIRATE_WEATHER_API_KEY`
- Cache TTL env vars use the `__` separator: `WeatherCache__FreshTtl`/`WeatherCache__StaleTtl` in `hh:mm:ss` form (a bare number parses as days, not minutes)
- **Providers must never trim their forecast to a caller's requested `hours`/`days`.** `WeatherCache` keys on `(provider, latitude, longitude, metric)` only, so a trimmed response becomes the ceiling for every later request at that location. Transform everything upstream returns; `ForecastTrimmer` applies per-request limits in `WeatherEndpoint`, after the cache.
- Test scenarios: `GET /api/v2/forecast?place=test:<name>` returns a canned result (each error code, plus `stale`, `precipitation`, `499`, `500`, `502`), so the plugin's error rendering can be checked on a real screen by editing its Place setting. Not environment-gated. Table in `api/docs/place-input.md`
- Geographic data is **bundled**, not fetched: `api/src/TrmnlApi.Geo` serves both forward geocoding (typed place to coordinates) and the on-screen location (coordinates to a label) from one SQLite file. Open-Meteo geocoding is the fallback for a local miss only, and `weather.geocoder` records which path served. `api/tools/GeoDataBuilder` builds the file from Natural Earth 10m admin-1, GeoNames `cities1000` and GeoNames postal; the Dockerfile fetches it by pinned `GEO_DATA_URL` + `GEO_DATA_SHA256`. With no dataset the service still boots and degrades to the vendor geocoder with no location shown. See `api/docs/geographic-telemetry.md`
- Round latitude/longitude to `F1` before logging or tagging a span (coordinates are PII)
- APM: traces ship to Datadog through an agent service on the private network; the native tracer is installed by `api/Dockerfile` and its version must stay in sync with the `Datadog.Trace` package. Setup and env vars: `api/docs/observability.md`
- Logs: sent by the native tracer's direct submission, not through the agent, which cannot tail another service's stdout. Turned on per environment with `DD_LOGS_DIRECT_SUBMISSION_INTEGRATIONS=ILogger` plus `DD_API_KEY` (deploy-time vars, not in the image). The allowlist is `Logging:Datadog:LogLevel` in `api/src/TrmnlApi/appsettings.json`, defaulting to `None`; console output is unaffected. Adding an event means adding its category there

## Credentials

`TRMNL_DEVICE_ID`, `TRMNL_DEVICE_API_KEY`, and `TRMNL_API_KEY` (for `trmnlp login`) are in **1Password item "trmnl"**:

```bash
op item get trmnl --fields label=TRMNL_DEVICE_ID,label=TRMNL_DEVICE_API_KEY --reveal
```

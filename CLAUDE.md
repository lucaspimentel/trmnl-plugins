# CLAUDE.md

TRMNL e-ink display plugins. Plugins live under `plugins/`, shared API backend under `api/`.

For TRMNL docs: https://docs.trmnl.com/go/llms.txt (append `.md` to any `docs.trmnl.com/go/...` URL for leaner Markdown).

## Critical Gotchas

- `settings.yml` must be at `src/settings.yml` — trmnlp ignores one at the plugin root
- Plugin ID is stored in `src/settings.yml` under the `id:` key (not in `.trmnlp.yml`)
- `polling_url` interpolation: use `{{ keyname }}` (plain Liquid), not `##{{ keyname }}`
- **`select` option values are slugified, and Liquid filters in `polling_url` are not applied.** An option written `US - United States of America` is submitted as `us_-_united_states_of_america` (lowercased, non-alphanumerics to `_`), and a `| split | first` filter meant to trim it never runs. A strict parser rejected the result, so a user who had set the field was served as though they had not. Put the key first in the option text, send the raw `{{ keyname }}`, and parse the leading token server-side. A filter also puts `: ` in the YAML scalar, so the value would have to be quoted or the file will not parse
- **`trmnl.user.*` DOES interpolate in `polling_url`**, unlike the two things above. `&tz={{ trmnl.user.time_zone_iana }}` arrives as `tz=America/New_York`, verified on a real device by forcing a refresh and reading the API log. So the user's time zone, locale and UTC offset are all available to the backend without a custom field. Custom fields still cannot be read from a *template* — that restriction is unchanged
- Flex children that should shrink need `min-width: 0` — `plugins.js` measures widths before layout, so without it they expand to full container width
- **Never put an inline `style` attribute on an element that carries an arbitrary-value class** (`w--[64cqw]`, `gap--[16px]`). The runtime applies those values itself and a static `style` attribute defeats it — silently, and only where the value mattered. Adding `style="min-height:0;"` next to `w--[64cqw]` collapsed the Weather plugin's OG `full` detail column to one character per line while every other layout looked fine. Put the rule in a `<style>` block keyed off a class instead
- A flex child with a fixed pixel height will not shrink below it: flex items default to `min-height: auto`, so the ancestors between the flex container and that child each need `min-height: 0`. This is the vertical twin of the `min-width: 0` rule above
- Recipe linter counts raw substrings of `font-size`, `padding`, `margin`, etc. across ALL markup (including JS, comments, variable names) — max 6 total. See `.claude/skills/trmnl-dev/references/framework/updates.md` for workarounds.

## Environment Setup

```bash
bash tools/setup-env.sh          # Ruby + trmnlp, .NET SDK, .env; skip steps with --skip-ruby/--skip-dotnet/--skip-node
```

Idempotent, safe to re-run. It writes `~/.trmnl-plugins-env.sh` (locale, `rbenv init`, `DOTNET_ROOT`) and
sources it from the **first line** of `~/.bashrc`, which covers interactive shells only. `bash -c`
— how Claude Code and CI run commands — reads no startup file at all, so the script also drops
wrappers for `ruby`, `gem`, `bundle`, `trmnlp` and `dotnet` into `~/.local/bin` (already on the
default PATH); each sources the env file and execs the real binary. They go there rather than
`/usr/local/bin` so the container's own system-Ruby links are left alone.

- `trmnl_preview` requires Ruby >= 3.4 (CI pins 4.0); a container shipping Ruby 3.3 needs an rbenv
  build, which takes several minutes.
- **`trmnlp` needs a UTF-8 locale.** Under `C`/`POSIX`, Ruby reads templates as US-ASCII and
  `trmnlp lint` dies with `invalid byte sequence in US-ASCII` on the first non-ASCII character in a
  template — an environment problem that looks exactly like a broken template.
- Environment variables are listed in `.env.example`; copy to `.env` (gitignored) and load with
  `set -a && source .env && set +a`. Secrets come from 1Password, not from a checked-in file.

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

## Framework Docs (skill)

```bash
bash tools/sync-framework-docs.sh --version 3.3     # regenerate + vendor into the trmnl-dev skill
bash tools/sync-framework-docs.sh --no-generate     # copy what the clone already generated
```

`.claude/skills/trmnl-dev/references/framework/3.3/` holds 57 pages generated by the framework's own
`rake framework:generate_markdown`, stamped in `SOURCE.md` with the commit they came from. Vendored
rather than fetched because the framework design system is the one TRMNL source with no working
LLM-ready endpoint: `trmnl.com/llms.txt` advertises `.md` twins that 404, and the pages are
gitignored ERB output so they are not on GitHub either. Re-sync when a plugin's `framework_version`
moves to a new docs track. Never edit the generated files; corrections go in `framework/updates.md`.

Needs a `usetrmnl/trmnl-framework` clone (`--clone`, or `$TRMNL_FRAMEWORK_DIR`). On Windows the
framework's `Gemfile` needs `gem "tzinfo-data", platforms: %i[windows jruby]` added locally, or the
rake task aborts with `ZoneinfoDirectoryNotFound` before writing anything — that line is missing
upstream.

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

### Fluid Mashup slots: `tools/build-mashup-preview.sh`

```bash
bash tools/build-mashup-preview.sh plugins/<name> --device x                       # default cells: 3x1, 1x1, 1x3
bash tools/build-mashup-preview.sh plugins/<name> --device x --cell 2x2 --cell 3x3 # pick cell sizes
bash tools/build-mashup-preview.sh plugins/<name> --screenshot --output _build/shots
```

Wraps a built view in a `mashup--3x3` cell of the requested size (columns x rows, 1-3 each) and
fills the rest of the grid with placeholders. This is the only way to see what a view does in a
slot no standalone layout has: the cell, not the view, owns the size, so `w--*`/`h--*` on the view
are ignored and every fixed pixel value in the plugin is a guess against a size that no longer
holds. It calls `build-preview.sh` first, so the screen classes stay defined in one place, and it
takes the same `--device`/`--orientation`/`--screenshot`/`--1bit`/`--output` flags. Which view goes
in a cell defaults to the shape core would pick (wide -> `half_horizontal`, tall ->
`half_vertical`, 1x1 -> `quadrant`, square -> `full`); override per cell with `--cell 2x2:quadrant`.

`--screenshot` starts its own server on port 8765 and stops it again, so there is nothing to set
up; it reuses one already listening there, which then has to be serving the same `_build/`.
`build-preview.sh` still expects you to have started one. `ruby -run -e httpd` will not do — the
bundled Ruby has no webrick — but `python -m http.server` is fine and has been threaded since 3.7.

**`playwright-cli` is the flaky part, not the server**, and it fails in two ways that both look like
success:

- `screenshot` writes no file every few calls and still exits 0. Check the file exists and retry;
  do not trust the exit status. Under `set -e` an unguarded call also ends a sweep partway.
- Navigating a reused browser with `goto` can silently not navigate, leaving the previous page up
  so the screenshot is of the wrong view. Both scripts open a fresh browser per shot for this
  reason. It cost a round of "regressions" that were stale pages, so do not optimise it away.

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

## Local Environment Notes

- **Keep code host-agnostic.** Don't name the deployment host (Railway, Azure, etc.) in source code, comments, or CI config — describe what the code/step does, not where it runs. The API has already moved hosts once, so host names in code go stale. Host-specific detail belongs in `api/docs/` and CLAUDE.md. Exception: a genuine host-specific mechanism the code must implement (e.g. reading an injected `PORT` env var) can name the host.
- **Container runtime is Podman, not Docker Desktop.** `docker.exe` is a winget shim talking to `npipe:////./pipe/docker_engine`, served by Podman. If a `docker` command fails with "failed to connect to the docker API at npipe:////./pipe/docker_engine", run `podman machine start` (machine name `podman-machine-default`); `DOCKER_HOST` doesn't need to be set. `podman machine stop` shuts it down.
- **Railway secrets are sealed and write-only.** Sealed variables (e.g. `DD_API_KEY`) are absent from `railway variables --json`, the MCP `list-variables` output, and the dashboard — that's the point, not a bug. Never conclude a variable is unset just because a listing omits it; verify by behavior instead (deployment logs showing auth success/failure). When setting a sealed value, pipe it straight from `op item get ... --reveal` into `railway variables --set` so it never appears in the transcript.

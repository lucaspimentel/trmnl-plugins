# CLAUDE.md

TRMNL e-ink display plugins. Plugins live under `plugins/`, shared API backend under `api/`.

For TRMNL docs: https://docs.trmnl.com/go/llms.txt (append `.md` to any `docs.trmnl.com/go/...` URL for leaner Markdown).

## Critical Gotchas

- `settings.yml` must be at `src/settings.yml` — trmnlp ignores one at the plugin root
- Plugin ID is stored in `src/settings.yml` under the `id:` key (not in `.trmnlp.yml`)
- `polling_url` interpolation: use `{{ keyname }}` (plain Liquid), not `##{{ keyname }}`
- Flex children that should shrink need `min-width: 0` — `plugins.js` measures widths before layout, so without it they expand to full container width
- Recipe linter counts raw substrings of `font-size`, `padding`, `margin`, etc. across ALL markup (including JS, comments, variable names) — max 6 total. See `.claude/skills/trmnl-dev/references/framework/updates.md` for workarounds.

## Deploy a Plugin

```bash
cd plugins/<name>
trmnlp push --force    # --force skips confirmation prompt
```

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

.NET 10 Azure Functions app (`TrmnlApi`) behind the Weather plugin. Solution: `api/TrmnlApi.slnx`.

```bash
dotnet build api/TrmnlApi.slnx
dotnet test api/TrmnlApi.slnx                     # also run by .github/workflows/tests.yml
cd api/src/TrmnlApi && func start                 # local run (Azure Functions Core Tools)
cd api/src/TrmnlApi && func azure functionapp publish trmnl-plugins-api          # prod
cd api/src/TrmnlApi && func azure functionapp publish trmnl-plugins-api-staging  # staging
```

- Routes: `GET /api/v1/forecast` (anonymous)
- `WeatherProviders` app setting is **required** (comma-separated, e.g. `open-meteo,pirate-weather`); the first entry is the default provider and the list defines the fallback order
- Provider keys: `OPEN_METEO_API_KEY`, `PIRATE_WEATHER_API_KEY`
- Round latitude/longitude to `F1` before logging (coordinates are PII)

## Credentials

`TRMNL_DEVICE_ID`, `TRMNL_DEVICE_API_KEY`, and `TRMNL_API_KEY` (for `trmnlp login`) are in **1Password item "trmnl"**:

```bash
op item get trmnl --fields label=TRMNL_DEVICE_ID,label=TRMNL_DEVICE_API_KEY --reveal
```

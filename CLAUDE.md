# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

This repository contains plugins for [TRMNL](https://usetrmnl.com/), an e-ink display device. TRMNL plugins fetch data from external APIs and render it using Liquid templates for display on the device.

For comprehensive TRMNL documentation, see: https://docs.trmnl.com/go/llms.txt

> **Tip**: Append `.md` to any `https://docs.trmnl.com/go/...` URL to get a leaner Markdown version.
> Example: `https://docs.trmnl.com/go/private-api/screens` → `https://docs.trmnl.com/go/private-api/screens.md`

## Plugin Architecture

Plugins live under the `plugins/` directory. Each plugin uses the trmnlp `src/` layout:

```
plugins/<name>/
  .trmnlp.yml       # trmnlp local dev config (not uploaded to TRMNL)
  fields.txt        # Documentation of API data fields
  src/
    settings.yml    # Plugin metadata, data strategy, polling URL (must be in src/)
    shared.liquid   # Reusable Liquid template blocks
    full.liquid     # Full screen layout
    half_horizontal.liquid
    half_vertical.liquid
    quadrant.liquid
```

Not all plugins implement every layout. For example, the weather plugin currently only has `full.liquid` and `shared.liquid`; the other layouts are TODOs.

Key `settings.yml` fields:
- `strategy`: Data fetching method (`polling` or `webhook`)
- `polling_url`: API endpoint to fetch data from
- `polling_verb`: HTTP method (typically `get`)
- `refresh_interval`: How often to fetch data (in minutes)
- `custom_fields`: Plugin metadata (name, description, GitHub URL)

## Template System

Templates use Liquid syntax with TRMNL-specific conventions:
- `{% template name %}...{% endtemplate %}`: Define reusable templates
- `{% render "template_name", param: value %}`: Include templates with parameters
- `data`: Variable containing API response data
- `trmnl.plugin_settings.instance_name`: Access plugin configuration

Layout templates typically render shared templates with size-specific parameters (e.g., `max_height`).

## Current Plugins

Plugins are located under `plugins/`.

### plugins/mbta-alerts
Displays service alerts from the Massachusetts Bay Transportation Authority (MBTA).
- API: `https://api-v3.mbta.com/alerts` (filtered for subway/light rail routes)
- Data fields: `service_effect`, `timeframe`, `header`, `updated_at`
- Features: Displays alerts sorted by severity, shows "No current alerts" when empty

### plugins/weather
Displays current conditions, a 24-hour temperature chart, and a 6-day forecast with weather icons throughout.
- API: Custom WeatherProxy Azure Function (`https://trmnl-weather.azurewebsites.net/api/v1/forecast`) that calls Open-Meteo and pre-processes the response; configurable lat/lon (default Boston 42.36°N, 71.06°W)
- WeatherProxy source: `plugins/weather/api/` — .NET 10 Azure Functions v4 app that fetches Open-Meteo, maps WMO codes to condition labels and icon classes, and returns a simplified JSON response
- Full layout: two-column at the outermost level — left column (68%): compact current conditions + hourly Highcharts chart with icons and sunrise/sunset lines; right column (32%): vertical 6-day forecast bars spanning full height
- Icons: Erik Flowers Weather Icons font, WMO code mapping with day/night variants; icon class pre-computed by WeatherProxy (not in Liquid templates)
- Only `full.liquid` is implemented; `half_horizontal`, `half_vertical`, and `quadrant` are TODOs

## Tools (`./tools/`)

### build-preview.sh
Builds standalone HTML previews for any plugin without needing `trmnlp serve`.

```bash
bash tools/build-preview.sh plugins/<name>              # build only
bash tools/build-preview.sh plugins/<name> --screenshot # build + screenshot
```

Runs `trmnlp build` then wraps each `_build/*.html` output with the TRMNL screen shell (`plugins.css`, `plugins.js`, Inter font). Open the results via a local HTTP server — `file://` is blocked by browsers:

```bash
cd plugins/<name>/_build && python -m http.server 8765
# http://localhost:8765/full.html
```

Keep the HTTP server running in the background — no need to restart it between rebuilds. The rebuild only overwrites the HTML files; the server continues serving them.

### Get-Trmnl-Image.ps1
Fetches the current TRMNL screen image and displays it in Sixel format (black/white).
- Saves timestamped PNG files (`yyyy-MM-dd_HH-mm-ss.png`)
- Run: `.\tools\Get-Trmnl-Image.ps1`

### Trmnl.Cli/
.NET 9 app that fetches and displays the current TRMNL screen image in Sixel (full color).
- Uses [SixPix](https://www.nuget.org/packages/SixPix) NuGet package
- Run: `dotnet run --project tools/Trmnl.Cli/Trmnl.Cli.csproj`

### Credentials
`Get-Trmnl-Image.ps1` and `Trmnl.Cli` require:
- `TRMNL_DEVICE_ID` — device identifier
- `TRMNL_DEVICE_API_KEY` — device API key

Stored in **1Password item "trmnl"** (personal account). Also contains `TRMNL_API_KEY` for `trmnlp login`.

Retrieve with: `op item get trmnl --fields label=TRMNL_DEVICE_ID,label=TRMNL_DEVICE_API_KEY --reveal`

## Development Workflow

When creating or modifying plugins:
1. Update `settings.yml` with API endpoint and configuration
2. Define reusable templates in `shared.liquid`
3. Create layout templates that render shared templates with appropriate parameters
4. Document API data fields in `fields.txt`
5. Test with different data scenarios (empty data, multiple items, long text)

Note: There are no build, test, or lint commands — plugins are deployed directly to TRMNL.

To push a plugin to TRMNL:
```bash
cd plugins/<name>
trmnlp push --force    # --force skips the interactive confirmation prompt
```

## Local Preview

### Preferred: Static build preview

Use `tools/build-preview.sh` to generate standalone HTML files that can be opened directly in a browser:

```bash
bash tools/build-preview.sh plugins/<name>              # build only
bash tools/build-preview.sh plugins/<name> --screenshot # build + screenshot to render.png
```

This runs `trmnlp build` (fetches live data and renders all layouts to `_build/`) then wraps each output file with the TRMNL screen shell (`plugins.css`, `plugins.js`, Inter font). The wrapped files are fully self-contained and render correctly in any browser.

To view after building, start a local HTTP server (required — `file://` is blocked by Edge/Chrome):
```bash
cd plugins/<name>/_build && python -m http.server 8765
# Open http://localhost:8765/full.html
```

Keep the HTTP server running in the background between rebuilds — it does not need to be restarted. The build only overwrites the HTML files; the server continues serving the updated versions on the next page reload.

The wrapper includes:
- `https://trmnl.com/css/latest/plugins.css`
- `https://trmnl.com/js/latest/plugins.js`
- Inter font from Google Fonts
- `<div class="screen screen--1bit screen--ogv2 screen--md screen--1x">` wrapper

**Important**: `plugins.js` runs pixel-perfect font processing that measures element widths. Flex children that should shrink must have `min-width: 0` set, otherwise they expand to full container width before `plugins.js` runs and the layout breaks.

### Alternative: trmnlp serve (live reload)

```bash
cd plugins/<name>
trmnlp serve          # http://localhost:4567
```

### Taking screenshots with playwright-cli

Use the `--screenshot` flag on `build-preview.sh` to build and screenshot in one step (requires the HTTP server on port 8765 to be running):

```bash
bash tools/build-preview.sh plugins/<name> --screenshot
# saves to plugins/<name>/render.png
```

To screenshot manually (e.g. after a browser refresh without rebuilding):

```bash
playwright-cli open --browser=msedge http://localhost:8765/full.html
playwright-cli resize 800 480
# wait ~3 seconds for Highcharts/fonts to render
playwright-cli screenshot --filename=plugins/<name>/render.png
playwright-cli close
```

## Assets (`./assets/`)

- `full-template.pdn` — Paint.NET template file for creating plugin screenshots at the correct 800×480 dimensions

## Skills

The `.claude/skills/trmnl-dev/` directory contains a Claude Code skill for full-lifecycle plugin development, including scaffolding, template authoring, and local preview with trmnlp.

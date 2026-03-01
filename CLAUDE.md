# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

This repository contains plugins for [TRMNL](https://usetrmnl.com/), an e-ink display device. TRMNL plugins fetch data from external APIs and render it using Liquid templates for display on the device.

For comprehensive TRMNL documentation, see: https://docs.trmnl.com/go/llms.txt

> **Tip**: Append `.md` to any `https://docs.trmnl.com/go/...` URL to get a leaner Markdown version.
> Example: `https://docs.trmnl.com/go/private-api/screens` → `https://docs.trmnl.com/go/private-api/screens.md`

## Plugin Architecture

Plugins live under the `plugins/` directory using the trmnlp `src/` layout (see README for structure).

Key `settings.yml` fields:
- `strategy`: Data fetching method (`polling` or `webhook`)
- `polling_url`: API endpoint to fetch data from
- `polling_verb`: HTTP method (typically `get`)
- `refresh_interval`: How often to fetch data (in minutes)
- `custom_fields`: Plugin metadata (name, description, GitHub URL)

**Critical**: `settings.yml` must be at `src/settings.yml` — trmnlp ignores a `settings.yml` at the plugin root.

## Template System

Templates use Liquid syntax with TRMNL-specific conventions:
- `{% template name %}...{% endtemplate %}`: Define reusable templates
- `{% render "template_name", param: value %}`: Include templates with parameters
- `data`: Variable containing API response data (when root is a JSON array)
- For JSON object responses, top-level keys are injected directly as variables (no `data.` prefix)
- `trmnl.plugin_settings.instance_name`: Access plugin configuration

Layout templates typically render shared templates with size-specific parameters (e.g., `max_height`).

**Important**: `plugins.js` runs pixel-perfect font processing that measures element widths. Flex children that should shrink must have `min-width: 0` set, otherwise they expand to full container width before `plugins.js` runs and the layout breaks.

## Current Plugins

### plugins/mbta-alerts
- API: `https://api-v3.mbta.com/alerts` (filtered for subway/light rail routes)
- Data fields: `service_effect`, `timeframe`, `header`, `updated_at`
- Features: Displays alerts sorted by severity, shows "No current alerts" when empty

### plugins/weather
- API: Custom WeatherProxy Azure Function (`https://trmnl-weather.azurewebsites.net/api/v1/forecast`) that calls Open-Meteo; configurable lat/lon (default Boston 42.36°N, 71.06°W)
- WeatherProxy source: `plugins/weather/api/` — .NET 10 Azure Functions v4 app that fetches Open-Meteo, maps WMO codes to condition labels and icon classes, and returns a simplified JSON response
- Full layout: two-column — left (68%): compact current conditions + hourly Highcharts chart with icons and sunrise/sunset lines; right (32%): vertical 6-day forecast bars
- Icons: Erik Flowers Weather Icons font, WMO code mapping with day/night variants; icon class pre-computed by WeatherProxy
- Only `full.liquid` is implemented; `half_horizontal`, `half_vertical`, and `quadrant` are TODOs

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

## Tools (`./tools/`)

See README for tool descriptions. Additional detail:

### Credentials for Get-Trmnl-Image.ps1 and Trmnl.Cli
- `TRMNL_DEVICE_ID` and `TRMNL_DEVICE_API_KEY` stored in **1Password item "trmnl"** (personal account)
- Also contains `TRMNL_API_KEY` for `trmnlp login`
- Retrieve with: `op item get trmnl --fields label=TRMNL_DEVICE_ID,label=TRMNL_DEVICE_API_KEY --reveal`

### build-preview.sh wrapper details
The wrapper added to each `_build/*.html` file includes:
- `https://trmnl.com/css/latest/plugins.css`
- `https://trmnl.com/js/latest/plugins.js`
- Inter font from Google Fonts
- `<div class="screen screen--1bit screen--ogv2 screen--md screen--1x">` wrapper

## Assets (`./assets/`)

- `full-template.pdn` — Paint.NET template file for creating plugin screenshots at the correct 800×480 dimensions

## Skills

The `.claude/skills/trmnl-dev/` directory contains a Claude Code skill for full-lifecycle plugin development, including scaffolding, template authoring, and local preview with trmnlp.

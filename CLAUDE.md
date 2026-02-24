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
Displays current conditions, a 24-hour temperature chart, and a 5-day forecast.
- API: Open-Meteo free forecast API (no key required)
- Features: Current temp/feels-like/humidity/wind, Highcharts hourly chart, daily range bars

## Tools (`./tools/`)

Utilities for interacting with the TRMNL API. Both require environment variables:
- `TRMNL_DEVICE_ID` - Your TRMNL device identifier
- `TRMNL_DEVICE_API_KEY` - Your TRMNL API access token

Credentials are stored in the **1Password item "trmnl"** (personal account). It contains:
- `TRMNL_DEVICE_ID` — device identifier
- `TRMNL_DEVICE_API_KEY` — device API key
- `TRMNL_API_KEY` — user API key (for `trmnlp login`)

Retrieve with: `op item get trmnl --fields label=TRMNL_DEVICE_ID,label=TRMNL_DEVICE_API_KEY --reveal`

### Get-Trmnl-Image.ps1
PowerShell script that fetches the current TRMNL screen image and displays it in Sixel format.
- Manual Sixel encoding with luminance-based black/white conversion (threshold: 127)
- Saves timestamped PNG files (`yyyy-MM-dd_HH-mm-ss.png`)
- Run: `.\tools\Get-Trmnl-Image.ps1`

### Trmnl.Cli/
.NET 9 console application that fetches the current TRMNL screen image and displays it in Sixel format.
- Uses [SixPix](https://www.nuget.org/packages/SixPix) NuGet package for Sixel encoding
- Supports full color output
- Run: `dotnet run --project tools/Trmnl.Cli/Trmnl.Cli.csproj`

## Development Workflow

When creating or modifying plugins:
1. Update `settings.yml` with API endpoint and configuration
2. Define reusable templates in `shared.liquid`
3. Create layout templates that render shared templates with appropriate parameters
4. Document API data fields in `fields.txt`
5. Test with different data scenarios (empty data, multiple items, long text)

Note: There are no build, test, or lint commands — plugins are deployed directly to TRMNL.

## Local Preview

Use [trmnlp](https://github.com/usetrmnl/trmnlp) to preview plugins locally. See the trmnl-dev skill (`.claude/skills/trmnl-dev/SKILL.md`) for detailed trmnlp usage and workflow instructions.

## Skills

The `.claude/skills/trmnl-dev/` directory contains a Claude Code skill for full-lifecycle plugin development, including scaffolding, template authoring, and local preview with trmnlp.

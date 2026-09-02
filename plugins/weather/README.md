# Weather

A [TRMNL](https://usetrmnl.com/) plugin that displays current conditions, a 24-hour temperature chart, and a multi-day forecast with weather icons.

![Weather screenshot](screenshot.png)

## Features

- Current conditions: temperature (°F and °C), feels like, humidity, wind speed/direction, weather icon
- 24-hour chart: temperature spline + precipitation probability bars (Highcharts)
- Weather icons on the hourly chart x-axis with day/night variants
- Sunrise and sunset times marked as dashed vertical lines on the chart
- Multi-day forecast with temperature range bars and weather icons (up to 14 days)
- Optional abbreviated weekday labels (`Wed` instead of `Wednesday`) for narrow slots
- 12-hour (am/pm) or 24-hour clock for all displayed times
- Configurable location by city name, postal code, or coordinates (defaults to Boston, MA)
- The matched location shown in the title bar, so a wrong match is visible (toggle with **Show Location**)
- Failures render as a readable on-screen message and a suggested fix rather than a blank or stale screen

## Setup

Install as a private plugin on [TRMNL](https://usetrmnl.com/). Configure your location by setting the **Location** field with a city name, postal code, or coordinate pair (latitude first). Your saved **Latitude** and **Longitude** still apply when **Location** is blank. Set **Country** if you use a bare postal code, since the same code is often valid in several countries. Optionally override the **Units**, **Hours**, **Days**, **Time Format**, **Show Location**, and **Abbreviate Day Names** fields. The plugin polls the API every 60 minutes.

> **Note:** Coordinate pairs are not checked for order, so a swapped pair can silently show the wrong place. Postal codes are not unique across countries (`02180` is a real code in six, and `75001` is both central Paris and Addison, TX). Your device's time zone settles most of them on its own. Set the **Country** field when it does not, or add the country to **Location** directly (`75001, US`).

## Data Source

Weather data is fetched via a custom ASP.NET Core backend (`api/` in this repo, deployed to Railway) that normalizes upstream responses into a uniform shape (condition labels, weather-icon classes, day/night variants). Supported upstreams: [Open-Meteo](https://open-meteo.com/) (default) and [Pirate Weather](https://pirateweather.net/). The upstream is chosen by the backend, not by a plugin setting. If it fails, the backend automatically falls back to the other one and reports which provider actually served the data.

### Attribution

- [Open-Meteo](https://open-meteo.com/) data is licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). This plugin modifies the data: unit normalization, WMO-code-to-icon mapping, day/night variants, condition label simplification, and trimming the response to the fields used by the templates.
- [Pirate Weather](https://pirateweather.net/) is an open-source weather API built on NOAA forecast data. This plugin modifies the response in the same ways as above.

**Proxy URL**: `https://trmnl-plugins-prod.lucasp.net/api/v2/forecast`

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `place` | no | — | City, postal code, or `latitude, longitude` pair (latitude first). Falls back to `latitude`/`longitude` when blank. v2 only |
| `latitude` | v1 | — | Location latitude (v2: fallback when `place` is blank) |
| `longitude` | v1 | — | Location longitude (v2: fallback when `place` is blank) |
| `units` | no | `imperial` | `imperial` (°F, mph) or `metric` (°C, km/h) |
| `hours` | no | `25` | Number of hourly forecast entries (1–25) |
| `days` | no | `6` | Number of daily forecast entries (1–14; Pirate Weather only supplies up to 7) |
| `provider` | no | server-configured | Upstream provider: `open-meteo` or `pirate-weather`. The plugin does not send it, so the server default applies |
| `time_format` | no | `12h` | `12h` (am/pm) or `24h` clock for the hourly labels |
| `show_place` | no | `yes` | `no` omits the `place` block, which is how the plugin hides the matched location in the title bar. v2 only |
| `abbreviate_days` | no | `no` | `yes` shortens the daily forecast's weekday labels to `Wed`. Echoed back as `meta.abbreviate_days`. v2 only |
| `country` | no | — | ISO 3166-1 alpha-2 of the user's country, used only to settle an ambiguous `place`. A preference, not a filter: a location outside it still resolves, and a country typed into `place` wins over it. Read from the leading two letters, so the dropdown's own slugified value works. v2 only |
| `tz` | no | — | The user's IANA time zone, e.g. `America/New_York`, sent as `{{ trmnl.user.time_zone_iana }}`. Settles an ambiguous postal code the same way `country` does, and only when `country` says nothing, so a user who has set nothing still gets their own country. Postal codes only. v2 only |

## Development

### Local Preview

```bash
cd plugins/weather
trmnlp serve    # http://localhost:4567
```

To render without hitting the live API, put the response's top-level keys (`current`, `hourly`, `daily`, `meta`, `place`) under `variables:` in `.trmnlp.yml` — there is no `data:` key in that file, and the API's root is an object rather than an array. Alternatively, drop a saved response at `~/.cache/trmnl/data.json`, which is where trmnlp caches the last poll and reads it back from.

`assets/data-2026-02-24T18-30.json` predates the backend: it is a **raw Open-Meteo response**, not the shape `/api/v2/forecast` returns, so it will not render against the current templates as-is. The filename encodes the `current.time` value it was captured at.

### External Dependencies

#### Highcharts

Used for the hourly temperature spline + precipitation bar chart.

- License: free for non-commercial use
- Loaded from `https://trmnl.com/js/highcharts/12.3.0/highcharts.js`, alongside the `pattern-fill.js` module from the same version, which is required because every framework paint resolves to a dither pattern on an e-ink screen

#### Erik Flowers Weather Icons

CSS icon font used for current conditions and chart labels.

- GitHub: https://github.com/erikflowers/weather-icons
- License: SIL OFL 1.1 (font), MIT (CSS)
- Loaded from jsDelivr CDN (`cdn.jsdelivr.net`)
- Icon class (e.g. `wi-day-sunny`) is pre-computed by the API proxy, including day/night variants; templates prepend the `wi` base class

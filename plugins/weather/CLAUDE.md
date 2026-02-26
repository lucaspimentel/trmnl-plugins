# Weather Plugin

Displays current conditions, a 24-hour temperature chart, and a 6-day forecast
using a custom WeatherProxy Azure Function that fetches and pre-processes Open-Meteo data.

## File Structure

```
plugins/weather/
  .trmnlp.yml         # trmnlp local dev config (static or live data)
  CLAUDE.md           # this file
  fields.txt          # API response field documentation (raw Open-Meteo format, for reference)
  api/                # WeatherProxy Azure Function source (.NET 10)
  assets/             # local copies of self-hosted files + sample API response JSON
  src/
    settings.yml      # plugin config and polling URL (must be in src/)
    shared.liquid     # all reusable templates
    full.liquid       # full screen layout (only layout implemented)
    # half_horizontal.liquid  TODO
    # half_vertical.liquid    TODO
    # quadrant.liquid         TODO
```

## Local Preview

See repo root `CLAUDE.md` for preview options (`trmnlp serve` vs static build).

To use static sample data instead of live API, configure a `data:` block in `.trmnlp.yml` pointing to a cached JSON file (e.g. `assets/data-2026-02-24T18-30.json`). The filename encodes the `current.time` value to use as "now" when testing.

## External Dependencies

### API: WeatherProxy (Azure Functions)

The plugin polls a custom Azure Function instead of Open-Meteo directly. The proxy handles WMO code mapping (condition strings, icon CSS classes, wind direction labels) and returns a pre-processed JSON object.

- **Deployed URL**: `https://trmnl-weather.azurewebsites.net/api/forecast?latitude={lat}&longitude={lon}`
- **Source**: `plugins/weather/api/` — .NET 10 Azure Functions v4 app
- **Auth**: None (anonymous)
- **Query params**: `fake=true` or `fake=1` to inject randomized precipitation values (useful for testing chart rendering)

WeatherProxy calls Open-Meteo with:
```
https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}
  &current=temperature_2m,apparent_temperature,relative_humidity_2m,
           precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day
  &hourly=temperature_2m,weather_code,precipitation_probability
  &daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max,
         sunrise,sunset
  &temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch
  &timezone=auto&forecast_hours=25&forecast_days=6
```

#### WeatherProxy Response Shape

```json
{
  "current": {
    "time": "2026-02-25T14:00",
    "temperature_f": 35,
    "temperature_c": 2,
    "apparent_temperature_f": 28,
    "apparent_temperature_c": -2,
    "relative_humidity": 72,
    "precipitation_in": 0.0,
    "weather_code": 3,
    "condition": "Overcast",
    "icon_class": "wi wi-cloudy",
    "wind_speed_mph": 12,
    "wind_direction_deg": 270,
    "wind_direction": "W",
    "is_day": true
  },
  "hourly": {
    "entries": [
      {
        "time": "2026-02-25T14:00",
        "label": "Now",
        "temperature_f": 35,
        "precipitation_probability": 10,
        "weather_code": 3,
        "icon_class": "wi wi-cloudy",
        "is_day": true
      }
      // ... 24 more entries
    ]
  },
  "daily": {
    "entries": [
      {
        "date": "2026-02-25",
        "high_f": 38,
        "low_f": 28,
        "weather_code": 61,
        "condition": "Light rain",
        "icon_class": "wi wi-rain",
        "precipitation_probability": 80,
        "sunrise": "2026-02-25T06:30",
        "sunset": "2026-02-25T17:35"
      }
      // ... 5 more entries (6 total)
    ]
  }
}
```

### JS Library: Highcharts

- **Docs**: https://api.highcharts.com/highcharts/
- **License**: Free for non-commercial use
- **Usage**: Spline chart (temperature) + column chart (precip %) for 24-hour forecast
- **Self-hosted**: `https://trmnlplugins.blob.core.windows.net/assets/highcharts.js`
- **Note**: The Highcharts CDN (`code.highcharts.com`) rate-limits automated/headless requests (429), so we self-host.

### Icon Font: Erik Flowers Weather Icons

- **GitHub**: https://github.com/erikflowers/weather-icons
- **Demo**: https://erikflowers.github.io/weather-icons/
- **License**: SIL OFL 1.1 (font), MIT (CSS/code)
- **Self-hosted**: `https://trmnlplugins.blob.core.windows.net/assets/weather-icons.woff2` (~44 KB)
- **Usage**: CSS class from `icon_class` field (e.g. `wi wi-day-sunny`, `wi wi-night-clear`, `wi wi-rain`)
- **Day/night variants** for WMO codes 0–2 are pre-computed by WeatherProxy using sunrise/sunset data
- **Where used**: Current conditions icon, hourly chart x-axis labels (from `hourly.entries[].icon_class`), daily forecast bars (from `daily.entries[].icon_class`)

### Static Asset Hosting: Azure Blob Storage

- **Account**: `trmnlplugins` (resource group `trmnl-plugins`, East US)
- **Container**: `assets` (public read access)
- **Base URL**: `https://trmnlplugins.blob.core.windows.net/assets/`
- **Hosted files**: `highcharts.js`, `weather-icons.woff2`, `weather-icons.css`
- **Upload**: `az storage blob upload --account-name trmnlplugins --container-name assets --file <file> --name <name> --content-type <type> --auth-mode key`

## Template Architecture

### `full.liquid` layout structure

The two-column split is at the outermost level so the right column spans full height:

```
[ left column (68%)               | right column (32%)      ]
[   weather_current_compact       |                         ]
[   weather_hourly_chart          | weather_daily_bars_vert |
[                                 | (full height, 6 days)   ]
[           title_bar (full width)                         ]
```

### Templates in `shared.liquid`

All logic lives in `shared.liquid` as `{% template %}` blocks:

| Template | Purpose |
|----------|---------|
| `weather_current_compact` | Current conditions (left-column, centered): temp °F/°C + weather icon + details |
| `weather_hourly_chart` | Highcharts spline (temp °F) + column (precip %) for next 24h, weather icons on x-axis, sunrise/sunset vertical lines |
| `weather_daily_bars_vertical` | 6-day CSS range bars (vertical layout for right column), weather icons next to bars, labels inside bars |
| `title_bar` | Standard bottom bar with day + time |

## Data Access

The WeatherProxy returns a JSON object. trmnlp injects object keys as **top-level variables**,
not under `data`. Templates use `current`, `hourly`, `daily` directly — not `data.current` etc.

This is different from JSON:API array responses (like MBTA) where the array becomes `data`.

Layout files pass these directly to shared templates:
```liquid
{% render "weather_current_compact", current: current %}
{% render "weather_hourly_chart", hourly: hourly, daily: daily, current_time: current.time, chart_height: 230 %}
{% render "weather_daily_bars_vertical", daily_entries: daily.entries, num_days: 6 %}
```

Key access patterns:
- `current.temperature_f`, `current.condition`, `current.icon_class`, `current.is_day`
- `hourly.entries` — array of 25 hourly entries (current hour + 24h ahead)
- `daily.entries` — array of 6 daily entries (today + 5 days)
- Icon classes already include day/night variant (e.g. `wi wi-day-sunny`)

## Key Implementation Notes

**Icon classes** are pre-computed by WeatherProxy (`WmoCodeMap.GetIconClass`), so Liquid templates
use `entry.icon_class` directly — no WMO-to-icon mapping needed in templates.

**Condition labels** (`current.condition`, `daily.entries[].condition`) are also pre-computed
by WeatherProxy.

**Highcharts**: Script tag must be inside the template block (not in the layout file).
Chart has three Y-axes: `yAxis[0]` = °F (left), `yAxis[1]` = precip % 0–100 (hidden), `yAxis[2]` = °C (right, linked to yAxis[0]).
Chart margin: `margin: [10, 58, 68, 58]` (extra bottom space for icon+text x-axis labels).

**Hourly chart features**:
- Weather icons on x-axis labels (every 4 hours) using `entry.icon_class`
- Sunrise/sunset dashed vertical plotLines from `daily.entries[0].sunrise`/`.sunset`
- Day/night already resolved in `entry.is_day`

**Vertical forecast bars** (`weather_daily_bars_vertical`): Shows all 6 days from `daily.entries`.
Bar widths computed from overall min/max across all days:
```liquid
{% assign left_pct = d_low | minus: overall_min | times: 100 | divided_by: range %}
{% assign width_pct = d_high | minus: d_low | times: 100 | divided_by: range | at_least: 1 %}
```

**Vertical bar labels**: Labels render inside the bar (white text)
when `width_pct >= 25`, otherwise outside (black text) to avoid overflow on narrow ranges.

# Weather Plugin

Displays current conditions, a 24-hour temperature chart, and a 6-day forecast
using a custom TrmnlApi Azure Function that fetches and pre-processes Open-Meteo data.

See `README.md` for contributor setup and external dependency details.

## API: TrmnlApi

- **Deployed URL**: `https://trmnl-plugins-api.azurewebsites.net/api/v1/forecast?latitude={lat}&longitude={lon}`
- **Source**: `api/` (repo root)
- **Auth**: None (anonymous)
- **Query params**: `latitude`, `longitude`, `units` (`imperial`/`metric`), `hours` (1–25), `days` (1–6); `fake=true` injects random precipitation for testing

### Response Shape

```json
{
  "current": {
    "time": "2026-02-25T14:00",
    "temperature": 35,
    "apparent_temperature": 28,
    "relative_humidity": 72,
    "precipitation": 0.0,
    "weather_code": 3,
    "condition": "Overcast",
    "icon_class": "wi wi-cloudy",
    "wind_speed": 12,
    "wind_direction_deg": 270,
    "wind_direction": "W",
    "is_day": true
  },
  "hourly": {
    "entries": [
      {
        "time": "2026-02-25T14:00",
        "label": "Now",
        "temperature": 35,
        "precipitation_probability": 10,
        "weather_code": 3,
        "icon_class": "wi wi-cloudy",
        "is_day": true
      }
      // ... up to 25 entries
    ]
  },
  "daily": {
    "entries": [
      {
        "date": "2026-02-25",
        "high": 38,
        "low": 28,
        "weather_code": 61,
        "condition": "Light rain",
        "icon_class": "wi wi-rain",
        "precipitation_probability": 80,
        "sunrise": "2026-02-25T06:30",
        "sunset": "2026-02-25T17:35"
      }
      // ... up to 6 entries
    ]
  }
}
```

Field names are unit-agnostic; actual units depend on the `units` param (`imperial`: °F, mph / `metric`: °C, km/h).

## Data Access in Templates

TrmnlApi returns a JSON **object** — trmnlp injects top-level keys as top-level variables (not under `data`):

```liquid
{% render "weather_current", current: current %}
{% render "weather_hourly_chart", hourly: hourly, daily: daily, current_time: current.time, chart_height: 230 %}
{% render "weather_daily_bars_vertical", daily_entries: daily.entries, num_days: 6, current_temp: current.temperature %}
```

Key access patterns:
- `current.temperature`, `current.condition`, `current.icon_class`, `current.is_day`
- `hourly.entries` — array of up to 25 entries (current hour + next 24h)
- `daily.entries` — array of up to 6 entries (today + next 5 days)
- `icon_class` already includes day/night variant (e.g. `wi wi-day-sunny`) — pre-computed by TrmnlApi

## Template Architecture

All logic lives in `shared.liquid` as `{% template %}` blocks:

| Template | Purpose |
|----------|---------|
| `weather_current` | Current conditions: temp, icon, details (full layout) |
| `weather_current_compact` | Compact current conditions (half/quadrant layouts) |
| `weather_hourly_chart` | Highcharts spline (temp) + column (precip %) with icons on x-axis, sunrise/sunset lines |
| `weather_daily_bars_vertical` | CSS range bars, weather icons, labels inside/outside bar |
| `title_bar` | Bottom bar with day + time |

`full.liquid` layout structure:

```
[ left (64%)                      | right (36%)             ]
[   weather_current               |                         ]
[   weather_hourly_chart          | weather_daily_bars_vert ]
[           title_bar (full width)                          ]
```

## Key Implementation Notes

**Highcharts**: Script tag must be inside the template block (not the layout file).
Three Y-axes: `yAxis[0]` = °F (left), `yAxis[1]` = precip % 0–100 (hidden), `yAxis[2]` = °C (right, linked to yAxis[0]).
Margin: `[10, 58, 68, 58]` (extra bottom for icon+text x-axis labels).

**Hourly chart**: Weather icons on x-axis every 4 hours; sunrise/sunset as dashed plotLines from `daily.entries[0].sunrise`/`.sunset`.

**Vertical forecast bars**: Bar widths from overall min/max across all days:
```liquid
{% assign left_pct = d_low | minus: overall_min | times: 100 | divided_by: range %}
{% assign width_pct = d_high | minus: d_low | times: 100 | divided_by: range | at_least: 1 %}
```
Labels render inside bar (white) when `width_pct >= 25`, outside (black) otherwise.

## Local Preview

```bash
cd plugins/weather
trmnlp serve    # http://localhost:4567
```

To use cached data: configure a `data:` block in `.trmnlp.yml` pointing to a file in `assets/` (e.g. `assets/data-2026-02-24T18-30.json`). The filename encodes the `current.time` value used as "now".

# Weather Plugin

Displays current conditions, a 24-hour temperature chart, and a 5-day forecast
using the [Open-Meteo](https://open-meteo.com/) free API (no key required).

## File Structure

```
plugins/weather/
  .trmnlp.yml         # trmnlp local dev config (static or live data)
  CLAUDE.md           # this file
  fields.txt          # API response field documentation
  src/
    settings.yml      # plugin config and polling URL (must be in src/)
    shared.liquid     # all reusable templates
    full.liquid
    half_horizontal.liquid  # TODO
    half_vertical.liquid    # TODO
    quadrant.liquid         # TODO
```

## Local Preview

See repo root `CLAUDE.md` for preview options (`trmnlp serve` vs static build).

To use static sample data instead of live API, add a `data:` block to `.trmnlp.yml` pointing to a cached JSON file (e.g. `data-2026-02-24T18-30.json`). The filename encodes the `current.time` value to use as "now" when testing.

## External Dependencies

### API: Open-Meteo

- **URL**: https://api.open-meteo.com/v1/forecast
- **Docs**: https://open-meteo.com/en/docs
- **Auth**: None (free, no API key)
- **Params**: Boston (42.36, -71.06), °F, mph, `forecast_hours=25`, `forecast_days=5`

Full polling URL (also in `src/settings.yml`):
```
https://api.open-meteo.com/v1/forecast?latitude=42.36&longitude=-71.06
  &current=temperature_2m,apparent_temperature,relative_humidity_2m,
           precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day
  &hourly=temperature_2m,weather_code,precipitation_probability
  &daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max,
         sunrise,sunset
  &temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch
  &timezone=auto&forecast_hours=25&forecast_days=5
```

Response: `hourly` has 25 entries (current hour + 24h), `daily` has 5 entries (today + 4 days).
Weather conditions are returned as **WMO weather codes** in `current.weather_code`, `hourly.weather_code`, and `daily.weather_code`.
`daily.sunrise` and `daily.sunset` are ISO 8601 timestamps used for day/night icon selection.

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
- **Usage**: CSS class `wi wi-wmo4680-{wmo_code}` maps directly to WMO weather codes
- **Day/night**: `wi wi-day-*` / `wi wi-night-*` variants for codes 0–2, determined by comparing hour timestamps against `daily.sunrise`/`daily.sunset`
- **Where used**: Current conditions icon, hourly chart x-axis labels, daily forecast bars

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
[ left column (62%)               | right column (38%)      ]
[   weather_current_compact       |                         ]
[   weather_hourly_chart          | weather_daily_bars_vert |
[                                 | (full height)           ]
[           title_bar (full width)                         ]
```

### Templates in `shared.liquid`

All logic lives in `shared.liquid` as `{% template %}` blocks:

| Template | Purpose |
|----------|---------|
| `weather_current` | Current conditions (full-width): location, temp °F, feels like, humidity, wind |
| `weather_current_compact` | Current conditions (left-column, centered): temp °F/°C + weather icon + details |
| `weather_hourly_chart` | Highcharts spline (temp °F) + column (precip %) for next 24h, weather icons on x-axis, sunrise/sunset vertical lines |
| `weather_daily_bars` | 5-day CSS range bars (horizontal layout), °F only |
| `weather_daily_bars_vertical` | 5-day CSS range bars (vertical layout for right column), weather icons next to bars, labels inside bars |
| `title_bar` | Standard bottom bar with day + time |

## Data Access

Open-Meteo returns a JSON object (not an array). trmnlp injects object keys as **top-level
variables**, not under `data`. So templates and layout files must use `current`, `hourly`,
`daily` directly — **not** `data.current`, `data.hourly`, `data.daily`.

This is different from JSON:API array responses (like MBTA) where the array becomes `data`.

Layout files pass these directly to shared templates:
```liquid
{% render "weather_current", current: current, ... %}
{% render "weather_hourly_chart", hourly: hourly, daily: daily, current_time: current.time, ... %}
{% render "weather_daily_bars", daily: daily, ... %}
```

## Key Implementation Notes

**WMO weather codes** → condition label strings are handled via `if/elsif` chains in
`weather_current` and `weather_current_compact` (duplicated due to Liquid template scoping).

**WMO-to-icon mapping** is implemented in three places (Liquid template scoping prevents sharing):
- `weather_current_compact`: Liquid `if/elsif` chain with day/night via `current.is_day`
- `weather_hourly_chart`: JS `wmoIcon(code, night)` function with day/night via `daily.sunrise`/`daily.sunset`
- `weather_daily_bars_vertical`: Liquid `if/elsif` chain, day icons only (forecast context)

**Highcharts**: Script tag must be inside the template block (not in the layout file):
```liquid
<script src="https://trmnl.com/js/highcharts/12.3.0/highcharts.js"></script>
```
Chart has three Y-axes: `yAxis[0]` = °F (left), `yAxis[1]` = precip % 0–100 (hidden), `yAxis[2]` = °C (right, linked to yAxis[0]).
Chart margin: `margin: [10, 58, 68, 58]` (extra bottom space for icon+text x-axis labels).

**Hourly chart features**:
- Weather icons on x-axis labels (every 4 hours) with day/night variants
- Sunrise/sunset dashed vertical plotLines with labeled times
- Day/night determined by comparing `hourTimes[i]` against `sunEvents[].rise`/`.set`

**Hourly slicing**: API returns 25 entries (`forecast_hours=25`). Find the current hour index by
comparing `hourly.time[i]` (first 13 chars) against `current.time` (first 13 chars), then
use `offset: start_index limit: 25` on a second loop.

**Forecast bars** (`weather_daily_bars`): Show days 1–5 (skip index 0 = today).
**Vertical forecast bars** (`weather_daily_bars_vertical`): Show all days starting from index 0, with weather icons to the left of each bar.

Bar position computed as:
```liquid
{% assign left_pct = d_low | minus: overall_min | times: 100 | divided_by: range %}
{% assign width_pct = d_high | minus: d_low | times: 100 | divided_by: range | at_least: 1 %}
```

**Vertical bar labels**: Labels render inside the bar (white text)
when `width_pct >= 25`, otherwise outside (black text) to avoid overflow on narrow ranges.

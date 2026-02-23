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

```bash
cd plugins/weather
trmnlp serve          # http://localhost:4567
```

To use live API data, ensure `.trmnlp.yml` has no `data:` block — trmnlp will poll
Open-Meteo automatically on startup. To use static sample data, add a `data:` block
to `.trmnlp.yml` (see the git history for a full example).

To kill a stuck server on port 4567:
```bash
pwsh -NoProfile -File ../../kill-port.ps1
```

## API

Open-Meteo forecast endpoint, Boston (42.36, -71.06), °F, mph:

```
https://api.open-meteo.com/v1/forecast?latitude=42.36&longitude=-71.06
  &current=temperature_2m,apparent_temperature,relative_humidity_2m,
           precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day
  &hourly=temperature_2m,weather_code,precipitation_probability
  &daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max
  &temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch
  &timezone=auto&forecast_days=6
```

Response arrays: `hourly` has 144 entries (6 days × 24h), `daily` has 6 entries (today + 5 days).

## Template Architecture

All logic lives in `shared.liquid` as four `{% template %}` blocks:

| Template | Purpose |
|----------|---------|
| `weather_current` | Current conditions: location, icon, temp (°F/°C), feels like, humidity, wind |
| `weather_hourly_chart` | Highcharts spline (temp) + column (precip %) for next 24h |
| `weather_daily_bars` | 5-day CSS range bars with condition, precip%, low/high temps |
| `title_bar` | Standard bottom bar |

## Data Access

Open-Meteo returns a JSON object (not an array). trmnlp injects object keys as **top-level
variables**, not under `data`. So templates and layout files must use `current`, `hourly`,
`daily` directly — **not** `data.current`, `data.hourly`, `data.daily`.

This is different from JSON:API array responses (like MBTA) where the array becomes `data`.

Layout files pass these directly to shared templates:
```liquid
{% render "weather_current", current: current, ... %}
{% render "weather_hourly_chart", hourly: hourly, current_time: current.time, ... %}
{% render "weather_daily_bars", daily: daily, ... %}
```

## Key Implementation Notes

**Temperature conversion** (°F → °C in Liquid):
```liquid
{% assign temp_c = temp_f | minus: 32 | times: 5 | divided_by: 9 | round %}
```
Note: `divided_by` does integer division — multiply by 100 first if you need decimals.

**WMO weather codes** → condition labels + icon names are handled via `if/elsif` chains
in `weather_current`. Use `current.is_day` to pick day/night variants.

**Weather icons**: [Meteocons](https://bas.dev/work/meteocons) via CDN:
```
https://bmcdn.nl/assets/weather-icons/v3.0/line/svg/{icon-name}.svg
```
Use `line` style for clean black rendering on e-ink.

**Highcharts**: Must include the CDN script inside the template block (not in the layout file):
```liquid
<script src="https://code.highcharts.com/highcharts.js"></script>
```
The right °C Y-axis uses `linkedTo: 0` + a `formatter` function to convert from °F.
Right margin must be at least 44px to prevent label clipping: `margin: [10, 44, 28, 36]`.

**Hourly slicing**: Open-Meteo returns 144 hourly entries. Find the current hour index by
comparing `hourly.time[i]` (first 13 chars) against `current.time` (first 13 chars), then
use `offset: start_index limit: 25` on a second loop.

**Forecast bars**: Show days 1–5 (skip index 0 = today). Bar position computed as:
```liquid
{% assign left_pct = d_low | minus: overall_min | times: 100 | divided_by: range %}
{% assign width_pct = d_high | minus: d_low | times: 100 | divided_by: range | at_least: 1 %}
```

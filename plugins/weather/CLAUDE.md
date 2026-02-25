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

## External Dependencies

### API: Open-Meteo

- **URL**: https://api.open-meteo.com/v1/forecast
- **Docs**: https://open-meteo.com/en/docs
- **Auth**: None (free, no API key)
- **Params**: Boston (42.36, -71.06), °F, mph, `forecast_hours=25`, `forecast_days=6`

Full polling URL (also in `src/settings.yml`):
```
https://api.open-meteo.com/v1/forecast?latitude=42.36&longitude=-71.06
  &current=temperature_2m,apparent_temperature,relative_humidity_2m,
           precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day
  &hourly=temperature_2m,weather_code,precipitation_probability
  &daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max
  &temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch
  &timezone=auto&forecast_hours=25&forecast_days=6
```

Response: `hourly` has 25 entries (current hour + 24h), `daily` has 6 entries (today + 5 days).
Weather conditions are returned as **WMO weather codes** in `current.weather_code` and `daily.weather_code`.

### JS Library: Highcharts

- **CDN**: https://code.highcharts.com/highcharts.js
- **Docs**: https://api.highcharts.com/highcharts/
- **License**: Free for non-commercial use
- **Usage**: Spline chart (temperature) + column chart (precip %) for 24-hour forecast
- **Note**: CDN applies rate limiting in automated/headless environments (429). Plan to self-host on Azure Blob Storage.

### Icon Font: Erik Flowers Weather Icons

- **GitHub**: https://github.com/erikflowers/weather-icons
- **Demo**: https://erikflowers.github.io/weather-icons/
- **License**: SIL OFL 1.1 (font), MIT (CSS/code)
- **Font file**: `weather-icons.woff2` (~44 KB)
- **Usage**: CSS class `wi wi-wmo4680-{wmo_code}` maps directly to WMO weather codes
- **Day/night**: `wi wi-day-*` / `wi wi-night-*` variants available via `current.is_day`
- **Status**: Plan to self-host on Azure Blob Storage (not yet implemented)

### Static Asset Hosting: Azure Blob Storage (planned)

- Self-hosted copies of `highcharts.js` and `weather-icons.woff2` to avoid CDN rate limits and external dependencies
- Public container with anonymous read access
- URL pattern: `https://<account>.blob.core.windows.net/<container>/<filename>`

## Template Architecture

All logic lives in `shared.liquid` as `{% template %}` blocks:

| Template | Purpose |
|----------|---------|
| `weather_current` | Current conditions (full-width): location, temp °F, feels like, humidity, wind |
| `weather_current_compact` | Current conditions (left-column): 48px temp + details stacked right |
| `weather_hourly_chart` | Highcharts spline (temp °F) + column (precip %) for next 24h |
| `weather_daily_bars` | 5-day CSS range bars (horizontal layout), °F only |
| `weather_daily_bars_vertical` | 5-day CSS range bars (vertical layout for right column), labels inside bars |
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

**All temperatures are °F only.** No °C conversion anywhere in templates.

**WMO weather codes** → condition label strings are handled via `if/elsif` chains in
`weather_current` and `weather_current_compact` (duplicated due to Liquid template scoping).
Use `current.is_day` to pick day/night icon variants when icons are added.

**Highcharts**: Script tag must be inside the template block (not in the layout file):
```liquid
<script src="https://code.highcharts.com/highcharts.js"></script>
```
Chart has two Y-axes: `yAxis[0]` = °F (left), `yAxis[1]` = precip % 0–100 (right, labels hidden).
Right margin is 10px (no right-axis labels): `margin: [10, 10, 30, 58]`.

**Hourly slicing**: API returns 25 entries (`forecast_hours=25`). Find the current hour index by
comparing `hourly.time[i]` (first 13 chars) against `current.time` (first 13 chars), then
use `offset: start_index limit: 25` on a second loop.

**Forecast bars**: Show days 1–5 (skip index 0 = today). Bar position computed as:
```liquid
{% assign left_pct = d_low | minus: overall_min | times: 100 | divided_by: range %}
{% assign width_pct = d_high | minus: d_low | times: 100 | divided_by: range | at_least: 1 %}
```

**Vertical bar labels** (`weather_daily_bars_vertical`): Labels render inside the bar (white text)
when `width_pct >= 25`, otherwise outside (black text) to avoid overflow on narrow ranges.

# Weather Plugin Planning Document

## Overview

A TRMNL plugin that displays current weather conditions and a short daily forecast using the
[Open-Meteo](https://open-meteo.com/) free weather API. No API key required.

---

## Open-Meteo API

### Base URL

```
https://api.open-meteo.com/v1/forecast
```

### Proposed polling_url

Requires both `hourly` (for 24h chart) and `daily` (for 5-day forecast) data, plus `current`:

```
https://api.open-meteo.com/v1/forecast?latitude=42.36&longitude=-71.06&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day&hourly=temperature_2m,weather_code,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch&timezone=auto&forecast_days=6
```

> **Note**: `forecast_days=6` gives today + 5 future days. Latitude/longitude hardcoded for
> Boston, MA (42.36, -71.06) for now.

### Response Shape

```json
{
  "latitude": 42.36,
  "longitude": -71.06,
  "timezone": "America/New_York",
  "current_units": { "temperature_2m": "°F", "wind_speed_10m": "mph" },
  "current": {
    "time": "2026-02-23T14:00",
    "temperature_2m": 38.5,
    "apparent_temperature": 31.2,
    "relative_humidity_2m": 62,
    "precipitation": 0.0,
    "weather_code": 3,
    "wind_speed_10m": 12.4,
    "wind_direction_10m": 270,
    "is_day": 1
  },
  "hourly": {
    "time": ["2026-02-23T00:00", "2026-02-23T01:00", ...],
    "temperature_2m": [33, 32, 31, ...],
    "weather_code": [71, 71, 3, ...],
    "precipitation_probability": [80, 75, 20, ...]
  },
  "daily": {
    "time": ["2026-02-23", "2026-02-24", ...],
    "temperature_2m_max": [40, 45, 38, 35, 42, 44],
    "temperature_2m_min": [28, 32, 25, 22, 30, 31],
    "weather_code": [3, 61, 71, 0, 1, 2],
    "precipitation_probability_max": [20, 80, 90, 5, 10, 30]
  }
}
```

> **Template access**: All fields via `data.*`
> e.g. `data.current.temperature_2m`, `data.hourly.temperature_2m[0]`

### WMO Weather Code Mapping

Simplified groupings for Liquid `if/elsif` chains:

| Codes | Symbol | Label |
|-------|--------|-------|
| 0 | ☀ | Clear |
| 1 | 🌤 | Mostly Clear |
| 2 | ⛅ | Partly Cloudy |
| 3 | ☁ | Overcast |
| 45, 48 | 🌫 | Fog |
| 51–55 | 🌦 | Drizzle |
| 61–65 | 🌧 | Rain |
| 66–67 | 🌧 | Freezing Rain |
| 71–77 | ❄ | Snow |
| 80–82 | 🌧 | Showers |
| 85–86 | ❄ | Snow Showers |
| 95–99 | ⛈ | Thunderstorm |

> Note: Emoji rendering on e-ink is inconsistent. Use text labels as fallback. Evaluate
> during local preview with trmnlp — may need to switch to text-only.

---

## UI Design

### Full Screen (800×480, ~410px content height)

Three sections stacked vertically:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│  BOSTON                 ☁ Overcast            38°F                      │
│                         Feels like 31°        Wind 12 mph W             │
│                         Humidity 62%                                    │
│                                                                         │
│  ─── TODAY ──────────────────────────────────────────────────────────   │
│                                                                         │
│  [Highcharts spline line chart — next 24 hours of hourly temps]        │
│  X-axis: hour labels (3am, 6am, 9am, 12pm, 3pm, 6pm, 9pm, now+24h)   │
│  Y-axis: temperature                                                    │
│  Data points labeled with condition symbol at select hours              │
│  Precipitation probability shown as bar series behind temp line         │
│                                                                         │
│  ─── 5-DAY ───────────────────────────────────────────────────────────  │
│                                                                         │
│  Mon ❄ 20%  28° ░░░▓▓▓▓▓▓▓░░░░░░░░░░░░  40°                          │
│  Tue 🌧 80%  32° ░░░░░░░░░▓▓▓▓▓▓░░░░░░  45°                          │
│  Wed ❄ 90%  25° ░░░░▓▓▓▓▓▓▓▓░░░░░░░░░  38°                           │
│  Thu ☀ 5%   22° ░░░░░░░░░░░░▓▓▓░░░░░░  35°                           │
│  Fri ⛅ 10%  30° ░░░░░░░░░░░░▓▓▓▓░░░░░  42°                           │
│                                                                         │
│ ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │
│  [icon]  Weather · Boston                                               │
└─────────────────────────────────────────────────────────────────────────┘
```

**Section breakdown** (approximate pixel heights):
- Current conditions: ~90px
- Hourly chart: ~160px
- 5-day range bars: ~130px
- Title bar: ~30px

**5-day range bar**: CSS-only horizontal bar.
- Track width represents the week's full temp range (coldest low → hottest high across all 5 days)
- Filled segment positioned with `margin-left` % and `width` % calculated from Liquid
- Min temp label left of bar, max temp label right of bar

**Hourly chart**: Highcharts spline.
- Series 1: Temperature (spline line, black, `animation: false`)
- Series 2: Precipitation probability (column chart, gray pattern fill, secondary Y-axis or scaled)
- Show next 24 hours starting from current hour
- X-axis: labels every 3 hours
- Disable mouse tracking, tooltips, legend for e-ink

### Half Horizontal (800×240, ~173px content height)

Space is tight — drop the hourly chart, show current + 5-day compact.

```
┌─────────────────────────────────────────────────────────────────────────┐
│  BOSTON  38°F  ☁ Overcast  Feels 31°  Wind 12mph W  Humidity 62%       │
│  ─────────────────────────────────────────────────────────────────────  │
│  Mon ❄ 20%  28° ░░░▓▓▓▓░░░░░░░░  40°                                  │
│  Tue 🌧 80%  32° ░░░░░░▓▓▓▓░░░░  45°                                  │
│  Wed ❄ 90%  25° ░░░▓▓▓▓▓░░░░░░  38°                                   │
│ ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │
│  [icon]  Weather · Boston                                               │
└─────────────────────────────────────────────────────────────────────────┘
```

### Half Vertical (400×480, ~360px content height, narrow)

Show current + hourly chart (abbreviated) + 3-day forecast.

```
┌──────────────────────────────────────┐
│  BOSTON                              │
│  ☁ Overcast     38°F                 │
│  Feels 31°  Wind 12mph W            │
│  Humidity 62%                        │
│  ─────── TODAY ────────────────────  │
│  [Highcharts spline — 24h temps,     │
│   narrower, fewer x-axis labels]     │
│  ─────── FORECAST ─────────────────  │
│  Mon ❄ 20%  28° ░▓▓▓░░  40°         │
│  Tue 🌧 80%  32° ░░▓▓▓░  45°        │
│  Wed ❄ 90%  25° ░▓▓░░░  38°         │
│ ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │
│  [icon]  Weather                     │
└──────────────────────────────────────┘
```

### Quadrant (400×240, ~173px content height, narrow)

Current conditions only — no room for charts.

```
┌──────────────────────────────────────┐
│  BOSTON                              │
│  ☁ Overcast           38°F           │
│  Feels like 31°F                     │
│  Wind 12 mph W   Humidity 62%        │
│ ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │
│  [icon]  Weather                     │
└──────────────────────────────────────┘
```

---

## Template Strategy

### shared.liquid templates

1. **`weather_current`** — Current conditions block (large temp, condition, feels like, wind, humidity)
   - Params: `current`, `location_name`
   - Includes WMO → label + symbol mapping inline

2. **`weather_hourly_chart`** — Highcharts spline for next 24h
   - Params: `hourly`, `chart_height`
   - Outputs `<div id="hourly-chart">` + `<script>` with Highcharts config
   - Slices hourly arrays to next 24 entries starting from current hour
   - Two series: temperature (spline) + precipitation probability (column, scaled to temp range)

3. **`weather_daily_bars`** — 5-day range bar rows
   - Params: `daily`, `days` (count)
   - Calculates overall min/max across all days in Liquid for bar positioning
   - Each row: day name | condition symbol | precip% | low temp | CSS bar | high temp

4. **`title_bar`** — Standard bottom bar

### Highcharts configuration notes

```javascript
Highcharts.chart('hourly-chart', {
  chart: { type: 'spline', animation: false, backgroundColor: 'transparent' },
  title: { text: null },
  legend: { enabled: false },
  tooltip: { enabled: false },
  plotOptions: { series: { enableMouseTracking: false, animation: false } },
  xAxis: { categories: [...hour_labels...], tickInterval: 3 },
  yAxis: [
    { title: null },  // temperature
    { title: null, min: 0, max: 100, opposite: true }  // precip %
  ],
  series: [
    { name: 'Temp', data: [...temps...], yAxis: 0, color: '#000', lineWidth: 2 },
    { name: 'Precip', type: 'column', data: [...precip...], yAxis: 1,
      color: 'rgba(0,0,0,0.15)', borderWidth: 0 }
  ]
});
```

> Liquid will need to construct the JS arrays by iterating `data.hourly.temperature_2m`
> and outputting comma-separated values inside `[...]`.

### CSS range bar approach

```html
<!-- Overall range: week_min=22, week_max=45 → span=23 -->
<!-- Day: low=28, high=40 → left_pct = (28-22)/23 = 26%, width_pct = (40-28)/23 = 52% -->
<div style="position:relative; height:8px; background:#eee; border-radius:4px;">
  <div style="position:absolute; left:26%; width:52%; height:100%; background:#000; border-radius:4px;"></div>
</div>
```

Liquid computes the percentages using `| minus:` and `| divided_by:` filters.

---

## API Changes from Initial Plan

- Added `hourly=temperature_2m,weather_code,precipitation_probability`
- Changed `daily` to include `precipitation_probability_max` instead of `precipitation_sum`
- Removed `wind_speed_10m_max` from daily (not shown in 5-day to save space)
- `forecast_days=6` (was 5) to ensure we always have 5 full future days

### Updated polling_url

```
https://api.open-meteo.com/v1/forecast?latitude=42.36&longitude=-71.06&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day&hourly=temperature_2m,weather_code,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch&timezone=auto&forecast_days=6
```

---

## settings.yml Plan

```yaml
strategy: polling
polling_verb: get
polling_url: https://api.open-meteo.com/v1/forecast?latitude=42.36&longitude=-71.06&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day&hourly=temperature_2m,weather_code,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch&timezone=auto&forecast_days=6
refresh_interval: 15
name: Weather
custom_fields:
  - keyname: instance_name
    name: Location Name
    field_type: author_bio
    description: Display name for the location (e.g. "Boston")
    github_url: https://github.com/lucaspimentel/trmnl-plugins
    learn_more_url: https://open-meteo.com/
```

---

## Implementation Steps

1. [ ] Create `weather/` directory and all required files
2. [ ] Write `settings.yml` with updated polling URL
3. [ ] Write `fields.txt` documenting `data.current.*`, `data.hourly.*`, `data.daily.*`
4. [ ] Write `shared.liquid`:
   - `weather_current` — large current conditions block
   - `weather_hourly_chart` — Highcharts spline with precip overlay
   - `weather_daily_bars` — CSS range bars for 5-day
   - `title_bar`
5. [ ] Write `full.liquid` (all 3 sections)
6. [ ] Write `half_horizontal.liquid` (current + 3-day bars, no chart)
7. [ ] Write `half_vertical.liquid` (current + chart + 3-day bars)
8. [ ] Write `quadrant.liquid` (current conditions only)
9. [ ] Preview locally with `trmnlp serve` — validate chart rendering, emoji, bar alignment
10. [ ] Update root `README.md`

---

## Open Questions / Risks

- **Emoji on e-ink**: May render poorly at 2-bit grayscale. Evaluate in trmnlp preview;
  fallback to short text labels (Clear, Rain, Snow, etc.) if needed.
- **Highcharts Liquid data injection**: Hourly arrays have 168 values (7 days × 24h).
  Slice to 24 values starting at current hour index in Liquid using `offset` + loop counter.
  This requires a Liquid `for` loop with a break condition — verify TRMNL's Liquid supports `break` or use `limit` on `for`.
- **Wind direction**: Convert degrees → 8 cardinal directions via `if/elsif` chain.
- **Current hour index**: Liquid needs to find which index in `data.hourly.time` matches
  `data.current.time`. This may not be straightforward — may need to hardcode offset
  based on current time or use a fixed 24-slice starting at index 0 of today.

---

## References

- [Open-Meteo Forecast API Docs](https://open-meteo.com/en/docs)
- [TRMNL Design System](https://trmnl.com/framework/docs)
- [TRMNL Chart Docs](https://trmnl.com/framework/docs/chart)
- [TRMNL Plugin Docs](https://docs.usetrmnl.com/go/llms.txt)

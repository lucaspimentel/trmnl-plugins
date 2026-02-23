# Weather Plugin Implementation Plan

## Context

Implementing a new TRMNL weather plugin using the Open-Meteo free API. The UI design is documented in `weather-plan.md`. The plugin shows:
- Current conditions (large, readable from distance)
- 24-hour hourly temperature chart (Highcharts spline + precipitation bars)
- 5-day forecast with CSS range bars (like iOS Weather)

## Files to Create

All under `weather/`:

### 1. `weather/settings.yml`
Copy pattern from `mbta-alerts/settings.yml`. Key differences:
- `polling_url`: Open-Meteo with `current`, `hourly`, `daily` params, `forecast_days=6`
- `name: Weather`
- `refresh_interval: 15`
- No `polling_headers` (Open-Meteo needs no auth)

### 2. `weather/fields.txt`
Document `data.current.*`, `data.hourly.*`, `data.daily.*`, `data.current_units.*`, `data.daily_units.*`.

### 3. `weather/shared.liquid`
Four `{% template %}` blocks:

**`weather_current`** — Current conditions
- Params: `current`, `current_units`, `location_name`
- Large temperature (`value` class for big text), condition label + symbol, feels like, wind (degrees → cardinal), humidity
- WMO code → label/symbol via `if/elsif` chain
- Wind direction → 8 cardinals via `if/elsif` on degree ranges

**`weather_hourly_chart`** — Highcharts spline
- Params: `hourly`, `current_time`, `chart_height`
- `<div id="hourly-chart">` + `<script>` block
- Liquid `for` loop over `hourly.time` with `limit: 24` to find starting offset matching `current_time` hour
- Then iterate 24 entries to emit JS arrays for categories (hour labels), temp data, precip probability data
- Highcharts config: spline + column dual-axis, `animation: false`, `enableMouseTracking: false`
- X-axis labels formatted as `9am`, `12pm`, etc. — parse from ISO time strings in Liquid using `slice`

**`weather_daily_bars`** — 5-day CSS range bars
- Params: `daily`, `num_days`
- First pass: find overall min/max temp across all days (Liquid loop with `assign` comparisons)
- Second pass: render each row with:
  - Day name (parse from `daily.time[i]` using `| date: "%a"`)
  - WMO code → condition symbol
  - Precipitation probability %
  - Low temp label
  - CSS bar: `<div>` track with inner `<div>` positioned via computed `left%` and `width%`
  - High temp label
- Bar math: `left_pct = (low - overall_min) * 100 / range`, `width_pct = (high - low) * 100 / range`

**`title_bar`** — Standard bottom bar
- Icon + "Weather" title

### 4. Layout files

**`weather/full.liquid`** (~410px content)
```liquid
{% render "weather_current", current: data.current, current_units: data.current_units, location_name: trmnl.plugin_settings.instance_name %}
{% render "weather_hourly_chart", hourly: data.hourly, current_time: data.current.time, chart_height: 150 %}
{% render "weather_daily_bars", daily: data.daily, num_days: 5 %}
{% render "title_bar", plugin_name: trmnl.plugin_settings.instance_name %}
```

**`weather/half_horizontal.liquid`** (~173px content)
- Current conditions (compact, single row) + 3-day bars, no chart

**`weather/half_vertical.liquid`** (~360px content)
- Current + hourly chart (shorter) + 3-day bars

**`weather/quadrant.liquid`** (~173px content)
- Current conditions only

### 5. Update `README.md`
Add bullet: `- **[weather](./weather)** - Current weather conditions and forecast from Open-Meteo`

## Implementation Order

1. `settings.yml` + `fields.txt` (scaffolding)
2. `shared.liquid` — `title_bar` template (simple, establishes pattern)
3. `shared.liquid` — `weather_current` template (WMO mapping, wind direction, main display)
4. `shared.liquid` — `weather_daily_bars` template (CSS range bars with Liquid math)
5. `shared.liquid` — `weather_hourly_chart` template (Highcharts, most complex)
6. `full.liquid` → `half_horizontal.liquid` → `half_vertical.liquid` → `quadrant.liquid`
7. `README.md` update

## Technical Challenges

**Hourly array slicing**: Open-Meteo returns 144 hourly entries (6 days × 24). Need to find the current hour's index. Approach: iterate `hourly.time` comparing against `current.time`, capture `forloop.index0` as offset, then use a second loop with `offset: start_index, limit: 24`.

**Liquid math for bar %**: `divided_by` does integer division when both are integers. Multiply by `100` first, then divide by range to get integer percentages. Use `| at_least: 1` to prevent zero-width bars.

**Highcharts JS in Liquid**: Build JS arrays inside `<script>` by looping hourly data and emitting comma-separated values. Hour labels extracted via string `slice` on ISO time strings (`"2026-02-23T14:00"` → slice at 11, length 5 → `"14:00"`, then format to `2pm`).

## Verification

1. Run `trmnlp serve` from `weather/` directory to preview locally
2. Verify all 4 layouts render without errors
3. Check Highcharts chart renders (requires browser preview)
4. Test with `{{ data | json }}` temporarily if data access issues arise
5. Verify CSS range bars align correctly across 5 days

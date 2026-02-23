# Weather Plugin Plan

A TRMNL plugin displaying current weather conditions, a 24-hour temperature chart,
and a 5-day forecast using the [Open-Meteo](https://open-meteo.com/) free API (no key required).

---

## API

Open-Meteo forecast endpoint, Boston (42.36, -71.06), °F, mph:

```
https://api.open-meteo.com/v1/forecast?latitude=42.36&longitude=-71.06&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day&hourly=temperature_2m,weather_code,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch&timezone=auto&forecast_days=6
```

- `current`: temperature, feels like, humidity, precipitation, weather code, wind, is_day
- `hourly`: 144 entries (6 days × 24h) — temp, weather code, precip probability
- `daily`: 6 entries (today + 5 days) — temp max/min, weather code, precip probability max
- `forecast_days=6` ensures we always have 5 full future days

---

## UI Design

### Full Screen (800×480, ~410px content height) — ✅ DONE

```
┌─────────────────────────────────────────────────────────────────────────┐
│  BOSTON                                                                  │
│  Overcast                          [icon]  38°F / 3°C                   │
│  Feels like 31°F / -1°C                                                 │
│  Humidity 62%                                                            │
│  Wind 12 mph W                                                           │
│                                                                          │
│  ─── TODAY ──────────────────────────────────────────────────────────   │
│  [Highcharts spline + precip bars, dual Y-axis: °F left / °C right]    │
│                                                                          │
│  ─── FORECAST ────────────────────────────────────────────────────────  │
│  Mon [icon] 20%  28° / -2°  ░░░▓▓▓▓▓▓▓░░░░  40° / 4°                  │
│  Tue [icon] 80%  32° / 0°   ░░░░░░░░▓▓▓▓░░  45° / 7°                  │
│  Wed [icon] 90%  25° / -4°  ░░░░▓▓▓▓▓░░░░░  38° / 3°                  │
│  Thu [icon] 5%   22° / -6°  ░░░░░░░░░░▓▓▓░  35° / 2°                  │
│  Fri [icon] 10%  30° / -1°  ░░░░░░░░▓▓▓▓░░  42° / 6°                  │
│ ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │
│  [icon]  Weather · Boston                                                │
└─────────────────────────────────────────────────────────────────────────┘
```

Section heights: current ~90px, hourly chart ~150px, forecast ~130px, title bar ~30px.

### Half Horizontal (800×240, ~173px content height) — TODO

Drop hourly chart; show current (compact single row) + 3-day forecast bars.

```
┌─────────────────────────────────────────────────────────────────────────┐
│  BOSTON  38°F / 3°C  [icon] Overcast  Feels 31°F  Wind 12mph W  62%    │
│  ─────────────────────────────────────────────────────────────────────  │
│  Mon [icon] 20%  28° / -2°  ░░░▓▓▓▓░░░░░░  40° / 4°                   │
│  Tue [icon] 80%  32° / 0°   ░░░░░░▓▓▓▓░░░  45° / 7°                   │
│  Wed [icon] 90%  25° / -4°  ░░░▓▓▓▓▓░░░░░  38° / 3°                   │
│ ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │
│  [icon]  Weather · Boston                                                │
└─────────────────────────────────────────────────────────────────────────┘
```

### Half Vertical (400×480, ~360px content height) — TODO

Show current + hourly chart (narrower) + 3-day forecast.

```
┌──────────────────────────────────────┐
│  BOSTON                              │
│  Overcast   [icon]  38°F / 3°C      │
│  Feels 31°F  Wind 12mph W           │
│  Humidity 62%                        │
│  ─────── TODAY ────────────────────  │
│  [Highcharts spline — narrower,      │
│   fewer x-axis labels]              │
│  ─────── FORECAST ─────────────────  │
│  Mon [icon] 20%  28° / -2°  ░▓▓░  40° / 4°  │
│  Tue [icon] 80%  32° / 0°   ░░▓▓░  45° / 7° │
│  Wed [icon] 90%  25° / -4°  ░▓▓░░  38° / 3° │
│ ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │
│  [icon]  Weather                     │
└──────────────────────────────────────┘
```

### Quadrant (400×240, ~173px content height) — TODO

Current conditions only.

```
┌──────────────────────────────────────┐
│  BOSTON                              │
│  Overcast     [icon]  38°F / 3°C    │
│  Feels like 31°F / -1°C             │
│  Wind 12 mph W   Humidity 62%       │
│ ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │
│  [icon]  Weather                     │
└──────────────────────────────────────┘
```

---

## Template Architecture

All logic lives in `shared.liquid` as four `{% template %}` blocks:

| Template | Purpose | Status |
|----------|---------|--------|
| `weather_current` | Current conditions: location, icon, temp °F/°C, feels like, humidity, wind | ✅ Done |
| `weather_hourly_chart` | Highcharts spline (temp) + column (precip %) for next 24h, dual Y-axis °F/°C | ✅ Done |
| `weather_daily_bars` | 5-day CSS range bars with Meteocons icon, precip%, low/high °F/°C | ✅ Done |
| `title_bar` | Standard bottom bar | ✅ Done |

---

## Layout Files

| File | Status | Notes |
|------|--------|-------|
| `src/full.liquid` | ✅ Done | All 3 sections + empty state |
| `src/half_horizontal.liquid` | TODO | Current (compact) + 3-day bars, no chart |
| `src/half_vertical.liquid` | TODO | Current + chart (shorter) + 3-day bars |
| `src/quadrant.liquid` | TODO | Current conditions only |

---

## Implementation Notes

### Data Access

Open-Meteo returns a JSON object. trmnlp injects object keys as **top-level variables** — use
`current`, `hourly`, `daily` directly (not `data.current` etc.). Different from JSON:API array
responses (like MBTA) where the array becomes `data`.

### Temperature Conversion (°F → °C in Liquid)
```liquid
{% assign temp_c = temp_f | minus: 32 | times: 5 | divided_by: 9 | round %}
```
`divided_by` does integer division — always multiply by 100 first if decimals are needed.

### WMO Weather Codes
Mapped to condition labels + Meteocons icon names via `if/elsif` chains. Use `current.is_day`
to pick day/night icon variants.

### Weather Icons
[Meteocons](https://bas.dev/work/meteocons) via CDN (`line` style for clean e-ink rendering):
```
https://bmcdn.nl/assets/weather-icons/v3.0/line/svg/{icon-name}.svg
```

### Highcharts
- Must include CDN script **inside** the template block (trmnlp's `plugins.js` doesn't include it)
- Right °C Y-axis: `linkedTo: 0` + `formatter` function to convert from °F values
- Right margin ≥ 44px to prevent label clipping: `margin: [10, 44, 28, 36]`
- Disable animation, mouse tracking, tooltips, legend for e-ink

### Hourly Slicing
Find the current hour index: compare `hourly.time[i]` (first 13 chars) against `current.time`
(first 13 chars), capture `forloop.index0` as offset, then use `offset: start_index limit: 25`.

### Forecast Bars
Show days 1–5 (skip index 0 = today). Bar position:
```liquid
{% assign left_pct = d_low | minus: overall_min | times: 100 | divided_by: range %}
{% assign width_pct = d_high | minus: d_low | times: 100 | divided_by: range | at_least: 1 %}
```

### Layout Width
The `.layout` CSS class doesn't stretch to fill container width. Use plain `<div style="display:flex; ...width:100%">` for custom layouts.

---

## Remaining TODO

1. `src/half_horizontal.liquid` — compact current row + 3-day bars (no chart)
2. `src/half_vertical.liquid` — current + narrow chart + 3-day bars
3. `src/quadrant.liquid` — current conditions only
4. Preview each new layout with trmnlp + Playwright
5. Consider making location configurable via custom_fields (currently hardcoded Boston in polling URL)

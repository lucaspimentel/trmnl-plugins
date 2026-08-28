# Weather Plugin

Displays current conditions, a 24-hour temperature chart, and a daily forecast
(up to 14 days, capped at 7 when served by Pirate Weather) using a custom
TrmnlApi backend that fetches and normalizes data from either Open-Meteo
(plugin default) or Pirate Weather.

See `README.md` for contributor setup and external dependency details.

## Plugin IDs

- **Prod**: 249564 (checked in at `src/settings.yml`)
- **Staging**: 316595

### Pushing to staging

```bash
bash tools/push-plugin.sh plugins/weather              # lint + push to staging
bash tools/push-plugin.sh plugins/weather --dry-run    # show the overrides, push nothing
```

The script applies these three overrides to `src/settings.yml`, pushes, then restores the file:

1. `id:` → `316595`
2. `polling_url` host → `trmnl-plugins-staging.lucasp.net`
3. `name:` → append ` (staging)` (e.g. `LP Weather (staging)`) so it's distinguishable from prod in the TRMNL UI

The staging id lives in `STAGING_IDS` in `tools/push-plugin.sh` as well as here; keep the two in step.

## API: TrmnlApi

- **Deployed URL**: `https://trmnl-plugins-prod.lucasp.net/api/v2/forecast?place={place}` (the plugin also sends `latitude`/`longitude` as the fallback for a blank `place`). `/api/v1/forecast` is frozen for forked copies of the plugin; never change what a v1 caller can observe
- **Source**: `api/` (repo root)
- **Auth**: None (anonymous)
- **Query params**: `place` (v2, city / postal code / `latitude, longitude` pair), `latitude`, `longitude` (required on v1; on v2 the fallback when `place` is blank), `units` (`imperial` default / `metric`), `hours` (1–25, default 25), `days` (1–14, default 6. Pirate Weather only ever supplies up to 7, so requests for more than 7 return fewer entries than requested when Pirate Weather serves them), `provider` (`open-meteo` / `pirate-weather`), `time_format` (`12h` default / `24h`), `show_place` (`yes` default; `no` omits the `place` block, v2 only); `fake=true` injects random precipitation for testing (**v1 only** - on v2 use `place=test:precipitation`)
- **Test scenarios (v2)**: `place=test:<name>` returns a canned result, so each error the templates can render can be put on screen by typing into the plugin's Place setting - no `settings.yml` edit or push. Names: the five `error.code` values, plus `stale`, `precipitation`, `499`, `500`, `502`. Full table in `api/docs/place-input.md`
- **Provider default**: when `provider` is omitted the API uses the first entry of its `WeatherProviders` app setting. The plugin never sends `provider` (there is no user-facing provider setting), so the server default always applies
- **Fallback**: if the requested provider fails, the API tries the remaining configured providers; `meta.provider` reports who actually served, `meta.requested_provider` who was asked

### Response Shape

v2 adds a `place` block and an `error` object to the v1 shape below. `current`, `hourly`, `daily`,
and `meta` are unchanged.

```json
{
  "place": {
    "name": "Boston",
    "admin1": "Massachusetts",     // geocoder display name, not an ISO code
    "country": "United States",
    "country_code": "US",
    "latitude": 42.36,
    "longitude": -71.06
  }
}
```

`place` is omitted when the request carries `show_place=no`, and when the API's bundled geographic
data has no name for the coordinates at all (mid-ocean, or nowhere near a settlement). A coordinate
pair **does** get a `place` block: everything but `name` comes from the bundled reverse lookup on
every input path.

`admin1` is a short label rather than a display name - `MA`, not `Massachusetts` - so that
`Boston, MA` fits the title bar's 18-character rule where `Boston, Massachusetts` did not. Where the
subdivision code is numeric, as in France and Japan, it falls back to the name (`Nord`, never `59`).

Every failure a device can see comes back as **HTTP 200 with an `error` object** instead of the
forecast, which is what the layouts branch on (`{{ error.message }}` / `{{ error.hint }}`):

```json
{
  "error": {
    "code": "place_not_found",     // stable: branch on this, not on the wording
    "message": "No place matches zzzzqqqq.",
    "hint": "Try adding a state or country, as in Portland, ME."
  }
}
```

`code` is one of `place_missing`, `place_invalid`, `place_not_found`, `request_invalid`,
`weather_unavailable`. Details in `api/docs/place-input.md`.

```json
{
  "current": {
    "time": "2026-02-25T14:00",
    "temperature": 35,
    "apparent_temperature": 28,
    "relative_humidity": 72,
    "precipitation": 0.0,
    "condition": "Overcast",
    "icon_class": "wi-wmo4680-3",
    "wind_speed": 12,
    "wind_direction_deg": 270,
    "wind_direction": "W",
    "is_day": true
  },
  "hourly": {
    "entries": [
      {
        "time": "2026-02-25T14:00",
        "label": "2pm",          // "14:00" when time_format=24h
        "temperature": 35,
        "precipitation_probability": 10,
        "icon_class": "wi-wmo4680-3",
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
        "condition": "Light rain",
        "icon_class": "wi-wmo4680-61",
        "precipitation_probability": 80,
        "sunrise": "2026-02-25T06:30",
        "sunset": "2026-02-25T17:35"
      }
      // ... up to `days` entries (14 max; 7 max when served by Pirate Weather)
    ]
  },
  "meta": {
    "cache": "fresh_fetch",          // fresh_fetch | fresh_hit | stale_served
    "provider": "open-meteo",        // provider that actually served (may differ from requested_provider on fallback)
    "requested_provider": "open-meteo",
    "fetched_at": "2026-02-25T14:00:00+00:00",
    "data_time": "2026-02-25T14:00",
    "served_at": "2026-02-25T14:00:01+00:00",
    "age_seconds": 1,
    "time_format": "12h",            // 12h | 24h
    "upstream": null                  // populated with { status, error } when stale_served or fallback used
  }
}
```

Field names are unit-agnostic; actual units depend on the `units` param (`imperial`: °F, mph / `metric`: °C, km/h).

## Data Access in Templates

TrmnlApi returns a JSON **object** — trmnlp injects top-level keys as top-level variables (not under `data`):

```liquid
{% render "weather_current", current: current %}
{% render "weather_hourly_chart", hourly: hourly, daily: daily, current_time: current.time, chart_height: 230, time_format: meta.time_format %}
{% render "weather_daily_bars_vertical", daily_entries: daily.entries, num_days: daily.entries.size, current_temp: current.temperature %}
```

Key access patterns:
- `error.code` / `error.message` / `error.hint` — every layout renders these instead of the forecast when `error` is present
- `place.name`, `place.admin1` — the matched location, shown in the title bar (`shared.liquid`); absent when Show Location is off
- `current.temperature`, `current.condition`, `current.icon_class`, `current.is_day`
- `hourly.entries` — array of up to 25 entries (current hour + next 24h)
- `daily.entries` — array of up to `days` entries (today + next N-1 days), max 14 (7 when served by Pirate Weather)
- `icon_class` already includes day/night variant (e.g. `wi-day-sunny`) — pre-computed by TrmnlApi; templates prepend the `wi` base class

## Template Architecture

All logic lives in `shared.liquid`, rendered via `{% render %}` from layout files:

| Template | Purpose |
|----------|---------|
| `weather_current` | Current conditions: temp, icon, details (full layout) |
| `weather_current_compact` | Compact current conditions (half/quadrant layouts) |
| `weather_hourly_chart` | Highcharts spline (temp) + areaspline (precip %) with icons on x-axis, sunrise/sunset lines |
| `weather_daily_bars_vertical` | CSS range bars, weather icons, labels inside/outside bar |
| `title_bar` | Bottom bar: plugin name, weather icon, "Updated"/"Cached" timestamp (all four layouts); the provider label is only passed in from `full`/`half_horizontal` |

Every layout passes `time_format: meta.time_format` to `weather_hourly_chart` and `title_bar` so rendered times follow the `time_format` setting.

Daily bars per layout: `full` follows the `days` setting (up to 14, fewer if Pirate Weather serves the response), `half_horizontal` 4, `half_vertical` 5, `quadrant` 3 (no hourly chart) — the latter three are hardcoded to fit their smaller layout space and don't scale with `days`.

`full.liquid` layout structure:

```
[ left (64%)                      | right (36%)             ]
[   weather_current               |                         ]
[   weather_hourly_chart          | weather_daily_bars_vert ]
[           title_bar (full width)                          ]
```

## Key Implementation Notes

**Linter workaround — `font-size` avoidance**: The TRMNL recipe linter (`chef.rb`) counts raw occurrences of `font-size`, `padding`, `margin`, `text-align`, `justify-content`, `background-color`, `border-radius`, `object-fit` across all markup (including `<style>`, `<script>`, comments, variable names). Max allowed: 6 total. Weather icons use `.wi-sz-*` CSS classes defined via the `font:` shorthand (e.g. `font: 110px/1 'weathericons'`) to avoid the `font-size` substring. Non-icon text uses `.fs-10`, `.chart-temp` similarly. In Highcharts JS config, flagged property keys use computed properties (`['mar'+'gin']`, `['pad'+'ding']`).

**Highcharts**: Script tag must be inside the template block (not the layout file).
Three Y-axes: `yAxis[0]` = temp (labels hidden), `yAxis[1]` = precip % 0–100 (hidden), `yAxis[2]` = linked to yAxis[0] (opposite side, labels hidden).
Margin: `[22, 8, 44, 8]` (OG) / `[30, 12, 56, 12]` (X via `isLg` JS flag). Chart height: 230px default in `full.liquid` (200 half_horizontal, 280 half_vertical), overridden via CSS in `full.liquid` to 380px on X (`.screen--lg`) and 300px in portrait (`.screen--portrait`).

**Hourly chart**: Weather icons on x-axis every 4 hours; sunrise/sunset as dashed plotLines from `daily.entries[0].sunrise`/`.sunset`.

**Vertical forecast bars**: Bar widths from overall min/max across all days:
```liquid
{% assign left_pct = d_low | minus: overall_min | times: 100 | divided_by: range %}
{% assign width_pct = d_high | minus: d_low | times: 100 | divided_by: range | at_least: 1 %}
```
Precipitation shows as an umbrella icon (`wi-umbrella`) + percentage under the day label, hidden when chance < 10%.
Temp range bars use `bg--gray-30`.

## Local Preview

```bash
cd plugins/weather
trmnlp serve    # http://localhost:4567
```

To use cached data: configure a `data:` block in `.trmnlp.yml` pointing to a file in `assets/` (e.g. `assets/data-2026-02-24T18-30.json`). The filename encodes the `current.time` value used as "now".

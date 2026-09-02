# Weather Plugin

Displays current conditions, a 24-hour temperature chart, and a daily forecast
(up to 14 days, capped at 7 when served by Pirate Weather) using a custom
TrmnlApi backend that fetches and normalizes data from either Open-Meteo
(plugin default) or Pirate Weather.

See `README.md` for contributor setup and external dependency details.

## Plugin IDs

- **Prod**: 249564 (checked in at `src/settings.yml`)
- **Staging**: 316595
- **v1 fork replica**: 462457 - **frozen, never push to this id again**

### The v1 fork replica (462457)

A copy of the plugin as it was **before** the backend moved off the original host, pushed once on
2026-08-31 from commit **`935da2c`** - the parent of `092ba04` ("Switch Weather plugin backend from
Azure to Railway prod"), which is the last state that still polled `/api/v1/forecast` on the old
host.

It exists to reproduce what users who forked the plugin back then are still running, since they poll
v1 on the old host and cannot be updated by us. Without it, the only way to test their experience
was to guess at it.

Only two fields differ from `935da2c`: `id`, and `name` (`LP Weather (v1 fork replica)`) so it is
not a third indistinguishable "LP Weather" in the UI. Everything else is as it was -
`framework_version: 2.3.7`, `refresh_interval: 30`, the old query string with `provider` and none of
`place`/`country`/`tz`, and the original templates.

**Do not push to it.** Its value is being a fixed point; a push would make it a copy of today's
plugin and quietly delete the only reference for what forks actually run. `tools/push-plugin.sh`
cannot reach it - it only knows the prod and staging ids - so the risk is a hand-run `trmnlp push`
from a directory whose `settings.yml` carries this id. Note `trmnlp lint` reports two issues against
it (an opacity rule, and an `<img>` URL that no longer resolves); those are today's rules judging
old markup and are **not** to be fixed here.

To rebuild it if it is ever lost, extract the plugin at `935da2c`, set `id: 462457` and the name,
and push from a scratch directory - never from the working tree, since `trmnlp push` writes the
server's copy back over the local `settings.yml`.

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
- **Query params**: `place` (v2, city / postal code / `latitude, longitude` pair), `latitude`, `longitude` (required on v1; on v2 the fallback when `place` is blank), `units` (`imperial` default / `metric`), `hours` (1–25, default 25), `days` (1–14, default 6. Pirate Weather only ever supplies up to 7, so requests for more than 7 return fewer entries than requested when Pirate Weather serves them), `provider` (`open-meteo` / `pirate-weather`), `time_format` (`12h` default / `24h`), `show_place` (`yes` default; `no` omits the `place` block, v2 only), `abbreviate_days` (`no` default; `yes` echoes `meta.abbreviate_days: true` so the daily forecast shows `Wed` instead of `Wednesday`, v2 only); `fake=true` injects random precipitation for testing (**v1 only** - on v2 use `place=test:precipitation`)
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
    "abbreviate_days": false,        // v2 only; absent from v1, whose bytes are frozen
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
{% render "weather_daily_bars_vertical", daily_entries: daily.entries, num_days: daily.entries.size, current_temp: current.temperature, abbreviate_days: meta.abbreviate_days %}
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

Daily bars per layout: every layout renders all `days` entries (up to 14, fewer if Pirate Weather serves the response) and a script at the end of `weather_daily_bars_vertical` hides the rows that do not fit. Nothing is hardcoded per layout any more: it measures the column, then hides one row at a time from the bottom and measures again until the last visible row is inside. Today's row is the last to go, since it carries the current-temperature marker, but it does go: a slot too short for even one row used to keep it and leave the marker floating over a row clipped away under the title bar.

The current-conditions block fits the same way, horizontally. `weather_current_fit` (rendered by both current-conditions templates, guarded so one copy runs) measures what the details column may occupy - the block's width less the icon and the temperature, which are `shrink-0` because they are the reading itself - and hides detail lines from the bottom until the widest one left fits. Wind goes first, then humidity, then the feels-like reading; the condition is last. If nothing fits, the whole column goes and the slot shows the icon and the temperature alone. Half a word of "Humidity" reads as a broken screen where an icon and a number reads as a small one. `.current-details` also carries `min-width: 0; overflow: hidden`, so if the script never runs the text clips inside its own column instead of running out over the daily bars.

Both fits and the chart's own floor exist for the same reason: a Fluid Mashup cell owns the size, so a view can land in a slot far smaller than any standalone layout. See `tools/build-mashup-preview.sh`.

Two things that keep the measurement honest, and that will silently break it if removed:

- `.daily-row` carries `shrink-0`. A flex column shrinks its children by default, so without it the rows squash to fit instead of overflowing and there is nothing to measure.
- The fit runs with `justify-content: flex-start` applied inline, then clears it so the column's `flex--evenly` spread returns. Measuring under `space-evenly` would count the distributed gaps against the budget and drop a row that fits.

The same pass sets the column widths. `.day-label` and `.temp-label` have no width in CSS: the script reads what the text needs at whatever type the device resolved, takes the widest across the list, and writes that on every row so the bars stay in one column. The old hardcoded pair (68/34 on OG, 90/44 on X) was tuned to the longest weekday and had already drifted: "Wednesday" measures 92px at 16px type, 2px more than the X box gave it, so the name ran under the weather icon on every X screen showing a Wednesday. Two pixels of slack are added on purpose.

If the bar is left with less than a quarter of the column, the day names fall back to their `data-day-short` form and the widths are measured once more. That is what **Abbreviate Day Names** was shipped to work around ([#1](https://github.com/lucaspimentel/trmnl-plugins/issues/1)), so the setting is now a preference rather than a repair. The swap only ever goes full to short, never back, so a user who asked for short names keeps them on a wide screen. Every pass restores `data-day-full` before measuring, so the choice is re-made from scratch when the screen changes.

The ▼ marker is positioned by the same pass, from the first bar's real rect. It used to sit behind a hand-summed offset (`padding-left:148px` = 68 + 2 + 36 + 2 + 34 + 4 + 1, plus a second copy for `screen--lg`), which measured widths invalidate. The Liquid still renders `left:{{ pct }}%` as a pre-script value, so a JS failure leaves the marker spread across the row rather than pinned to its left edge.

It waits for `window.TRMNL_PLUGINS_READY` before measuring, because the framework's own layout pass moves row heights, and re-runs through `TRMNLPaint.watch` when the screen's classes change. The column also needs `min-height:0` on its flex ancestors (`full.liquid`), or the column grows to fit its content instead of staying in its share, and the budget it reports is its own overflow.

`full.liquid` layout structure:

```
[ left (64%)                      | right (36%)             ]
[   weather_current               |                         ]
[   weather_hourly_chart          | weather_daily_bars_vert ]
[           title_bar (full width)                          ]
```

## Key Implementation Notes

**Linter workaround — `font-size` avoidance**: The TRMNL recipe linter (`chef.rb`) counts raw occurrences of `font-size`, `padding`, `margin`, `text-align`, `justify-content`, `background-color`, `border-radius`, `object-fit` across all markup (including `<style>`, `<script>`, comments, variable names). Max allowed: 6 total. Weather icons use `.wi-sz-*` CSS classes defined via the `font:` shorthand (e.g. `font: 110px/1 'weathericons'`) to avoid the `font-size` substring. Non-icon text uses `.fs-10`, `.chart-temp` similarly. In Highcharts JS config, flagged property keys use computed properties (`['mar'+'gin']`, `['pad'+'ding']`). **The plugin currently spends 0 of the 6**: the four `padding` occurrences that used to be its whole budget were the marker shim, which the measured layout replaced.

**Highcharts**: Script tags must be inside the template block (not the layout file); `pattern-fill.js` is loaded next to `highcharts.js` and is **required**, since on a dithering screen every framework paint comes back as a pattern.
The config is built on **`TRMNLCharts`** (framework 3.2, exported from the plugin runtime), so colors, dither tiles, typography and axis furniture are resolved from the live screen rather than hardcoded. Notes for editing it:

- The whole build runs inside `TRMNLCharts.watch()`, which rebuilds on a device / scale / bit-depth / theme change. The callback **must return the chart instance** or the previous one leaks.
- `TRMNLCharts.merge()` replaces arrays wholesale, so each of the three Y-axes is merged onto `base.yAxis` individually. `yAxis[0]` = temp (labels hidden), `yAxis[1]` = precip % 0–100 (hidden), `yAxis[2]` = linked to `yAxis[0]` (opposite side, labels hidden).
- Do **not** set `chart.events.render`: `TRMNLCharts.options()` installs a hook there that repaints axis strokes as dither patterns, and merging an `events` object would replace it.
- `animation: false` is restated explicitly even though `options()` already sets it. The lint rule greps the markup for the literal and cannot see a runtime value.
- **`TRMNLPaint.px()` does not distinguish OG from TRMNL X.** `--content-scale` resolves to 1 on both; the scale cascade is for user display-scale settings and odd BYOD devices. Device-size differences come from a `screen--lg` class check on the resolved screen, which (unlike a viewport media query) stays correct when the view is only part of the screen.
- Margin: `[22, 8, 44, 8]` (OG) / `[30, 12, 56, 12]` (X), both through `px()`. Chart height stays in Liquid/CSS and is deliberately **not** passed through `px()`: 230px default in `full.liquid` (200 half_horizontal, 280 half_vertical), overridden via CSS to 380px on X (`.screen--lg`) and 300px in portrait (`.screen--portrait`).
- The container id starts with `chart-` (`chart-hourly-<random>`), which is what the framework's `[id^="chart-"] { height: auto; overflow: visible }` rule keys off. `full.liquid` matches the same prefix for its height overrides.

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

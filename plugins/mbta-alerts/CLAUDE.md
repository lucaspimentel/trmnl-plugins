# MBTA Alerts Plugin

Displays current service alerts from the Massachusetts Bay Transportation Authority (MBTA),
filtered to subway and light rail routes, sorted by severity.

## File Structure

```
plugins/mbta-alerts/
  .trmnlp.yml         # trmnlp local dev config
  CLAUDE.md           # this file
  fields.txt          # API response field documentation
  src/
    settings.yml      # plugin config and polling URL (must be in src/)
    shared.liquid     # all reusable templates
    full.liquid
    half_horizontal.liquid
    half_vertical.liquid
    quadrant.liquid
```

## Local Preview

```bash
cd plugins/mbta-alerts
trmnlp serve          # http://localhost:4567
```

## API

MBTA v3 API, no auth required for public endpoints:

```
https://api-v3.mbta.com/alerts?filter[route_type]=0,1&sort=-severity&fields[alert]=service_effect,timeframe,header,updated_at
```

- `filter[route_type]=0,1` — Light Rail (0) and Heavy Rail/Subway (1) only
- `sort=-severity` — most severe first
- Response is a JSON:API array; iterate directly with `{% for alert in data %}`

## Template Notes

- Data is a JSON:API array — access fields via `alert.attributes.field_name`
- Show "No current alerts." when `data` is empty or nil
- Uses `data-list-limit="true"` + `data-list-max-height` to handle overflow gracefully

# MBTA Alerts Plugin

Displays current service alerts from the MBTA, filtered to subway and light rail routes, sorted by severity.

## API

MBTA v3 API, no auth required:

```
https://api-v3.mbta.com/alerts?filter[route_type]=0,1&sort=-severity&fields[alert]=service_effect,timeframe,header,updated_at
```

- `filter[route_type]=0,1` — Light Rail (0) and Heavy Rail/Subway (1) only
- `sort=-severity` — most severe first
- Response is a JSON:API array; iterate with `{% for alert in data %}`
- Fields accessed as `alert.attributes.field_name`

## Template Notes

- Show "No current alerts." when `data` is empty or nil
- Uses `data-list-limit="true"` + `data-list-max-height` to handle overflow gracefully

## Local Preview

```bash
cd plugins/mbta-alerts
trmnlp serve    # http://localhost:4567
```

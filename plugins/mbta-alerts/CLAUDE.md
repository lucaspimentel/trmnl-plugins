# MBTA Alerts Plugin

Displays current service alerts from the MBTA, filtered to subway and light rail routes, sorted by severity.

## Plugin IDs

- **Prod**: 93149 (checked in at `src/settings.yml`)
- **Staging**: 316556

### Pushing to staging

```bash
bash tools/push-plugin.sh plugins/mbta-alerts              # lint + push to staging
bash tools/push-plugin.sh plugins/mbta-alerts --dry-run    # show the overrides, push nothing
```

The script applies these overrides to `src/settings.yml`, pushes, then restores the file:

1. `id:` → `316556`
2. `name:` → append ` (staging)` (e.g. `MBTA Alerts (staging)`) so it's distinguishable from prod in the TRMNL UI

(No `polling_url` swap: this plugin polls the public MBTA API directly, same for prod and staging. The
script's host swap is a no-op here, since the URL never mentions the prod host.)

The staging id lives in `STAGING_IDS` in `tools/push-plugin.sh` as well as here; keep the two in step.

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

- All markup lives in `src/shared.liquid` as two `{% template %}` blocks, `alert_list` and `title_bar`; each layout file computes `latest_update` (the newest `updated_at` across the alerts) and `{% render %}`s the two blocks with a layout-specific `max_height`
- Show "No current alerts." when `data` is empty or nil
- Overflow is handled by the framework, not by hand: `data-list-limit="true"` + `data-list-max-height` on the list, `data-list-hidden-count="true"` for the "and N more" indicator, `data-list-max-columns="1"`, and `data-content-limiter="true"` on each item
- `settings.yml` pins no `framework_version`, so this plugin renders against whatever the platform currently serves

## Local Preview

```bash
cd plugins/mbta-alerts
trmnlp serve    # http://localhost:4567
```

# TRMNL Template Variables Reference

Variables available inside every Liquid template at render time.

---

## `trmnl` Object

### `trmnl.user`

| Variable | Type | Description |
|----------|------|-------------|
| `trmnl.user.name` | string | Full name |
| `trmnl.user.first_name` | string | First name |
| `trmnl.user.last_name` | string | Last name |
| `trmnl.user.locale` | string | Locale code (e.g. `"en"`) |
| `trmnl.user.time_zone` | string | Time zone name (e.g. `"Eastern Time (US & Canada)"`) |
| `trmnl.user.time_zone_iana` | string | IANA tz string (e.g. `"America/New_York"`) |
| `trmnl.user.utc_offset` | string/number | UTC offset |

### `trmnl.device`

| Variable | Type | Description |
|----------|------|-------------|
| `trmnl.device.friendly_id` | string | Short human-readable device ID |
| `trmnl.device.percent_charged` | number | Battery level (0–100) |
| `trmnl.device.wifi_strength` | number | WiFi RSSI signal strength |
| `trmnl.device.width` | number | Display width in pixels (800) |
| `trmnl.device.height` | number | Display height in pixels (480) |

### `trmnl.system`

| Variable | Type | Description |
|----------|------|-------------|
| `trmnl.system.timestamp_utc` | string | Current UTC timestamp at render time |

### `trmnl.plugin_settings`

| Variable | Type | Description |
|----------|------|-------------|
| `trmnl.plugin_settings.instance_name` | string | User-configured label for this plugin instance |
| `trmnl.plugin_settings.<keyname>` | any | Value of any custom field defined in `settings.yml` |

Custom field values are accessed by their `keyname` from `settings.yml`:
```liquid
{{ trmnl.plugin_settings.latitude }}
{{ trmnl.plugin_settings.api_key }}
```

---

## Data Variables

How the API response is exposed depends on the JSON root type:

| Response root | How to access | Example |
|---------------|---------------|---------|
| Array `[{...}]` | `data` is the array | `{% for item in data %}` |
| Object `{...}` | Top-level keys become top-level variables | `{{ current.temperature_2m }}` (not `data.current`) |
| Webhook `{"merge_variables": {...}}` | Keys of `merge_variables` become top-level variables | `{{ my_key }}` |

```liquid
{%- comment -%} Array response (e.g. MBTA alerts) {%- endcomment -%}
{% for item in data %}
  {{ item.attributes.service_effect }}
{% endfor %}

{%- comment -%} Object response (e.g. Open-Meteo — keys injected directly) {%- endcomment -%}
{{ current.temperature_2m }}
{% for temp in hourly.temperature_2m %}...{% endfor %}
```

---

## Variable Scoping

- Variables defined with `{% assign %}` inside a `{% render %}` block are **not** available outside it.
- Pass all data explicitly via render parameters:

```liquid
{% render "my_template", data: data, user: trmnl.user, max_height: 410 %}
```

- `shared.liquid` is prepended to every layout before rendering — variables assigned there **are** available in layout files.

---

## Local Development Overrides (`.trmnlp.yml`)

Override any template variable locally for testing:

```yaml
variables:
  trmnl:
    plugin_settings:
      instance_name: My Plugin Dev
      latitude: "42.36"
      longitude: "-71.06"
    user:
      time_zone_iana: America/New_York
  data:
    some_field: value
```

Custom field values for the `polling_url` are set under `custom_fields`:

```yaml
custom_fields:
  latitude: "42.36"
  longitude: "-71.06"
  api_key: "{{ env.MY_API_KEY }}"   # supports env var interpolation
```

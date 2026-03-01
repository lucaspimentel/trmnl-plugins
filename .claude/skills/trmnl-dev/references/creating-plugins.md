# Creating a New Plugin

## 1. Scaffold with trmnlp (preferred)

If `trmnlp` is available:

```bash
trmnlp init <plugin-name>
```

This creates the standard directory structure under `<plugin-name>/` with `src/` containing
the Liquid files and settings.yml, plus `.trmnlp.yml` for local dev config and `bin/dev`.

Plugins in this repository use the trmnlp `src/` layout: `.trmnlp.yml` at the plugin root,
all liquid files and `settings.yml` under `src/`. Always follow this layout when adding new plugins.

## 2. Manual scaffolding

Create the plugin directory with a `src/` subdirectory, then add these files:

**settings.yml** — configure data fetching:

```yaml
---
strategy: polling
no_screen_padding: 'no'
dark_mode: 'no'
static_data: ''
polling_verb: get
polling_url: https://api.example.com/data
polling_headers: ''
name: My Plugin
refresh_interval: 15
custom_fields:
- keyname: field_key
  name: Display Name
  field_type: author_bio
  description: Short description of the plugin
  github_url: https://github.com/your-org/your-repo
  learn_more_url: https://github.com/your-org/your-repo
```

Key settings:
- `strategy`: `polling` (TRMNL fetches your URL) or `webhook` (you POST to TRMNL)
- `polling_url`: The API endpoint. Use `fields[resource]=field1,field2` to limit response size. Reference custom field values with `##{{ keyname }}` interpolation.
- `refresh_interval`: Minutes between data refreshes
- `polling_headers`: Optional HTTP headers (e.g., for auth)

**Custom fields** let users configure plugin inputs (API keys, locations, preferences) via the TRMNL UI. Each field appears as a form input on the plugin settings page. Reference values in `polling_url` with `##{{ keyname }}`, and in templates with `trmnl.plugin_settings.keyname`.

Available `field_type` values: `author_bio`, `string`, `multi_string`, `text`, `number`, `password`, `boolean`, `date`, `time`, `select`, `time_zone`, `url`, `code`, `copyable`, `copyable_webhook_url`, `plugin_instance_select`.

Example — configurable location for a weather plugin:
```yaml
custom_fields:
- keyname: instance_name
  field_type: author_bio
  name: My Weather
  description: Displays current weather
  github_url: https://github.com/example/plugin
- keyname: latitude
  field_type: string
  name: Latitude
  placeholder: "42.36"
- keyname: longitude
  field_type: string
  name: Longitude
  placeholder: "-71.06"
```
Then in `polling_url`:
```
https://api.example.com/weather?lat=##{{ latitude }}&lon=##{{ longitude }}
```

**shared.liquid** — define reusable template blocks. Always handle the empty/error state
where `data` may be nil or empty:

```liquid
{% template my_content %}
<div class="layout layout--col layout--left layout--top"
     data-list-limit="true"
     data-list-max-height="{{ max_height }}"
     data-list-hidden-count="true"
     data-list-max-columns="1">
  {% if data and data.size > 0 %}
  <!-- render data items here -->
  {% else %}
  <div class="title" data-pixel-perfect="true">No data available.</div>
  {% endif %}
</div>
{% endtemplate %}

{% template title_bar %}
<div class="title_bar">
  <img class="image" src="https://example.com/icon.svg">
  <span class="title">{{ plugin_name }}</span>
</div>
{% endtemplate %}
```

**Layout files** — each renders shared templates with size-appropriate max_height.
Pass API data explicitly via `data: data` (or sub-paths like `current: data.current`):

```liquid
{%- comment -%} full.liquid {%- endcomment -%}
{% render "my_content", data: data, max_height: 410 %}
{% render "title_bar", plugin_name: trmnl.plugin_settings.instance_name %}
```

Typical max_height values: full=410, half_horizontal=173, half_vertical=360, quadrant=173.

Consider using different shared templates for full vs compact layouts when the data presentation
should differ by size (e.g., a detailed view for full/half_vertical, a compact view for
half_horizontal/quadrant).

**fields.txt** — document the data shape for reference:

```
resource.
    attributes.
        field_name_1
        field_name_2
```

## 3. Update README.md

Add a bullet linking to the new plugin directory.

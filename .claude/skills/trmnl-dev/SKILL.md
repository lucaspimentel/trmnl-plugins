---
name: trmnl-dev
description: >
  Full-lifecycle development of TRMNL e-ink display plugins — creating new plugins,
  modifying existing ones, debugging template issues, and previewing locally with trmnlp.
  Use this skill whenever the user mentions TRMNL plugins, Liquid templates for TRMNL,
  e-ink display layouts, trmnlp, or wants to build/modify anything that displays on a
  TRMNL device. Also trigger when the user references plugin files like settings.yml,
  *.liquid templates, or .trmnlp.yml.
---

# TRMNL Plugin Development

## What is TRMNL?

TRMNL is an 800x480 pixel, black-and-white, 2-bit grayscale e-ink display device.
Plugins fetch data from APIs and render it using Liquid templates styled with the
TRMNL design system. Think of each plugin as a small dashboard widget.

## Repository Layout

This repository (`trmnl-plugins`) contains one directory per plugin at the root level.
Each plugin directory holds:

| File | Role |
|------|------|
| `settings.yml` | Plugin metadata, data strategy (polling/webhook), API URL, refresh interval |
| `full.liquid` | Full-screen layout (~410px content height) |
| `half_horizontal.liquid` | Half-screen horizontal (~173px content height) |
| `half_vertical.liquid` | Half-screen vertical (~360px content height) |
| `quadrant.liquid` | Quarter-screen (~173px content height) |
| `shared.liquid` | Reusable template blocks shared across layouts |
| `fields.txt` | Documents the API response fields the plugin uses |

The root `README.md` links to each plugin.

## Creating a New Plugin

### 1. Scaffold with trmnlp (preferred)

If `trmnlp` is available:

```bash
trmnlp init <plugin-name>
```

This creates the standard directory structure under `<plugin-name>/` with `src/` containing
the Liquid files and settings.yml, plus `.trmnlp.yml` for local dev config and `bin/dev`.

If building inside this repository (not using trmnlp's project structure), create files
directly in a new top-level directory — no `src/` subdirectory needed.

### 2. Manual scaffolding

Create the plugin directory and these files:

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
  github_url: https://github.com/lucaspimentel/trmnl-plugins
  learn_more_url: https://github.com/lucaspimentel/trmnl-plugins
```

Key settings:
- `strategy`: `polling` (TRMNL fetches your URL) or `webhook` (you POST to TRMNL)
- `polling_url`: The API endpoint. Use `fields[resource]=field1,field2` to limit response size.
- `refresh_interval`: Minutes between data refreshes
- `polling_headers`: Optional HTTP headers (e.g., for auth)

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

### 3. Update README.md

Add a bullet linking to the new plugin directory.

## Data Access in Templates

For polling plugins, the entire JSON response from `polling_url` is available as the `data` variable.
Every field from the API must be accessed through `data.*` — there are no other top-level variables
for API response content.

**Example**: If the API returns `{"current": {"temperature": 72}, "units": {"temperature": "°F"}}`,
access it as:
- `data.current.temperature` (not `current.temperature`)
- `data.units.temperature` (not `units.temperature`)

**In layout files**, always pass `data` (or sub-paths of `data`) explicitly to shared templates:

```liquid
{% render "my_content", current: data.current, units: data.units, max_height: 410 %}
```

**For JSON:API responses** (like MBTA), the response is an array, so iterate directly:

```liquid
{% for item in data %}
  {{ item.attributes.field_name }}
{% endfor %}
```

**Webhook plugins**: Data sent via `{"merge_variables": {...}}` becomes available as top-level variables.

**Plugin settings**: `trmnl.plugin_settings.instance_name` gives the user-configured instance name.

**Liquid filters**: Standard Shopify Liquid filters work (e.g., `| date: "%b %-d"`, `| upcase`).
TRMNL also provides custom filters via the trmnl-liquid gem.

### Liquid Syntax Notes

TRMNL uses the trmnl-liquid gem (based on Shopify Liquid). Some caveats:
- **`case/when`**: Use `{% if %}` / `{% elsif %}` chains instead of `{% case %}` with `{% when X or Y %}`
  — the `or` syntax inside `when` is non-standard and may not work. Use separate `when` clauses
  or `if/elsif` with `==` comparisons instead.
- **Template variables are scoped**: Variables defined with `{% assign %}` inside a `{% render %}`
  block are not available outside it. Pass data explicitly via render parameters.

## Local Development with trmnlp

[trmnlp](https://github.com/usetrmnl/trmnlp) is a local dev server for previewing plugins.

### Install

```bash
# Via RubyGems (requires Ruby 3.x)
gem install trmnl_preview

# Or via Docker
docker run --publish 4567:4567 --volume "$(pwd):/plugin" trmnl/trmnlp serve
```

### Workflow

```bash
trmnlp init my_plugin     # scaffold a new plugin
cd my_plugin
trmnlp serve              # start local preview at http://localhost:4567
# Edit templates — preview auto-reloads on save

trmnlp login              # authenticate with TRMNL API key
trmnlp push               # upload plugin to your TRMNL device
```

For existing plugins on the TRMNL server:

```bash
trmnlp login
trmnlp clone my_plugin <id>   # download from server
cd my_plugin
trmnlp serve                  # develop locally
trmnlp push                   # upload changes
```

### .trmnlp.yml — Local Dev Config

This file configures the local preview server (not uploaded to TRMNL):

```yaml
---
watch:
  - src
  - .trmnlp.yml

custom_fields:
  api_key: "{{ env.MY_API_KEY }}"   # interpolate environment variables

time_zone: America/New_York

variables:
  trmnl:
    plugin_settings:
      instance_name: My Plugin Dev
```

- `watch`: directories to watch for auto-reload
- `custom_fields`: values for custom fields defined in settings.yml; supports `{{ env.VAR }}` interpolation
- `variables`: override template variables for local testing
- `time_zone`: IANA timezone injected into `trmnl.user`

### trmnlp Project Structure vs Repository Structure

trmnlp expects templates under `src/` with `.trmnlp.yml` at the project root.
This repository stores templates directly in each plugin directory (no `src/`).
When using trmnlp with plugins from this repo, either:
- Run trmnlp from within the plugin directory after adding a `src/` symlink or copying files
- Or restructure the plugin to match trmnlp's expected layout

## Design System Reference

The TRMNL design system provides CSS classes and data attributes optimized for e-ink rendering.
For the full component reference with examples, read `references/design-system.md`.
For live interactive docs, see https://trmnl.com/framework/docs.

Key principles:
- Everything renders at 800x480px in black, white, and 2-bit grayscale
- Use `data-pixel-perfect="true"` on text elements for crisp e-ink rendering
- Use `data-list-limit="true"` with `data-list-max-height` on list containers to handle overflow
- Use `.clamp--N` classes to limit text to N lines
- Use `.item` components for structured list entries with `.meta` and `.content` sub-elements
- Always include a `.title_bar` at the bottom of each layout

## Debugging Tips

- **Blank screen**: Check that `data` is populated — add `{{ data | json }}` temporarily to see raw data
- **Content overflow**: Adjust `data-list-max-height` or add `data-list-limit="true"`
- **Layout issues**: Ensure `.layout` has direction (`.layout--col` or `.layout--row`) and alignment modifiers
- **Stale data**: Check `refresh_interval` in settings.yml; minimum is 1 minute
- **Webhook errors**: Check rate limits (12/hr standard, 30/hr TRMNL+) and payload size (2kb/5kb)

## Webhook Strategy Details

For plugins that push data rather than polling:

```yaml
strategy: webhook
```

- POST to the webhook URL with `{"merge_variables": {"key": "value"}}`
- GET the webhook URL to retrieve current merge variables
- **Merge strategies**: `deep_merge` (combine nested data) or `stream` (append to arrays with `stream_limit`)
- Rate limits: 12/hr standard, 30/hr TRMNL+
- Payload limits: 2kb standard, 5kb TRMNL+

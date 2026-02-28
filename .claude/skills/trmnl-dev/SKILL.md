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

## Plugin Layout

Each plugin is a directory containing:

| File | Role |
|------|------|
| `settings.yml` | Plugin metadata, data strategy (polling/webhook), API URL, refresh interval |
| `full.liquid` | Full-screen layout (~410px content height) |
| `half_horizontal.liquid` | Half-screen horizontal (~173px content height) |
| `half_vertical.liquid` | Half-screen vertical (~360px content height) |
| `quadrant.liquid` | Quarter-screen (~173px content height) |
| `shared.liquid` | Reusable template blocks; prepended to every layout before rendering |
| `fields.txt` | Documents the API response fields the plugin uses |


## Creating a New Plugin

### 1. Scaffold with trmnlp (preferred)

If `trmnlp` is available:

```bash
trmnlp init <plugin-name>
```

This creates the standard directory structure under `<plugin-name>/` with `src/` containing
the Liquid files and settings.yml, plus `.trmnlp.yml` for local dev config and `bin/dev`.

Plugins in this repository use the trmnlp `src/` layout: `.trmnlp.yml` at the plugin root,
all liquid files and `settings.yml` under `src/`. Always follow this layout when adding new plugins.

### 2. Manual scaffolding

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

### 3. Update README.md

Add a bullet linking to the new plugin directory.

## Data Access in Templates

How API data is exposed to templates depends on the shape of the JSON response:

| Response shape | How data is available | Access pattern |
|---|---|---|
| **JSON array** (e.g. MBTA `[{...}, {...}]`) | `data` is the array | `{% for item in data %}` |
| **JSON object** (e.g. Open-Meteo `{"current": {...}}`) | top-level keys injected as top-level variables | `current.temperature_2m` (no `data.` prefix) |
| **Webhook** (`{"merge_variables": {...}}`) | merge_variables keys become top-level variables | `{{ my_key }}` |

**Determining which applies**: Check the API response shape. If the root is `[...]`, use `data`. If the root is `{...}`, the top-level keys are top-level variables — do **not** use `data.*` to access them.

**JSON array example** (MBTA alerts — root is an array):
```liquid
{% for item in data %}
  {{ item.attributes.service_effect }}
{% endfor %}
```

**JSON object example** (Open-Meteo — root is an object with `current`, `hourly`, etc.):
```liquid
{%- comment -%} top-level keys 'current' and 'hourly_units' are injected directly {%- endcomment -%}
{% render "my_content", current: current, units: hourly_units, max_height: 410 %}
```

**In layout files**, always pass data explicitly to shared templates rather than relying on implicit variable inheritance.

**Plugin settings**: `trmnl.plugin_settings.instance_name` gives the user-configured instance name.

**Liquid filters**: Standard Shopify Liquid filters work (e.g., `| date: "%b %-d"`, `| upcase`).
TRMNL also provides custom filters via the trmnl-liquid gem.
For the full Liquid language reference (all filters, tags, operators, types), see `references/liquid.md`.

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

trmnlp login              # authenticate with TRMNL API key (or set $TRMNL_API_KEY env var)
trmnlp push --force       # upload plugin to your TRMNL device (--force skips confirmation prompt)
```

For existing plugins on the TRMNL server:

```bash
trmnlp login
trmnlp clone my_plugin <id>   # download from server
cd my_plugin
trmnlp serve                  # develop locally
trmnlp push --force           # upload changes
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

### trmnlp Project Structure

trmnlp expects the following layout, with `.trmnlp.yml` at the project root and all plugin
files under `src/`:

```
my-plugin/
  .trmnlp.yml
  src/
    settings.yml        ← trmnlp reads this (not the root-level one)
    shared.liquid
    full.liquid
    half_horizontal.liquid
    half_vertical.liquid
    quadrant.liquid
```

**Critical**: trmnlp reads `settings.yml` from `src/settings.yml` (not the plugin root).
If `settings.yml` is in the wrong location, polling will not work — the Poll button will
appear to succeed but all data will be zero/empty.

Plugins in this repository use this layout: `.trmnlp.yml` at the plugin root,
all liquid files and `settings.yml` under `src/`.

### Static vs Live Data in .trmnlp.yml

For development with static sample data, add a `data:` block under `variables:`:

```yaml
variables:
  trmnl:
    plugin_settings:
      instance_name: My Plugin
  data:
    some_field: value
    nested:
      field: value
```

To switch to live API data, **remove the `data:` block entirely**. trmnlp will
automatically poll the `polling_url` from `src/settings.yml` on startup and when
the Poll button is clicked.

**Note**: When switching from static to live data, restart the trmnlp server so it
picks up the updated `.trmnlp.yml` and re-polls the API fresh.

### Killing and Restarting the Server (Windows)

Port 4567 must be free before starting. To kill a stuck server:

```powershell
$conns = Get-NetTCPConnection -LocalPort 4567 -ErrorAction SilentlyContinue
foreach ($c in $conns) { Stop-Process -Id $c.OwningProcess -Force -ErrorAction SilentlyContinue }
```

Then restart from the plugin directory:

```bash
cd my-plugin
trmnlp serve
```

## TRMNL Documentation

When fetching TRMNL docs, append `.md` to any `https://docs.trmnl.com/go/...` URL for a leaner Markdown response:
- `https://docs.trmnl.com/go/private-api/screens` → `https://docs.trmnl.com/go/private-api/screens.md`

For other web pages (e.g. framework docs at `trmnl.com/framework/docs/*`), use `https://r.jina.ai/<url>` to get a clean Markdown version:
- `https://r.jina.ai/https://trmnl.com/framework/docs/label`

For a full table of contents of all 29 TRMNL doc pages, see `references/trmnl-docs-toc.md`.

## References

| File | Contents |
|------|----------|
| `references/trmnl-docs-toc.md` | Full TOC of all TRMNL docs (29 pages) with links and summaries |
| `references/design-system.md` | TRMNL CSS framework — all components, utilities, layout classes |
| `references/template-variables.md` | All Liquid template variables: `trmnl.user`, `trmnl.device`, `trmnl.plugin_settings`, data access patterns |
| `references/settings-yml.md` | Full `settings.yml` schema — all fields, all `field_type` values, polling URL interpolation, strategies, sandbox transform |
| `references/private-api.md` | Full TRMNL REST API reference — all endpoints, auth, request/response formats |
| `references/highcharts.md` | Highcharts config reference for TRMNL — mandatory settings, chart types, axis options, patterns |
| `references/liquid.md` | Full Liquid language reference — all 50 filters, tags, operators, TRMNL custom filters |
| `references/strftime.md` | strftime format code cheat sheet — all codes, padding modifiers, common TRMNL patterns |

For live interactive design system docs: https://trmnl.com/framework/docs
For live layout examples (GitHub Commit Graph, Weather, Stock Price, Reddit, etc.) across 30+ device models: https://trmnl.com/framework/examples

## Plugin Examples

For real-world plugin implementations to use as reference, see the official TRMNL plugins repository: https://github.com/usetrmnl/plugins

Key principles:
- Everything renders at 800x480px in black, white, and 2-bit grayscale
- Use `data-pixel-perfect="true"` on text elements for crisp e-ink rendering
- Use `data-list-limit="true"` with `data-list-max-height` on list containers to handle overflow
- Use `.clamp--N` classes to limit text to N lines
- Use `.item` components for structured list entries with `.meta` and `.content` sub-elements
- Always include a `.title_bar` at the bottom of each layout

## Debugging Tips

- **Blank screen / zero values**: Check that `data` is populated — add `{{ data | json }}` temporarily to see raw data. Zero values usually mean the template is receiving empty structs, not actual API data — check that `settings.yml` is in `src/`.
- **Poll not working / empty data**: Verify `settings.yml` is at `src/settings.yml`. trmnlp reads `src/settings.yml` exclusively — a `settings.yml` at the plugin root will be ignored for polling.
- **Content overflow**: Adjust `data-list-max-height` or add `data-list-limit="true"`
- **Layout not full-width**: The `.layout` class does not automatically stretch to fill its container. Add `style="width:100%"` on the layout div, or use a plain `<div style="display:flex; ...">` for custom layouts.
- **Highcharts not defined**: trmnlp's bundled `plugins.js` does not include Highcharts. Add a `<script src="...highcharts.js"></script>` tag inside the template block that uses it. Avoid `code.highcharts.com` — it rate-limits automated/headless requests (429); self-host the file instead.
- **Highcharts axis labels clipped**: Increase the chart margin on the clipped side, e.g. `margin: [10, 44, 28, 36]` for a right axis.
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

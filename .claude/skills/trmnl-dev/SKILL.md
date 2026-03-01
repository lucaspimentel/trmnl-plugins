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

See `references/creating-plugins.md` for the full guide — scaffolding with trmnlp, manual setup, settings.yml, custom fields, shared.liquid, layout files, and fields.txt.

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

See `references/local-development.md` for the full guide — install, workflow, `.trmnlp.yml` config, project structure, static vs live data, and troubleshooting.

Key points:
- trmnlp reads `settings.yml` from `src/settings.yml` (not the plugin root) — wrong location causes empty data
- Plugins in this repo use: `.trmnlp.yml` at plugin root, all files under `src/`
- `trmnlp serve` starts local preview at `http://localhost:4567`
- `trmnlp push --force` uploads to TRMNL device

## TRMNL Documentation

When fetching TRMNL docs, append `.md` to any `https://docs.trmnl.com/go/...` URL for a leaner Markdown response:
- `https://docs.trmnl.com/go/private-api/screens` → `https://docs.trmnl.com/go/private-api/screens.md`

For other web pages (e.g. framework docs at `trmnl.com/framework/docs/*`), use `https://r.jina.ai/<url>` to get a clean Markdown version:
- `https://r.jina.ai/https://trmnl.com/framework/docs/label`

For a full table of contents of all 29 TRMNL doc pages, see `references/trmnl-docs-toc.md`.

## References

| File | Contents |
|------|----------|
| `references/creating-plugins.md` | Full guide to creating new plugins — scaffolding, settings.yml, custom fields, shared.liquid, layout files |
| `references/local-development.md` | Local dev with trmnlp — install, workflow, .trmnlp.yml config, project structure, static vs live data |
| `references/trmnl-docs-toc.md` | Full TOC of all TRMNL docs (29 pages) with links and summaries |
| `references/design-system.md` | TRMNL CSS framework — all components, utilities, layout classes |
| `references/template-variables.md` | All Liquid template variables: `trmnl.user`, `trmnl.device`, `trmnl.plugin_settings`, data access patterns |
| `references/settings-yml.md` | Full `settings.yml` schema — all fields, all `field_type` values, polling URL interpolation, strategies, sandbox transform |
| `references/private-api.md` | Full TRMNL REST API reference — all endpoints, auth, request/response formats |
| `references/highcharts.md` | Highcharts config reference for TRMNL — mandatory settings, chart types, axis options, patterns |
| `references/liquid.md` | Full Liquid language reference — all 50 filters, tags, operators, TRMNL custom filters |
| `references/strftime.md` | strftime format code cheat sheet — all codes, padding modifiers, common TRMNL patterns |

### Framework Reference (detailed)

When you need specific class names, attributes, or code examples beyond what `design-system.md` covers, read the relevant framework reference file:

| File | Contents | Read when... |
|------|----------|--------------|
| `references/framework/foundation.md` | Screen, View, Layout, Title Bar, Columns, Mashup — hierarchy and structure | Setting up page structure, mashup layouts, or debugging layout hierarchy |
| `references/framework/arrangement.md` | Flex, Grid, Size, Spacing, Gap, Aspect Ratio — layout utilities | Arranging content with flex/grid, sizing elements, spacing |
| `references/framework/styling.md` | Background, Text, Border, Rounded, Outline, Image, Strokes, Scale | Styling colors, borders, images, text readability on e-ink |
| `references/framework/components.md` | Chart, Item, Progress, Rich Text, Table — reusable UI components | Building charts, list items, progress indicators, tables |
| `references/framework/elements.md` | Title, Value, Label, Description, Divider — basic building blocks | Typography choices, text element sizing, dividers |
| `references/framework/modulations.md` | Clamp, Overflow, Fit Value, Format Value, Content Limiter, Pixel Perfect, Table Overflow | Handling text overflow, auto-sizing, number formatting, crisp rendering |
| `references/framework/responsive.md` | Size breakpoints, orientation, bit-depth, visibility/display utilities | Making layouts adapt to different TRMNL devices |
| `references/framework/guides.md` | v2 overview, upgrade guide, enhancement guide, troubleshooting, TRMNL X | Understanding v2 changes, migrating from v1, TRMNL X features |

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

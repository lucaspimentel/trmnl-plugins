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

TRMNL is a family of e-ink display devices. Plugins fetch data from APIs and render
it using Liquid templates styled with the TRMNL design system. Think of each plugin
as a small dashboard widget.

| Device | Resolution | Bit depth | Orientation | Screen classes |
|--------|-----------|-----------|-------------|----------------|
| TRMNL OG | 800x480 | 1-bit (B&W) | Landscape | `screen--ogv2 screen--md screen--1bit` |
| TRMNL X | 1040x780 | 4-bit (16 shades) | Landscape + Portrait | `screen--v2 screen--lg screen--4bit` |

The framework uses responsive prefixes (`md:`, `lg:`, `1bit:`, `4bit:`, `portrait:`) so a
single template can adapt to all devices. See `references/framework/3.3/responsive.md` and
`references/framework/3.3/trmnl_x_guide.md` for details.

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

**Custom fields are NOT readable from templates.** `trmnl.plugin_settings.instance_name` works, but a
custom field defined in `settings.yml` does not resolve under `trmnl.plugin_settings.<keyname>` when the
server renders the plugin (nor under `custom_fields.<keyname>` or `custom_fields_values.<keyname>` —
that last form is the JS sandbox only). It silently renders empty, so a conditional on it always takes
the else branch. Confirmed on a device Aug 2026 (Weather plugin `time_format`); local `trmnlp serve`
behavior was not checked, so verify on a real device rather than in preview.

The custom field's real job is `polling_url` interpolation. To get its value into a template, send it to
your API as a query param and **echo it back in the response body** — response keys are injected as
template variables, so `meta.my_setting` is reliably readable where `trmnl.plugin_settings.my_setting`
is not. If you do not control the API, a `{% assign %}` default in `shared.liquid` is the fallback.

**Liquid filters**: Standard Shopify Liquid filters work (e.g., `| date: "%b %-d"`, `| upcase`).
TRMNL adds its own through the trmnl-liquid gem; those are tabulated in `references/liquid.md`.
For the standard language itself, see <https://shopify.github.io/liquid/>.

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
- `trmnlp push` round-trips the target plugin's server-side settings back into the local `settings.yml`, so check `git diff` after a push for unrelated drift (e.g. a value edited in the TRMNL UI on the pushed-to plugin)

## Where the documentation lives

Most TRMNL documentation is upstream and fetched on demand. Only findings that upstream does
**not** state, or that contradict it, are kept in this repo — a stale local copy is worse than no
copy, because it reads as authoritative.

| Need | Source |
|---|---|
| Platform docs: templating, webhooks, Display/Plugin Data/Account APIs, BYOS | <https://docs.trmnl.com/go/llms-full.txt> — 34 pages in one fetch |
| A single platform page | append `.md` to any `https://docs.trmnl.com/go/...` URL |
| Framework design system | `references/framework/3.3/` — vendored, see below |
| `settings.yml` form fields | <https://help.trmnl.com/en/articles/10513740-custom-plugin-form-builder.md> |
| Liquid engine and TRMNL's own filters | <https://github.com/usetrmnl/trmnl-liquid>, or <https://context7.com/usetrmnl/trmnl-liquid/llms.txt> |
| trmnlp behavior | <https://github.com/usetrmnl/trmnlp> |
| Framework source | <https://github.com/usetrmnl/trmnl-framework> |
| A targeted framework lookup | `https://context7.com/websites/trmnl_framework/llms.txt?topic=<topic>&tokens=<n>` |

Two of these have traps worth knowing. `https://trmnl.com/llms.txt` indexes the framework docs but
advertises `.md` twins that **404** — do not rely on it. And `context7.com/usetrmnl/trmnl-framework`
(the *repo*) indexes only README/CONTRIBUTING/RELEASE, because the design-system pages are
gitignored ERB output; use the `websites/trmnl_framework` entry above instead.

### Framework docs are vendored, not fetched

`references/framework/3.3/` holds 57 pages generated by the framework's own
`rake framework:generate_markdown` and stamped in `SOURCE.md` with the commit they came from. They
are pinned to the docs track the plugins render against (`framework_version` in `src/settings.yml`).

Do not edit them; regenerate instead:

```bash
bash tools/sync-framework-docs.sh --version 3.3
```

Corrections of our own go in `references/framework/updates.md`, which is deliberately a sibling of
the generated directory rather than inside it.

## Non-negotiable gotchas

These cost real debugging time. They are inline here rather than behind a fetch.

- **`settings.yml` must be at `src/settings.yml`.** trmnlp ignores one at the plugin root, and the
  symptom is empty data rather than an error.
- **Liquid filters are NOT applied in `polling_url`.** A `{{ x | split: ',' | first }}` silently
  never runs. Send the raw `{{ keyname }}` and parse it server-side. This bites `lat_lon` in
  particular, where TRMNL's own help article recommends the broken pattern. A filter also puts `: `
  into the YAML scalar, so the line must be quoted or the file will not parse.
- **`select` option values are slugified.** `US - United States` arrives as
  `us_-_united_states`. Put the key first in the option text and parse the leading token server-side.
- **`trmnl.user.*` DOES interpolate in `polling_url`**, unlike the two above — so time zone, locale
  and UTC offset reach the backend without a custom field.
- **Custom fields are NOT readable from templates.** See Data Access above; route the value through
  the API response instead.
- **Flex children that should shrink need `min-width: 0`** — `plugins.js` measures widths before
  layout, so without it they expand to the full container width.
- **The recipe linter counts raw substrings** of `font-size`, `padding`, `margin` etc. across all
  markup including JS and comments — 6 total, maximum. See `references/framework/updates.md`.

## References

Everything here is a finding, a repo convention, or a TRMNL-specific residue that upstream does not
cover. Read the one that matches the task.

| File | Contents |
|------|----------|
| `references/framework/3.3/` | Generated framework docs, 57 pages — one file per utility, component and guide |
| `references/framework/updates.md` | Corrections found by hand that the official docs do not state |
| `references/local-development.md` | trmnlp workflow, `.trmnlp.yml`, static vs live data, troubleshooting |
| `references/creating-plugins.md` | Creating a plugin the way this repo does it |
| `references/settings-yml.md` | `field_type` values and the `polling_url` interpolation traps |
| `references/template-variables.md` | `trmnl.user`, `trmnl.device`, `trmnl.plugin_settings`, data access |
| `references/liquid.md` | TRMNL's custom Liquid filters and the syntax caveats |
| `references/highcharts.md` | What TRMNL requires of a Highcharts config, and what breaks on e-ink |

Live interactive docs: <https://trmnl.com/framework/docs>.
Layout examples across 30+ device models: <https://trmnl.com/framework/examples>.

## Plugin Examples

For real-world plugin implementations to use as reference, see the official TRMNL plugins repository: https://github.com/usetrmnl/plugins

Key principles:
- OG renders at 800x480 (1-bit B&W); X renders at 1040x780 (4-bit, 16 shades) with portrait support
- Use responsive prefixes (`lg:`, `4bit:`, `portrait:`) to adapt layouts for TRMNL X
- Use `data-pixel-perfect="true"` on text elements for crisp e-ink rendering
- Use `data-list-limit="true"` with `data-list-max-height` on list containers to handle overflow
- Use `.clamp--N` classes to limit text to N lines
- Use `.item` components for structured list entries with `.meta` and `.content` sub-elements
- Always include a `.title_bar` at the bottom of each layout

## Debugging Tips

- **Blank screen / zero values**: Check that `data` is populated — add `{{ data | json }}` temporarily to see raw data. Zero values usually mean the template is receiving empty structs, not actual API data — check that `settings.yml` is in `src/`.
- **Poll not working / empty data**: Verify `settings.yml` is at `src/settings.yml`. trmnlp reads `src/settings.yml` exclusively — a `settings.yml` at the plugin root will be ignored for polling.
- **Custom field setting has no effect on the device**: templates cannot read `trmnl.plugin_settings.<keyname>` — it renders empty server-side. Route the value through the API response instead. See **Plugin settings** under Data Access in Templates.
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

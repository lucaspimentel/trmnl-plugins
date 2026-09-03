# settings.yml Reference

`src/settings.yml` defines plugin metadata, data strategy, and custom user-configurable fields.
Changes are overwritten by `trmnlp pull` — edit locally and push with `trmnlp push --force`.

Authority for the form fields: <https://help.trmnl.com/en/articles/10513740-custom-plugin-form-builder.md>
(append `.md` for Markdown). Import/export: <https://help.trmnl.com/en/articles/10542599-importing-and-exporting-private-plugins>

Kept locally because the field-type list drifts and because the `polling_url` section below
**contradicts** the vendor documentation on a point that has already cost us a bug.

---

## Full Schema

```yaml
---
# Data strategy: how TRMNL fetches data for this plugin
strategy: polling           # polling | webhook | static

# Display options
no_screen_padding: 'no'     # 'yes' removes the default outer padding
dark_mode: 'no'             # 'yes' inverts the display

# Polling strategy fields (used when strategy: polling)
polling_verb: get           # get | post
polling_url: ''             # API endpoint URL; supports {{ keyname }} interpolation and Liquid
polling_headers: ''         # HTTP headers, e.g. 'Authorization: Bearer token'
polling_body: ''            # Request body for POST requests

# Static strategy (used when strategy: static)
static_data: ''             # JSON string, e.g. '{"key": "value"}'

# Plugin metadata
name: My Plugin             # Display name on the TRMNL UI
description: ''             # One-line summary; written back by `trmnlp push`
refresh_interval: 15        # Minutes between data refreshes (UI offers 15 | 60 | 360 | 720 | 1440)
id: 12345                   # Plugin ID (assigned by TRMNL, do not set manually)
framework_version: 3.3.1    # Pin the design-system version this plugin renders against
serverless_language:        # Language for a sandbox transform; blank when unused

# User-configurable fields shown on the plugin settings page
custom_fields:
  - keyname: instance_name  # identifier used in polling_url (not readable from templates)
    name: My Plugin         # label shown in the TRMNL UI
    field_type: author_bio  # see field types below
    description: ''         # optional help text shown under the field
    github_url: ''          # link to source/docs shown in TRMNL UI
    learn_more_url: ''      # optional secondary link
    placeholder: ''         # placeholder text for string/number inputs
    options: []             # list of options for select field_type
```

---

## Top-Level Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `strategy` | string | yes | `polling`, `webhook`, or `static` |
| `name` | string | yes | Plugin display name |
| `refresh_interval` | integer | yes | Minutes between refreshes. The UI dropdown offers `15`, `60`, `360`, `720`, `1440`, but any integer set in the file is accepted and pushed — `plugins/mbta-alerts` runs at `30` |
| `no_screen_padding` | `'yes'`/`'no'` | no | Remove outer screen padding. Quotes required. Default: `'no'` |
| `dark_mode` | `'yes'`/`'no'` | no | Invert display colors. Quotes required. Default: `'no'` |
| `id` | integer | no | Assigned by TRMNL — do not set manually |
| `polling_url` | string | polling | API endpoint. Supports `{{ keyname }}` and full Liquid templating |
| `polling_verb` | string | polling | HTTP method: `get` or `post` |
| `polling_headers` | string | no | HTTP headers string, e.g. `'X-API-Key: abc123'` |
| `polling_body` | string | no | Request body for POST requests |
| `static_data` | string | static | JSON string used as data when `strategy: static` |
| `description` | string | no | One-line summary shown in the TRMNL UI. `trmnlp push` writes the server's copy back into the file |
| `framework_version` | string | no | Design-system version to render against, e.g. `3.3.1`. Omit to track whatever the platform currently serves — `plugins/mbta-alerts` does, `plugins/weather` pins it |
| `serverless_language` | string | no | Language for a sandbox transform (see below). Blank when unused |
| `oauth_*` | mixed | no | ~20 keys (`oauth_enabled`, `oauth_authorize_url`, `oauth_token_url`, …) for plugins that authenticate against an OAuth provider. `trmnlp push` writes the full set back into the file even when OAuth is off, so expect them to appear after a push |

---

## `custom_fields` Schema

Each entry in `custom_fields` defines one user-configurable input on the plugin settings page.

| Field | Required | Description |
|-------|----------|-------------|
| `keyname` | yes | Identifier. Used in `polling_url` as `{{ keyname }}`. **Not** readable from a template — see `template-variables.md` |
| `name` | yes | Label shown in TRMNL UI |
| `field_type` | yes | Input type (see below) |
| `description` | no | Help text displayed under the field |
| `github_url` | no | Link to plugin source/docs |
| `learn_more_url` | no | Secondary link |
| `placeholder` | no | Placeholder for text/number inputs |
| `options` | no | List of values for `select` field type |
| `default` | no | Pre-filled value that is submitted if the user leaves the field untouched (all field types). Unlike `placeholder`, which is hint-only and not submitted. |
| `min` | no | Minimum value for `number` fields |
| `max` | no | Maximum value for `number` fields |
| `group` | no | Section heading the field is filed under on the settings page. Fields sharing a value are grouped together, in first-appearance order |
| `help_text` | no | Longer explanation shown below `description`, for the caveats that do not fit a one-liner |
| `optional` | no | `true` lets the user leave the field blank. Fields are required by default |
| `maxlength` | no | Character limit for text inputs |
| `category` | no | On the `author_bio` entry only: comma-separated catalogue categories, e.g. `environment,custom` |
| `step` | no | Decimal grid for `number` fields. Renders as an HTML5 `step` attribute, so an off-grid value (e.g. a 6-decimal coordinate under `step: 0.001`) is rejected as "invalid" with no "too long" wording. Use `step: any` for free-form decimal precision. |

---

## `field_type` Values

| field_type | Input rendered | Value returned | Notes |
|------------|---------------|----------------|-------|
| `author_bio` | Read-only info block | n/a | Used for the first field to show plugin name, description, and links. Not a user-input field. |
| `string` | Single-line text input | string | General purpose text |
| `multi_string` | Multiple text inputs | array of strings | For comma-separated or multiple values |
| `text` | Multi-line textarea | string | For longer text |
| `number` | Numeric input | number | Supports `min`/`max`/`step`/`default`. `step` enforces HTML5 grid validation; use `step: any` to accept arbitrary decimals. |
| `password` | Password input (masked) | string | For API keys, tokens |
| `boolean` | Checkbox | `true`/`false` | |
| `date` | Date picker | date string | |
| `time` | Time picker | time string | |
| `time_zone` | Timezone dropdown | IANA timezone string (e.g. `"America/New_York"`) | |
| `select` | Dropdown | selected option value | Requires `options:` list |
| `url` | URL input | string | Validates as URL |
| `code` | Code editor input | string | For code/JSON inputs |
| `copyable` | Read-only text with copy button | n/a | Display-only; shows a value users can copy |
| `copyable_webhook_url` | Read-only webhook URL with copy button | n/a | Auto-populated with the plugin's webhook URL |
| `plugin_instance_select` | Dropdown of the user's plugin instances | plugin instance ID | For "data only" / Plugin Merge strategy |
| `lat_lon` | Autocomplete over cities, addresses and postal codes | `"lat,lon"` string, e.g. `33.7490,-84.3880` | User may also type coordinates directly. `| split: ',' | first` works here — see `polling_url` interpolation below |
| `xhrSelect` | Dropdown populated from an `endpoint` | selected option value | Single or multi |
| `xhrSelectSearch` | Searchable dropdown populated from an `endpoint` | selected option value | |

### Typical first field pattern

The first `custom_field` is almost always `author_bio` — it renders as a display-only info block showing the plugin name, description, and links:

```yaml
custom_fields:
  - keyname: instance_name
    name: My Weather Plugin
    field_type: author_bio
    description: Shows current weather conditions and forecast.
    github_url: https://github.com/example/plugin
    learn_more_url: https://github.com/example/plugin
```

---

## `polling_url` Interpolation

### `{{ keyname }}` — custom field values

Reference any custom field value using standard Liquid syntax (no `##` prefix):

```
https://api.example.com/data?lat={{ latitude }}&lon={{ longitude }}&key={{ api_key }}
```

### Liquid filters in `polling_url` — they DO run

**Verified 2026-09-02.** `&country={{ country | prepend: 'aq_' }}` was pushed to a staging plugin,
and a real device poll logged the prepended value server-side.

This reference previously claimed the opposite, as did four other files in the repo. The claim rested
on a single experiment that could not support it. A `select` field sent:

```
country={{ country | split: ' - ' | first }}
```

with option text `US - United States of America`, and the whole label arrived server-side. But
**select values are slugified before interpolation**, to `us_-_united_states_of_america`, and the
delimiter ` - ` does not occur in that string. `split` therefore returns a one-element array and
`first` hands the label straight back — the same observable result whether or not the filter ran.
Slugification alone explains it. TRMNL's help article was never recommending a broken pattern.

Two things remain true and are the actual traps:

- **`select` values are slugified**, so write filters against the slug, or put the key first in the
  option text and parse the leading token server-side.
- **A filter puts `: ` into the YAML scalar**, so the line has to be quoted or `settings.yml` will
  not parse at all. Use single quotes for filter arguments inside the double-quoted scalar:
  `"...&c={{ country | prepend: 'x_' }}"`.

So the `lat_lon` pattern TRMNL documents is fine — `lat_lon` is not a select, and its value keeps a
real comma to split on:

```
https://api.example.com/?lat={{ lat_lon | split: ',' | first }}
```

#### Check interpolation with the Parse button, not by deploying

The plugin settings page (`trmnl.com/plugin_settings/<id>/edit`) has a **Parse** button under the
Polling URL box that renders the URL with the current field values substituted. It shows filter
output and slugified select values directly, so it answers "what will actually be sent?" in one
click. It agreed exactly with what the server sent when the two were checked against each other.

Reach for it first. The `country` bug above was one button-press from being diagnosed correctly:
the preview would have read `country=us_-_united_states_of_america`, with the missing ` - ` visible.

**`trmnl.user.*` interpolates too.** `&tz={{ trmnl.user.time_zone_iana }}`
arrives as `tz=America/New_York`, verified on a device. Time zone, locale and UTC offset are
therefore available to a backend without defining a custom field at all.

### Multiple URLs (multi-endpoint polling)

Provide multiple URLs line-separated — TRMNL fetches all and merges results into indexed nodes:

```
https://api.example.com/current
https://api.example.com/forecast
```

Response shape:
```json
{
  "IDX_0": { ...response from first URL },
  "IDX_1": { ...response from second URL }
}
```

### Dynamic URL lists with Liquid

Build URL sets at runtime:

```liquid
{% assign ids = recipe_ids | split: "," %}
{% for id in ids %}https://api.example.com/items/{{ id }}.json
{% endfor %}
```

Use the **Parse** button in the TRMNL UI to test URL generation without waiting for a refresh cycle.

---

## Strategies

### `polling`

TRMNL fetches `polling_url` on every `refresh_interval`. The JSON response is injected into templates:
- Array root `[...]` → available as `data`
- Object root `{...}` → top-level keys become top-level template variables

### `webhook`

You POST data to TRMNL. TRMNL renders templates with the posted `merge_variables`.

```yaml
strategy: webhook
```

POST to: `https://trmnl.com/api/custom_plugins/{PLUGIN_SETTINGS_UUID}`

```json
{ "merge_variables": { "key": "value" } }
```

### `static`

Data is hardcoded in `settings.yml` — no API calls:

```yaml
strategy: static
static_data: '{"message": "Hello World", "count": 42}'
```

---

## Sandbox Transform (advanced)

For payloads >100KB or complex transformations, add a JavaScript transform function.
Runs in Node.js v22, isolated sandbox, 1-second timeout, no network access:

```javascript
function transform(input) {
  return {
    items: input.data.slice(0, 30),
    total: input.data.length
  }
}
```

Access custom field values inside the transform:
```javascript
input.trmnl.plugin_settings.custom_fields_values.my_keyname
```

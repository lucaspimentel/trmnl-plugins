# settings.yml Reference

`src/settings.yml` defines plugin metadata, data strategy, and custom user-configurable fields.
Changes are overwritten by `trmnlp pull` — edit locally and push with `trmnlp push --force`.

Docs: https://help.trmnl.com/en/articles/10542599-importing-and-exporting-private-plugins

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
polling_url: ''             # API endpoint URL; supports ##{{ }} interpolation and Liquid
polling_headers: ''         # HTTP headers, e.g. 'Authorization: Bearer token'
polling_body: ''            # Request body for POST requests

# Static strategy (used when strategy: static)
static_data: ''             # JSON string, e.g. '{"key": "value"}'

# Plugin metadata
name: My Plugin             # Display name on the TRMNL UI
refresh_interval: 15        # Minutes between data refreshes (15 | 60 | 360 | 720 | 1440)
id: 12345                   # Plugin ID (assigned by TRMNL, do not set manually)

# User-configurable fields shown on the plugin settings page
custom_fields:
  - keyname: instance_name  # identifier used in templates and polling_url
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
| `refresh_interval` | integer | yes | Minutes between refreshes. Valid: `15`, `60`, `360`, `720`, `1440` |
| `no_screen_padding` | `'yes'`/`'no'` | no | Remove outer screen padding. Quotes required. Default: `'no'` |
| `dark_mode` | `'yes'`/`'no'` | no | Invert display colors. Quotes required. Default: `'no'` |
| `id` | integer | no | Assigned by TRMNL — do not set manually |
| `polling_url` | string | polling | API endpoint. Supports `##{{ keyname }}` and full Liquid templating |
| `polling_verb` | string | polling | HTTP method: `get` or `post` |
| `polling_headers` | string | no | HTTP headers string, e.g. `'X-API-Key: abc123'` |
| `polling_body` | string | no | Request body for POST requests |
| `static_data` | string | static | JSON string used as data when `strategy: static` |

---

## `custom_fields` Schema

Each entry in `custom_fields` defines one user-configurable input on the plugin settings page.

| Field | Required | Description |
|-------|----------|-------------|
| `keyname` | yes | Identifier. Used in `polling_url` as `##{{ keyname }}` and in templates as `trmnl.plugin_settings.keyname` |
| `name` | yes | Label shown in TRMNL UI |
| `field_type` | yes | Input type (see below) |
| `description` | no | Help text displayed under the field |
| `github_url` | no | Link to plugin source/docs |
| `learn_more_url` | no | Secondary link |
| `placeholder` | no | Placeholder for text/number inputs |
| `options` | no | List of values for `select` field type |

---

## `field_type` Values

| field_type | Input rendered | Value returned | Notes |
|------------|---------------|----------------|-------|
| `author_bio` | Read-only info block | n/a | Used for the first field to show plugin name, description, and links. Not a user-input field. |
| `string` | Single-line text input | string | General purpose text |
| `multi_string` | Multiple text inputs | array of strings | For comma-separated or multiple values |
| `text` | Multi-line textarea | string | For longer text |
| `number` | Numeric input | number | |
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

### `##{{ keyname }}` — custom field values

Reference any custom field value using double-hash Liquid syntax:

```
https://api.example.com/data?lat=##{{ latitude }}&lon=##{{ longitude }}&key=##{{ api_key }}
```

### Liquid filters in `polling_url`

Full Liquid is supported inside `##{{ }}`:

```
https://api.example.com/posts.json?since=##{{ "now" | date: "%Y-%m-%d" }}
```

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
{% for id in ids %}https://api.example.com/items/##{{ id }}.json
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

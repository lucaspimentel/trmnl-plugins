# Liquid in TRMNL

TRMNL renders templates with the [trmnl-liquid gem](https://github.com/usetrmnl/trmnl-liquid), which
is Shopify Liquid plus the custom filters and the `{% template %}` tag below. Only those additions
are documented here; the standard language lives upstream.

---

## TRMNL Custom Filters

These filters are provided by the `trmnl-liquid` gem in addition to standard Liquid filters. Some require optional Rails/i18n dependencies.

### Number Formatting

| Filter | Description | Example |
|--------|-------------|---------|
| `number_with_delimiter` | Formats a number with thousands separators | `{{ 1337 \| number_with_delimiter }}` → `1,337` |
| `number_with_delimiter: ",", "."` | Custom delimiter and decimal separator | `{{ 1337.5 \| number_with_delimiter: ".", "," }}` → `1.337,5` |
| `number_to_currency` | Formats as currency with unit, delimiter, separator, precision | `{{ 9.99 \| number_to_currency }}` → `$9.99` |
| `number_to_currency: "$"` | Explicit currency unit | `{{ 9.99 \| number_to_currency: "€" }}` → `€9.99` |
| `map_to_i` | Maps all items in a collection to integers | `{{ prices \| map_to_i }}` |

`number_to_currency` signature: `number_to_currency(number, unit_or_locale="$", delimiter=",", separator=".", precision=2)`

### Date / Time

| Filter | Description | Example |
|--------|-------------|---------|
| `days_ago: N` | Returns the date N days ago (in given timezone) | `{{ 7 \| days_ago }}` → date 7 days ago; `{{ 7 \| days_ago: "America/New_York" }}` |
| `l_date: format, locale` | Localizes a date with format and locale | `{{ article.date \| l_date: "%B %-d", "en" }}` → `January 5` |
| `ordinalize: format` | Formats a date with ordinal day via `<<ordinal_day>>` placeholder | `{{ date \| ordinalize: "<<ordinal_day>> of %B" }}` → `5th of January` |

`l_date` accepts either strftime format strings (containing `%`) or i18n format symbols. Requires `rails-i18n` + `trmnl-i18n` for full locale support.

### Text

| Filter | Description | Example |
|--------|-------------|---------|
| `markdown_to_html` | Renders Markdown as HTML | `{{ body \| markdown_to_html }}` |
| `pluralize: count` | Pluralizes a word based on count | `{{ "item" \| pluralize: count }}` → `item` or `items` |
| `pluralize: count, plural: "..."` | Custom plural form | `{{ "goose" \| pluralize: count, plural: "geese" }}` |
| `l_word: locale` | Localizes a custom plugin word via i18n | `{{ "alerts" \| l_word: "en" }}` |

### Collections

| Filter | Description | Example |
|--------|-------------|---------|
| `group_by: "key"` | Groups array of objects by a property | `{{ items \| group_by: "category" }}` → hash keyed by category |
| `find_by: "key", "value"` | Finds first object where `key == value` | `{{ items \| find_by: "id", "42" }}` |
| `find_by: "key", "value", fallback` | Same, with fallback if not found | `{{ items \| find_by: "id", "42", default_item }}` |
| `where_exp: "var", "expression"` | Filters array by a Liquid expression | `{{ items \| where_exp: "item", "item.score > 50" }}` |
| `sample` | Returns a random element from an array | `{{ quotes \| sample }}` |

### Utilities

| Filter | Description | Example |
|--------|-------------|---------|
| `json` | Serializes a value to JSON string | `{{ data \| json }}` |
| `parse_json` | Parses a JSON string into an object | `{{ json_string \| parse_json }}` |
| `append_random` | Appends a random 4-character hex suffix | `{{ "cache-key" \| append_random }}` → `cache-key3f2a` |
| `qr_code` | Generates an SVG QR code | `{{ url \| qr_code }}` |
| `qr_code: size, level` | QR code with custom module size and error level (`l`/`m`/`q`/`h`) | `{{ url \| qr_code: 8, "m" }}` |

### TRMNL Template Tag

TRMNL adds `{% template %}` / `{% endtemplate %}` for defining reusable blocks, used with `{% render %}`:

```liquid
{% template my_block %}
  <div class="layout layout--col">
    {{ content }}
  </div>
{% endtemplate %}

{% render "my_block", content: data.title %}
```

Variables are **scoped**: data passed to `{% render %}` is available inside the block, but parent-scope variables are not. Always pass data explicitly.

---
## The standard language

Everything else — tags, operators, types, whitespace control and the ~50 stock filters — is
ordinary Shopify Liquid and is documented at <https://shopify.github.io/liquid/>. It was
transcribed here once; the copy added nothing and could only go stale.

Two caveats that are **not** in those docs, learned here:

- **`case`/`when`**: use `{% if %}` / `{% elsif %}` chains instead. `{% when X or Y %}` is
  non-standard and may not work; separate `when` clauses or `==` comparisons do.
- **Scoping**: a variable `{% assign %}`ed inside a `{% render %}` block is not visible outside it.
  Pass data explicitly through render parameters.

---

## Common Patterns

**Null-safe data check:**
```liquid
{% if data and data.size > 0 %}
  {% for item in data %}...{% endfor %}
{% else %}
  No data available.
{% endif %}
```

**Safe string with fallback:**
```liquid
{{ item.title | default: "Untitled" | truncate: 40 }}
```

**Building a URL from parts:**
```liquid
{{ base_url | append: "/path" | append: ".html" }}
```

**Sorted, deduped list:**
```liquid
{% assign tags = product.tags | split: "," | sort_natural | uniq %}
{% for tag in tags %}{{ tag }}{% endfor %}
```

**Filter array by property:**
```liquid
{% assign available = products | where: "available" %}
{% assign shirts = products | where: "type", "shirt" | first %}
```

**Date formatting for TRMNL:**
```liquid
{{ updated_at | date: "%b %-d, %I:%M %p" }}  → Jan 5, 3:42 PM
```

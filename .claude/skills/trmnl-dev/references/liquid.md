# Liquid Template Language Reference

Source: https://shopify.github.io/liquid/

Liquid is a safe, customer-facing template language. TRMNL uses the [trmnl-liquid gem](https://github.com/usetrmnl/trmnl-liquid) (based on Shopify Liquid), which adds custom filters and the `{% template %}` tag.

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

## Syntax

| Syntax | Purpose |
|--------|---------|
| `{{ variable }}` | Output — renders a value |
| `{% tag %}` | Logic — control flow, iteration, variable assignment |
| `{{ value \| filter }}` | Filter — modifies output |

Filters chain left-to-right: `{{ value | filter1 | filter2 }}`

---

## Types

| Type | Description |
|------|-------------|
| `String` | `"hello"` or `'hello'` |
| `Number` | Integer or float: `42`, `3.14` |
| `Boolean` | `true` or `false` |
| `Nil` | Empty value; renders as empty string, treated as falsy |
| `Array` | List of values; cannot be created directly, only via `split` or API data |
| `EmptyDrop` | Returned when accessing a deleted/non-existent object |

---

## Operators

| Operator | Meaning |
|----------|---------|
| `==` | equals |
| `!=` | does not equal |
| `>` | greater than |
| `<` | less than |
| `>=` | greater than or equal to |
| `<=` | less than or equal to |
| `and` | logical AND |
| `or` | logical OR |
| `contains` | substring in string, or string in array of strings |

**Order of operations**: `and`/`or` are evaluated right-to-left. Parentheses are not supported.

```liquid
{% if a == 1 or b == 2 and c == 3 %}
{%- comment -%} evaluates as: a==1 OR (b==2 AND c==3) {%- endcomment -%}
```

**`contains`** works on strings and string arrays only — not arrays of objects:
```liquid
{% if product.title contains "Pack" %}
{% if product.tags contains "sale" %}
```

---

## Truthy / Falsy

- **Falsy**: `nil`, `false`
- **Truthy**: everything else, including `0`, `""`, and empty arrays

---

## Tags

### Control Flow

**`if` / `elsif` / `else`**
```liquid
{% if condition %}
  ...
{% elsif other_condition %}
  ...
{% else %}
  ...
{% endif %}
```

**`unless`** — inverse of `if`:
```liquid
{% unless product.title == "Awesome Shoes" %}
  These shoes are not awesome.
{% endunless %}
```

**`case` / `when`** — switch statement:
```liquid
{% case handle %}
  {% when "cake" %}
    This is a cake
  {% when "cookie", "biscuit" %}
    This is a cookie
  {% else %}
    This is not a cake nor a cookie
{% endcase %}
```
> **TRMNL caveat**: `{% when X or Y %}` syntax may not work — use comma-separated values or `if/elsif` chains instead.

---

### Iteration

**`for`** — loop over array or range:
```liquid
{% for product in collection.products %}
  {{ product.title }}
{% endfor %}

{% for i in (1..5) %}
  {{ i }}
{% endfor %}
```

Parameters:
- `limit:N` — stop after N iterations: `{% for i in array limit:3 %}`
- `offset:N` — start at index N: `{% for i in array offset:2 %}`
- `reversed` — iterate in reverse: `{% for i in array reversed %}`

**`else`** inside `for` — renders when the array is empty:
```liquid
{% for product in collection.products %}
  {{ product.title }}
{% else %}
  The collection is empty.
{% endfor %}
```

**`break`** / **`continue`**:
```liquid
{% for i in (1..5) %}
  {% if i == 4 %}{% break %}{% endif %}
{% endfor %}
```

**Loop variables** (available inside `for`):
- `forloop.index` — 1-based current index
- `forloop.index0` — 0-based current index
- `forloop.first` — true on first iteration
- `forloop.last` — true on last iteration
- `forloop.length` — total number of iterations

**`cycle`** — cycles through a list of strings:
```liquid
{% cycle "odd", "even" %}
```

**`tablerow`** — generates `<tr>/<td>` HTML:
```liquid
<table>
{% tablerow product in collection.products cols:2 %}
  {{ product.title }}
{% endtablerow %}
</table>
```

---

### Variable

**`assign`** — create a variable:
```liquid
{% assign my_string = "Hello" %}
{% assign my_bool = true %}
```

**`capture`** — assign a block of rendered content to a variable:
```liquid
{% capture my_variable %}
  Hello {{ name }}!
{% endcapture %}
{{ my_variable }}
```

**`increment`** / **`decrement`** — create and output a counter (independent of `assign` variables):
```liquid
{% increment my_counter %}  → 0
{% increment my_counter %}  → 1
{% decrement my_counter %}  → -1
```

---

### Template

**`comment`** — suppress output:
```liquid
{% comment %} This won't render {% endcomment %}
{%- comment -%} With whitespace stripping {%- endcomment -%}
```

**`raw`** — disable Liquid processing (useful for JavaScript templates):
```liquid
{% raw %}
  {{ this will not be processed }}
{% endraw %}
```

**`liquid`** — write multiple tags without repeated `{% %}` delimiters:
```liquid
{% liquid
  assign username = "Alice"
  if username == "Alice"
    echo "Hello, Alice!"
  endif
%}
```

**`echo`** — output a value inside a `liquid` tag (supports filters):
```liquid
{% liquid
  echo product.title | upcase
%}
```

**`render`** — include another template with isolated variable scope:
```liquid
{% render "snippet-name" %}
{% render "snippet-name", variable: value %}
{% render "snippet-name" with object %}
{% render "snippet-name" for array as item %}
```
Variables from the parent template are **not** available inside `render`. Pass data explicitly.

**`include`** *(deprecated)* — like `render` but shares variable scope. Avoid in new code.

---

## Whitespace Control

Add `-` inside delimiters to strip whitespace/newlines:
```liquid
{%- if condition -%}
  content
{%- endif -%}

{{- variable -}}
```

- `{%-` strips whitespace before the tag
- `-%}` strips whitespace after the tag

---

## Filters — Complete Reference

Filters are applied with the pipe `|` operator. Multiple filters chain left-to-right.

### String Filters

| Filter | Description | Example |
|--------|-------------|---------|
| `append` | Appends a string to the end | `{{ "/url" \| append: ".html" }}` → `/url.html` |
| `prepend` | Adds a string to the beginning | `{{ "/index.html" \| prepend: "example.com" }}` → `example.com/index.html` |
| `capitalize` | Capitalizes first character, lowercases rest | `{{ "my TITLE" \| capitalize }}` → `My title` |
| `upcase` | Converts all characters to uppercase | `{{ "hello" \| upcase }}` → `HELLO` |
| `downcase` | Converts all characters to lowercase | `{{ "HELLO" \| downcase }}` → `hello` |
| `remove` | Removes all occurrences of substring | `{{ "I strained" \| remove: "rain" }}` → `I sted` |
| `remove_first` | Removes only the first occurrence | `{{ "rain rain" \| remove_first: "rain" }}` → ` rain` |
| `remove_last` | Removes only the last occurrence | `{{ "rain rain" \| remove_last: "rain" }}` → `rain ` |
| `replace` | Replaces all occurrences | `{{ "my my" \| replace: "my", "your" }}` → `your your` |
| `replace_first` | Replaces only the first occurrence | `{{ "my my" \| replace_first: "my", "your" }}` → `your my` |
| `replace_last` | Replaces only the last occurrence | `{{ "my my" \| replace_last: "my", "your" }}` → `my your` |
| `strip` | Removes whitespace from both ends | `{{ "  hello  " \| strip }}` → `hello` |
| `lstrip` | Removes whitespace from the left | `{{ "  hello  " \| lstrip }}` → `hello  ` |
| `rstrip` | Removes whitespace from the right | `{{ "  hello  " \| rstrip }}` → `  hello` |
| `strip_html` | Removes all HTML tags | `{{ "<em>hi</em>" \| strip_html }}` → `hi` |
| `strip_newlines` | Removes all newline characters | Multi-line → single line |
| `newline_to_br` | Inserts `<br />` before each newline | Useful for displaying user text in HTML |
| `truncate` | Shortens to N characters (ellipsis counts toward total) | `{{ "Hello World" \| truncate: 8 }}` → `Hello...` |
| `truncatewords` | Shortens to N words | `{{ "one two three" \| truncatewords: 2 }}` → `one two...` |
| `escape` | Converts `<`, `>`, `&`, `'` to HTML entities | `{{ "<p>" \| escape }}` → `&lt;p&gt;` |
| `escape_once` | Escapes HTML without double-escaping already-escaped content | Safe to apply multiple times |
| `url_encode` | Percent-encodes URL-unsafe characters; spaces become `+` | `{{ "a b" \| url_encode }}` → `a+b` |
| `url_decode` | Decodes a URL-encoded string | `{{ "a+b" \| url_decode }}` → `a b` |
| `split` | Splits a string into an array by delimiter | `{{ "a,b,c" \| split: "," }}` → `["a","b","c"]` |
| `slice` | Extracts substring or array items starting at index | `{{ "Liquid" \| slice: 0, 3 }}` → `Liq`; supports negative index |
| `size` | Returns character count (string) or item count (array) | `{{ "hello" \| size }}` → `5` |

`truncate` / `truncatewords` accept an optional second argument to replace the ellipsis:
```liquid
{{ "Ground control to Major Tom." | truncate: 25, ", and so on" }}  → Ground control, and so on
{{ "Ground control to Major Tom." | truncatewords: 3, "--" }}        → Ground control to--
{{ "Ground control to Major Tom." | truncatewords: 3, "" }}          → Ground control to
```

---

### Number / Math Filters

| Filter | Description | Example |
|--------|-------------|---------|
| `abs` | Absolute value | `{{ -17 \| abs }}` → `17` |
| `ceil` | Round up to nearest integer | `{{ 1.2 \| ceil }}` → `2` |
| `floor` | Round down to nearest integer | `{{ 1.9 \| floor }}` → `1` |
| `round` | Round to nearest integer or N decimal places | `{{ 1.5 \| round }}` → `2`; `{{ 183.357 \| round: 2 }}` → `183.36` |
| `plus` | Addition | `{{ 4 \| plus: 2 }}` → `6` |
| `minus` | Subtraction | `{{ 4 \| minus: 2 }}` → `2` |
| `times` | Multiplication | `{{ 3 \| times: 2 }}` → `6` |
| `divided_by` | Division; integer divisor rounds down, float preserves decimals | `{{ 5 \| divided_by: 3 }}` → `1`; `{{ 5 \| divided_by: 3.0 }}` → `1.666...` |
| `modulo` | Remainder after division | `{{ 7 \| modulo: 3 }}` → `1` |
| `at_least` | Clamps to a minimum value | `{{ 4 \| at_least: 5 }}` → `5` |
| `at_most` | Clamps to a maximum value | `{{ 4 \| at_most: 3 }}` → `3` |

---

### Array Filters

| Filter | Description | Example |
|--------|-------------|---------|
| `first` | Returns the first item | `{{ array \| first }}` or `array.first` |
| `last` | Returns the last item | `{{ array \| last }}` or `array.last` |
| `size` | Returns the number of items | `{{ array \| size }}` or `array.size` |
| `join` | Joins array into a string with a separator | `{{ array \| join: ", " }}` |
| `reverse` | Reverses array order | `{{ array \| reverse }}` |
| `sort` | Sorts case-sensitively (uppercase before lowercase) | `{{ array \| sort }}` or `{{ array \| sort: "property" }}` |
| `sort_natural` | Sorts case-insensitively | `{{ array \| sort_natural }}` or `{{ array \| sort_natural: "property" }}` |
| `map` | Extracts a property value from each object in array | `{{ products \| map: "title" }}` |
| `where` | Filters array to objects matching a property value | `{{ products \| where: "type", "kitchen" }}`; omit value to match truthy |
| `uniq` | Removes duplicate items | `{{ array \| uniq }}` |
| `compact` | Removes `nil` values from array | `{{ array \| map: "category" \| compact }}` |
| `concat` | Merges two arrays | `{{ fruits \| concat: vegetables }}` |
| `sum` | Sums all numeric values, or a named property across objects | `{{ array \| sum }}` or `{{ products \| sum: "quantity" }}` |
| `slice` | Returns a subset starting at index with optional length | `{{ array \| slice: 1, 2 }}` |

---

### Date Filter

| Filter | Description |
|--------|-------------|
| `date` | Formats a timestamp using [strftime](https://strftime.net/) format strings |

```liquid
{{ article.published_at | date: "%a, %b %d, %Y" }}   → Fri, Jul 17, 2015
{{ "March 14, 2016" | date: "%b %d, %y" }}             → Mar 14, 16
{{ "now" | date: "%Y-%m-%d %H:%M" }}                   → current date/time at render
```

Common format codes:
| Code | Output |
|------|--------|
| `%Y` | 4-digit year (2024) |
| `%y` | 2-digit year (24) |
| `%m` | Month number (01–12) |
| `%b` | Abbreviated month (Jan) |
| `%B` | Full month (January) |
| `%d` | Day of month (01–31) |
| `%-d` | Day without leading zero (1–31) |
| `%H` | 24-hour hour (00–23) |
| `%I` | 12-hour hour (01–12) |
| `%M` | Minutes (00–59) |
| `%p` | AM/PM |
| `%a` | Abbreviated weekday (Mon) |
| `%A` | Full weekday (Monday) |

---

### Default Filter

```liquid
{{ variable | default: fallback }}
```

Returns `fallback` when the variable is `nil`, `false`, or empty (`""`).

```liquid
{{ product_price | default: 2.99 }}  → 2.99 (if nil/false/empty)

{%- comment -%} Preserve false values explicitly: {%- endcomment -%}
{{ display_price | default: true, allow_false: true }}
```

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

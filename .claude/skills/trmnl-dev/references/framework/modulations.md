# Modulations

Runtime engines that transform content: clamping, overflow, fitting, formatting, content limiting, pixel-perfect rendering, and table overflow.

Source docs: [Clamp](https://trmnl.com/framework/docs/clamp) | [Overflow](https://trmnl.com/framework/docs/overflow) | [Content Limiter](https://trmnl.com/framework/docs/content_limiter) | [Fit Value](https://trmnl.com/framework/docs/fit_value) | [Format Value](https://trmnl.com/framework/docs/format_value) | [Pixel Perfect](https://trmnl.com/framework/docs/pixel_perfect) | [Table Overflow](https://trmnl.com/framework/docs/table_overflow) | [Framework Runtime](https://trmnl.com/framework/docs/framework_runtime)

## Framework Runtime

The runtime (`plugins.js`) fills the screen optimally by executing engines in this order:

1. **Clamp** — truncate text to N lines
2. **Overflow** — plan 1..N columns, duplicate group headers, optional "and N more"
3. **Value Formatting** — format numbers with locale
4. **Fit Value** — adjust font size/weight/line-height to fit container
5. **Grid Gaps** — tweak CSS gaps for integer pixel widths
6. **Column Gaps** — normalize gaps between `.column` elements
7. **Pixel-Perfect Fonts** — wrap lines in spans, enforce even/odd widths (skipped on higher bit-depth)
8. **Content Limiter** — constrain height by view type, add `content--small` and clamp
9. **Index Widths** — ensure item index badges render at even widths

## Clamp

Truncates text to N lines with word-based ellipsis.

```html
<span class="description" data-clamp="2">Long text...</span>

<!-- Responsive -->
<span class="description" data-clamp="2" data-clamp-md="4" data-clamp-portrait="1">...</span>
```

**Responsive attributes:** `data-clamp`, `data-clamp-sm`, `data-clamp-md`, `data-clamp-lg`, `data-clamp-portrait`, `data-clamp-sm-portrait`, `data-clamp-md-portrait`, `data-clamp-lg-portrait`

**Legacy classes (still supported):** `clamp--none`, `clamp--1` through `clamp--50`

v2 change: Title, Label, and Description are unclamped by default. Add `data-clamp` explicitly.

## Overflow

Distributes items across columns, manages height budget, shows "and N more".

```html
<div class="columns" data-overflow="true" data-overflow-max-cols="3"
     data-overflow-max-height="410" data-overflow-counter="true">
  <div class="column">
    <span class="label label--medium group-header" data-group-header="true">Today</span>
    <!-- items -->
  </div>
</div>
```

| Attribute | Default | Purpose |
|---|---|---|
| `data-overflow="true"` | false | Enable overflow |
| `data-overflow-max-height="N"` | auto | Height budget in px (or `"auto"`) |
| `data-overflow-counter="true"` | false | Show "and N more" |
| `data-overflow-max-cols="N"` | unset | Max columns (best-fit) |
| `data-overflow-cols="N"` | unset | Force exact column count |
| `data-group-header="true"` | — | Mark as group header (never orphaned) |

**Responsive suffixes:** Append `-sm`, `-md`, `-lg`, `-portrait`, `-sm-portrait`, `-md-portrait`, `-lg-portrait` to attribute names.

**Legacy (still supported):** `data-list-limit`, `data-list-max-columns`, `data-list-max-height`, `data-list-hidden-count`

## Content Limiter

Auto-restricts content height by view type.

```html
<div class="content" data-content-limiter="true">...</div>
<div class="content" data-content-limiter="true" data-content-max-height="140">...</div>
```

When content exceeds threshold: applies `content--small` class and clamps the first overflowing block. Subsequent blocks are hidden.

## Fit Value

Auto-adjusts font size, weight, and line height so values fit their containers.

```html
<!-- Numeric values: auto-fits within container width -->
<span class="value value--xxxlarge" data-value-fit="true">$1,000</span>

<!-- Text content: needs explicit max-height -->
<span class="value value--large" data-value-fit="true" data-value-fit-max-height="80">Long text</span>
```

| Attribute | Purpose |
|---|---|
| `data-value-fit="true"` | Enable auto-sizing |
| `data-value-fit-max-height="N"` | Max height in px (required for non-numeric text) |

## Format Value

Automatically formats numeric values with locale-aware number formatting.

```html
<span class="value value--xlarge value--tnums" data-value-format="true">2345678</span>
<span class="value" data-value-format="true" data-value-locale="de-DE">€123456.78</span>
```

| Attribute | Purpose |
|---|---|
| `data-value-format="true"` | Enable formatting |
| `data-value-locale="en-US"` | Regional format (default: en-US) |

Supported currencies: $, EUR, GBP, JPY, and many others. Handles abbreviations (K, M, B) and dynamic precision.

**Common locales:** `en-US` (123,456.78), `de-DE` (123.456,78), `fr-FR` (123 456,78)

## Pixel Perfect

Aligns text to the pixel grid for crisp 1-bit e-ink rendering. Without this, anti-aliased gray pixels at character edges get forced to black or white, causing randomly bold/distorted text.

```html
<span class="title" data-pixel-perfect="true">Crisp Title</span>
```

Works by measuring parent width, breaking text into lines, wrapping each in a span, and adjusting widths to align to the pixel grid. Only works on text elements at designed font sizes.

## Table Overflow

Constrains table height and adds "and X more" row for hidden entries.

```html
<table class="table" data-table-limit="true" data-table-max-height="auto">...</table>
```

| Attribute | Purpose |
|---|---|
| `data-table-limit="true"` | Enable overflow handling |
| `data-table-max-height` | Max height in px or `"auto"` (inherit from parent) |

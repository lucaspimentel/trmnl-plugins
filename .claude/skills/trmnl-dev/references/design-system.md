# TRMNL Design System Reference

Full interactive docs: https://trmnl.com/framework/docs

Device: 800x480px, black & white, 2-bit grayscale e-ink display.

---

## Table of Contents

### Guides
| Page | URL | Summary |
|------|-----|---------|
| Framework v2 Overview | [/framework/docs/v2_overview](https://trmnl.com/framework/docs/v2_overview) | Open-source e-ink adaptive front-end framework; new utilities (Scale, Visibility, Aspect Ratio, Rounded, Divider, Progress), enhanced Clamp/Overflow/Content Limiter engines, breaking changes to Border classes and unclamped typography by default |
| Upgrade Guide | [/framework/docs/upgrade_guide](https://trmnl.com/framework/docs/upgrade_guide) | Two key migration steps: update border classes for multi-bit-depth support, and explicitly add `data-clamp` attributes since clamping is no longer automatic in v2 |
| Enhancement Guide | [/framework/docs/enhancement_guide](https://trmnl.com/framework/docs/enhancement_guide) | How to apply responsive prefixes for device size/bit-depth/orientation adaptation, and leverage new utilities like Aspect Ratio, Rounded, and Progress |
| Troubleshooting Guide | [/framework/docs/troubleshooting_guide](https://trmnl.com/framework/docs/troubleshooting_guide) | Checklist for common issues from v2's stricter markup requirements: layout structure, unsupported nesting, and text clamping |
| TRMNL X Guide | [/framework/docs/trmnl_x_guide](https://trmnl.com/framework/docs/trmnl_x_guide) | Enhancements for the TRMNL X larger 4-bit e-ink display: `--base` modifier, expanded typography sizes, container query units, improved layout controls |

### Foundation
| Page | URL | Summary |
|------|-----|---------|
| Structure | [/framework/docs/structure](https://trmnl.com/framework/docs/structure) | Fixed div hierarchy: Screen → (Mashup →) View → Layout + optional Title Bar; covers single-view and multi-view (mashup) code examples |
| Screen | [/framework/docs/screen](https://trmnl.com/framework/docs/screen) | Outermost root container; configure device dimensions, orientation (`screen--portrait`), color depth, padding, dark mode via classes like `screen--og`, `screen--dark-mode` |
| View | [/framework/docs/view](https://trmnl.com/framework/docs/view) | Holds plugin content; size modifiers: `view--full` (800×480), `view--half_horizontal` (800×240), `view--half_vertical` (400×480), `view--quadrant` (400×240) |
| Layout | [/framework/docs/layout](https://trmnl.com/framework/docs/layout) | Exactly one per View; content container with direction (`layout--row`/`--col`), alignment, and stretch modifiers; interior content uses Flex/Grid/Columns |
| Title Bar | [/framework/docs/title_bar](https://trmnl.com/framework/docs/title_bar) | Sibling to Layout inside a View; displays icon, title, and optional instance label; auto-compacts in Mashup contexts |
| Columns | [/framework/docs/columns](https://trmnl.com/framework/docs/columns) | Distributes same-type data across auto-balanced columns with overflow handling and group preservation; use over Grid/Flex when dealing with variable-length lists |
| Mashup | [/framework/docs/mashup](https://trmnl.com/framework/docs/mashup) | Arranges multiple plugin Views in one screen; 7 layout configurations: `1Lx1R`, `1Tx1B`, `1Lx2R`, `2Lx1R`, `2Tx1B`, `1Tx2B`, `2x2` |

### Utilities
| Page | URL | Summary |
|------|-----|---------|
| Size | [/framework/docs/size](https://trmnl.com/framework/docs/size) | Width/height utility classes (`w--N`, `h--N`); fixed sizes, arbitrary pixel values, dynamic sizing, container query units, min/max constraints, responsive variants |
| Spacing | [/framework/docs/spacing](https://trmnl.com/framework/docs/spacing) | Margin/padding utilities (`m--{size}`, `mt--{size}`, `p--{size}`, `pt--{size}`); fixed and decimal values, responsive modifiers |
| Gap | [/framework/docs/gap](https://trmnl.com/framework/docs/gap) | CSS gap between flex/grid children; predefined sizes, arbitrary values, distribution modifiers (`gap--space-between`), responsive variants |
| Flex | [/framework/docs/flex](https://trmnl.com/framework/docs/flex) | Flexbox containers (`.flex`, `.flex--row`, `.flex--col`); alignment, stretch, wrapping, item-level flex properties; use over Grid for content-sized arrangements |
| Grid | [/framework/docs/grid](https://trmnl.com/framework/docs/grid) | Grid layouts (`.grid`, `.grid--cols-N`); column counts, spans, row-based layouts, positioning, responsive wrapping; use over Flex for strict alignment |
| Background | [/framework/docs/background](https://trmnl.com/framework/docs/background) | Dithered grayscale fills via repeating pixel patterns; `bg--black`, `bg--gray-10` through `bg--gray-75`, `bg--white`; legacy `bg--gray-1`–`bg--gray-7` still work |
| Border | [/framework/docs/border](https://trmnl.com/framework/docs/border) | Horizontal/vertical border patterns (`border--h-{n}`, `border--v-{n}`); v2 breaking change: new class names supporting 1/2/4-bit color spaces |
| Rounded | [/framework/docs/rounded](https://trmnl.com/framework/docs/rounded) | Predefined border radius values; size variants, corner-specific rounding, custom values, responsive breakpoints |
| Outline | [/framework/docs/outline](https://trmnl.com/framework/docs/outline) | Pixel-perfect rounded borders via CSS `border-image` (9-slice); adapts across bit depths — `border-image` on 1-bit, standard CSS borders on 2/4-bit; screen backdrop modifier for Mashup |
| Visibility | [/framework/docs/visibility](https://trmnl.com/framework/docs/visibility) | Show/hide elements by display bit depth (`hidden`, `visible`, `flex`, `grid`); responsive prefixes for screen sizes and bit-depth variants (monochrome to 16-shade grayscale) |
| Responsive | [/framework/docs/responsive](https://trmnl.com/framework/docs/responsive) | Three complementary approaches: size-based breakpoints (`-sm`, `-md`, `-lg`), orientation (`-portrait`), and bit-depth variants; can be combined (e.g. `-md-portrait`) |
| Responsive Test | [/framework/docs/responsive_test](https://trmnl.com/framework/docs/responsive_test) | Beta testing interface comparing SCSS mixins with CSS utility classes across screen sizes, orientations, and bit depths to verify equivalent styling results |
| Text | [/framework/docs/text](https://trmnl.com/framework/docs/text) | Text color via dither patterns, alignment options, responsive variants across screen sizes, orientations, and display bit depths |
| Image | [/framework/docs/image](https://trmnl.com/framework/docs/image) | Optimize images for 1-bit displays using dithering; control display via object-fit options (fill, contain, cover) |
| Image Stroke | [/framework/docs/image_stroke](https://trmnl.com/framework/docs/image_stroke) | Customizable outlines on vector/transparent raster images; preset widths (small→xlarge), white or black color options for contrast on shaded backgrounds |
| Text Stroke | [/framework/docs/text_stroke](https://trmnl.com/framework/docs/text_stroke) | Outlined text for readability on shaded backgrounds; works only on pure black or white text; preset sizes (small→xlarge) and shade options |
| Scale | [/framework/docs/scale](https://trmnl.com/framework/docs/scale) | Scale the entire interface via a UI scale factor; six predefined levels (75%–150%) to adjust content density for different viewing distances |
| Aspect Ratio | [/framework/docs/aspect_ratio](https://trmnl.com/framework/docs/aspect_ratio) | CSS-based predefined aspect ratio classes; ten options from square (1:1) to ultra-widescreen (21:9) |

### Modulations
| Page | URL | Summary |
|------|-----|---------|
| Overflow | [/framework/docs/overflow](https://trmnl.com/framework/docs/overflow) | Distributes items across columns, manages height budgets, shows "and N more"; `data-overflow`, `data-overflow-max-cols`, `data-overflow-counter`, group header handling, responsive variants, legacy `data-list-*` support |
| Table Overflow | [/framework/docs/table_overflow](https://trmnl.com/framework/docs/table_overflow) | Constrains table height with `data-table-limit="true"` and `data-table-max-height`; appends "and X more" for hidden rows |
| Clamp | [/framework/docs/clamp](https://trmnl.com/framework/docs/clamp) | Truncates text to N lines using word-based ellipsis via `data-clamp="N"`; responsive variants (`data-clamp-md`, `-portrait`, etc.); legacy `.clamp--N` classes |
| Format Value | [/framework/docs/format_value](https://trmnl.com/framework/docs/format_value) | Automatically formats numeric values for readability; handles currency symbols with regional variations and locale-specific number formatting |
| Fit Value | [/framework/docs/fit_value](https://trmnl.com/framework/docs/fit_value) | Auto-adjusts font size, weight, and line height so values fit their containers; requires `data-max-height` for text content |
| Content Limiter | [/framework/docs/content_limiter](https://trmnl.com/framework/docs/content_limiter) | Auto-restricts content height by view type via `data-content-limiter="true"`; adds `content--small` class and clamps first overflowing block; `data-content-max-height` for custom threshold |
| Pixel Perfect | [/framework/docs/pixel_perfect](https://trmnl.com/framework/docs/pixel_perfect) | Aligns text precisely to the pixel grid via `data-pixel-perfect="true"`; eliminates anti-aliasing artifacts critical for 1-bit e-ink rendering |
| Framework Runtime | [/framework/docs/framework_runtime](https://trmnl.com/framework/docs/framework_runtime) | Automatically measures space and sequentially applies: text clamping → column overflow planning → value formatting → font sizing → gap normalization → pixel-perfect rendering |

### Elements
| Page | URL | Summary |
|------|-----|---------|
| Title | [/framework/docs/title](https://trmnl.com/framework/docs/title) | Heading text; five size variants (small, base, large, xlarge, xxlarge); responsive and orientation-based styling; unclamped by default in v2 |
| Value | [/framework/docs/value](https://trmnl.com/framework/docs/value) | Numerical/textual values; twelve size variants (XXSmall→Peta), tabular numbers (`value--tnums`), responsive breakpoints |
| Label | [/framework/docs/label](https://trmnl.com/framework/docs/label) | Tags/badges; style variants (default, outline, underline, gray, inverted); overflow handling; responsive and bit-depth modifiers; unclamped by default in v2 |
| Description | [/framework/docs/description](https://trmnl.com/framework/docs/description) | Descriptive body text; four size variants (base, large, xlarge, xxlarge); wrapping/clamping options; responsive breakpoints; unclamped by default in v2 |
| Divider | [/framework/docs/divider](https://trmnl.com/framework/docs/divider) | Horizontal or vertical dividers; auto-detects background type (white, light, dark, black) for optimal contrast |

### Components
| Page | URL | Summary |
|------|-----|---------|
| Rich Text | [/framework/docs/rich_text](https://trmnl.com/framework/docs/rich_text) | Formatted paragraph container; alignment options (left, center, right), six size variants, integrates with Content Limiter and Pixel Perfect |
| Item | [/framework/docs/item](https://trmnl.com/framework/docs/item) | Flexible list entry container; variants: with meta, with emphasis levels (1–3), with index, simple, with icon; used for lists, schedules, ordered content |
| Table | [/framework/docs/table](https://trmnl.com/framework/docs/table) | Data tables optimized for 1-bit rendering; size options, indexing, built-in overflow and clamping engines |
| Chart | [/framework/docs/chart](https://trmnl.com/framework/docs/chart) | Data visualizations via Highcharts/Chartkick; line, multi-series, bar, and gauge chart types; animation must be disabled for TRMNL screenshot capture |
| Progress | [/framework/docs/progress](https://trmnl.com/framework/docs/progress) | Two component types: progress bars (continuous, customizable size/emphasis) and progress dots (discrete stages with filled/current/empty states) |

---

## Structure

Hierarchy: **Screen → (Mashup →) View → Layout + Title Bar**

```html
<!-- Single view (BYOS/trmnlp) -->
<div class="view view--full">
  <div class="layout layout--col">
    <!-- content -->
  </div>
  <div class="title_bar">...</div>
</div>

<!-- Mashup (multiple views) -->
<div class="screen">
  <div class="mashup mashup--1Lx1R">
    <div class="view view--half_vertical">
      <div class="layout">...</div>
    </div>
    <div class="view view--half_vertical">
      <div class="layout">...</div>
    </div>
  </div>
</div>
```

On the TRMNL Platform, `screen`/`view` wrappers are injected automatically. With trmnlp (BYOS), include the full hierarchy.

---

## View

| Class | Dimensions |
|-------|-----------|
| `view--full` | 800×480 |
| `view--half_horizontal` | 800×240 |
| `view--half_vertical` | 400×480 |
| `view--quadrant` | 400×240 |

---

## Layout

Exactly one `layout` per `view`. Use flex/grid/columns inside — do not nest layouts.

```html
<div class="layout layout--col layout--left layout--top">
  <!-- content -->
</div>
```

**Direction:** `layout--row` | `layout--col`

**Horizontal alignment:** `layout--left` | `layout--center-x` | `layout--right`

**Vertical alignment:** `layout--top` | `layout--center-y` | `layout--bottom`

**Combined:** `layout--center` (both axes)

**Stretch (container):** `layout--stretch` | `layout--stretch-x` | `layout--stretch-y`

**Stretch (child):** `stretch-x` | `stretch-y`

---

## Title Bar

Sibling of `layout` inside a `view`. Auto-compacts in mashup contexts.

```html
<div class="title_bar">
  <img class="image" src="https://example.com/icon.svg">
  <span class="title">Plugin Name</span>
  <span class="instance">Instance Label</span>  <!-- optional -->
</div>
```

---

## Item

```html
<div class="item item--emphasis-2">
  <div class="meta"><span class="index">1</span></div>  <!-- optional -->
  <div class="icon"><img src="icon.svg" class="w--[6cqw] h--[6cqw]"></div>  <!-- optional -->
  <div class="content">
    <span class="title title--small">Heading</span>
    <span class="description">Body text</span>
    <span class="label label--small">Tag</span>
  </div>
</div>
```

**Emphasis:** `item--emphasis-1` (default/lightest) | `item--emphasis-2` | `item--emphasis-3` (darkest)

**Meta sub-elements:** `.meta` (colored sidebar) | `.meta > .index` (numbered index)

---

## Typography

> **Important — always prefer built-in CSS classes over custom `font-size` overrides.**
> Each class uses a font specifically optimized for e-ink at its native size. Overriding `font-size` on these elements — especially `.description` (NicoPups, a bitmap font designed for 16px) — breaks pixel-perfect rendering on the e-ink display. If you need a different size, pick the closest built-in class instead of adding a `font-size` style.

### Value classes (Inter font)

| Class | Size | Weight |
|-------|------|--------|
| `.value--xxsmall` | 16px | — |
| `.value--xsmall` | 20px | 600 |
| `.value--small` | 26px | 500 |
| `.value` | 38px | 450 |
| `.value--large` | 58px | 400 |
| `.value--xlarge` | 74px | — |
| `.value--xxlarge` | 96px | — |
| `.value--xxxlarge` | 128px | 300 |

Use `.value--tnums` for tabular (monospaced) numbers.

### Label classes (NicoClean font)

Docs: https://trmnl.com/framework/docs/label

**Size variants:** `.label--small` | `.label` (base) | `.label--large` | `.label--xlarge` | `.label--xxlarge`

**Style variants:** `.label` (plain) | `.label--outline` (bordered) | `.label--underline` | `.label--gray` (muted) | `.label--inverted` (high contrast)

Size and style can be combined: `class="label label--large label--outline"`.

Supports `data-clamp="N"` for truncation. Responsive prefixes: `sm:`, `md:`, `lg:`, `portrait:`, bit-depth.

Legacy: `.label--gray-out` still works but is deprecated — use `.label--gray`.

### Description classes (NicoPups font — bitmap, designed for 16px)

Docs: https://trmnl.com/framework/docs/description

**Size variants:** `.description` (base, 16px) | `.description--large` | `.description--xlarge` | `.description--xxlarge`

> **NicoPups is a bitmap font.** It only renders correctly at its native 16px size. Do not apply `font-size` overrides to `.description` elements — use a `.value--*` class (Inter) instead if you need a different size. The larger description variants (large/xlarge/xxlarge) are designed to work at their respective sizes.

Supports `data-clamp="N"` for truncation. Responsive prefixes: `sm:`, `md:`, `lg:`, `portrait:`.

### Title classes

- `.title` / `.title--small` / `.title--large` / `.title--xlarge` / `.title--xxlarge`

### Rich text

- `.rich-text` — formatted paragraph container

> **v2 note:** `title`, `label`, and `description` are unclamped by default. Apply `data-clamp="N"` explicitly if needed.

### Fit Value

Docs: https://trmnl.com/framework/docs/fit_value

Automatically adjusts font size, weight, and line height so value elements fit their containers. Useful for large dynamic numbers (e.g. temperatures, prices) that may vary in digit count.

```html
<!-- Numeric values: auto-fits within container width -->
<span class="value value--xxxlarge" data-value-fit="true">1,234</span>

<!-- Text content: needs explicit max-height constraint -->
<span class="value value--large" data-value-fit="true" data-value-fit-max-height="80">Long text</span>
```

| Attribute | Purpose |
|-----------|---------|
| `data-value-fit="true"` | Enable auto-sizing |
| `data-value-fit-max-height="N"` | Max height in px (required for non-numeric text) |

---

## Text Utilities

Docs: https://trmnl.com/framework/docs/text

**Color:** `text--black`, `text--gray-10` through `text--gray-75`, `text--white` — uses dither patterns to simulate grayscale on 1-bit displays.

**Alignment:** `text--left` | `text--center` | `text--right` | `text--justify`

Responsive prefixes supported: `sm:`, `md:`, `lg:`, `portrait:`, bit-depth (`1bit:`, `2bit:`, `4bit:`).

---

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
|-----------|---------|---------|
| `data-overflow="true"` | false | Enable overflow handling |
| `data-overflow-max-height="N"` | auto | Height budget in px (or `"auto"`) |
| `data-overflow-counter="true"` | false | Show "and N more" |
| `data-overflow-max-cols="N"` | unset | Max columns (best-fit) |
| `data-overflow-cols="N"` | unset | Force exact column count |
| `data-group-header="true"` | — | Mark element as group header (never orphaned) |

Responsive variants: append `-sm`, `-md`, `-lg`, `-portrait`, or `-md-portrait` to attribute names.

**Legacy (still supported):** `data-list-limit`, `data-list-max-columns`, `data-list-max-height`, `data-list-hidden-count`

---

## Clamp

```html
<span class="description" data-clamp="2">Long text...</span>

<!-- Responsive -->
<span class="description" data-clamp="2" data-clamp-md="4" data-clamp-portrait="1">
  Long text...
</span>

<!-- Legacy -->
<span class="description clamp--2">Long text...</span>
```

---

## Content Limiter

Auto-shrinks font when content overflows its container.

```html
<div class="content" data-content-limiter="true">...</div>
<div class="content" data-content-limiter="true" data-content-max-height="140">...</div>
```

Adds `content--small` class and applies Clamp on the first overflowing block.

---

## Pixel Perfect

Docs: https://trmnl.com/framework/docs/pixel_perfect

Aligns text to the pixel grid for crisp 1-bit rendering. Without this, anti-aliased gray pixels at character edges get forced to black or white on the e-ink display, causing text to appear randomly bold or distorted.

Works by measuring the parent element's width, breaking text into individual lines, wrapping each in a span, and adjusting widths to align to the pixel grid. Also normalizes cross-platform browser rendering differences.

```html
<span class="title" data-pixel-perfect="true">Crisp Title</span>
```

**Constraints:** Only works on text elements. Depends on pixel fonts rendered at their designed sizes — another reason not to override `font-size` on `.description` or `.label` elements.

---

## Background

Dithered grayscale fills via repeating pixel patterns.

```html
<div class="bg--black">...</div>
<div class="bg--gray-10">...</div>   <!-- lightest gray -->
<div class="bg--gray-50">...</div>
<div class="bg--white">...</div>
```

Available: `bg--black`, `bg--gray-10`, `bg--gray-15`, `bg--gray-20`, `bg--gray-25`, `bg--gray-30`, `bg--gray-35`, `bg--gray-40`, `bg--gray-45`, `bg--gray-50`, `bg--gray-55`, `bg--gray-60`, `bg--gray-65`, `bg--gray-70`, `bg--gray-75`, `bg--white`

Legacy `bg--gray-1` … `bg--gray-7` still work but are deprecated.

---

## Spacing & Gaps

- `.gap--small` / `.gap` / `.gap--space-between` — spacing between flex/grid children
- Margin/padding utilities: see `/framework/docs/spacing`

---

## Flex & Grid

- `.flex` / `.flex--row` / `.flex--col` — flexbox containers
- `.grid` / `.grid--cols-N` — grid layouts
- `.columns` / `.column` — column-based layout with Overflow integration

> **Note:** Flex children that should shrink must have `min-width: 0` set, otherwise they expand to full container width before `plugins.js` runs and the layout breaks.

---

## Visual Utilities

- `.border` — border patterns for varying intensities (v2: supports 1/2/4-bit)
- `.rounded` — border radius presets
- `.outline` — pixel-perfect rounded borders via `border-image`
- `.image` — dithered image rendering for e-ink
- `.text-stroke` — legible text on shaded backgrounds
- `.divider` — horizontal or vertical dividers
- `.visibility` — show/hide based on display bit depth
- `.responsive` — adapt styles based on screen width with breakpoint prefixes

---

## Chart

Renders data visualizations via Highcharts or Chartkick. **Animation must be disabled** for TRMNL's screenshot capture to work.

Import Highcharts via CDN:
```html
<script src="https://trmnl.com/js/highcharts/12.3.0/highcharts.js"></script>
<!-- For pattern fills (multi-series): -->
<script src="https://trmnl.com/js/highcharts/12.3.0/pattern-fill.js"></script>
```

### Supported Libraries
- **Highcharts** (primary)
- **Chartkick** (wrapper)
- Any CDN-enabled JS charting library

### Chart Types

**Line Chart** (trends over time):
```javascript
{
  animation: false,
  enableMouseTracking: false,
  colors: ["black"],
  series: [{ data: [...], lineWidth: 4, marker: { enabled: false } }],
  yAxis: { gridLineDashStyle: "shortdot", tickAmount: 5 },
  xAxis: { gridLineDashStyle: "dot", tickPixelInterval: 120 }
}
```

**Multi-Series Line** (compare datasets):
- Primary: solid `color: "#000000"`
- Secondary: pattern fill `color: { pattern: { image: '...gray-5.png', width: 12, height: 12 }}`
- Use `zIndex` for layering

**Bar Chart** (category comparisons):
```javascript
{ type: 'column', pointPadding: 0.05, groupPadding: 0.1 }
// Multiple series use pattern fills: gray-3, gray-5, gray-7
```

**Gauge Chart** (single metric 0–100):
```javascript
{
  type: 'gauge',
  startAngle: -150, endAngle: 150,
  plotBands: [/* colored segments with pattern fills */]
}
```

### Markup Pattern
```html
<div class="view view--full">
  <div class="layout layout--col gap--space-between">
    <!-- Optional metrics grid -->
    <div class="grid grid--cols-3">
      <div class="item">
        <div class="content">
          <span class="value value--tnums">975</span>
          <span class="label">Metric</span>
        </div>
      </div>
    </div>
    <!-- Chart container -->
    <div id="my-chart" class="w--full"></div>
  </div>
  <div class="title_bar">...</div>
</div>

<script>
  Highcharts.chart('my-chart', {
    animation: false,
    enableMouseTracking: false,
    chart: { height: 260 },
    series: [{ data: [["2024-06-09", 975], ["2024-06-10", 840]] }]
  });
</script>
```

### Common Config Options

| Option | Value | Purpose |
|--------|-------|---------|
| `animation` | `false` | Required — disable for screenshot capture |
| `enableMouseTracking` | `false` | Remove interactivity |
| `chart.height` | `260` / `284` / `203` | Chart height in px |
| `gridLineWidth` | `0` / `1` | Axis grid visibility |
| `thousands` | `","` | Number formatting |

---

## Data Visualization (other)

- `.table` — data tables optimized for e-ink; supports small/large/index variants
- `.progress` — progress bar styles with adaptive rendering

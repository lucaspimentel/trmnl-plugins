# TRMNL Design System Reference

Full interactive docs: https://trmnl.com/framework/docs

Device: 800x480px, black & white, 2-bit grayscale e-ink display.

## Layout

Base container for arranging content. Combine direction + alignment modifiers.

```html
<div class="layout layout--col layout--left layout--top">
  <!-- content -->
</div>
```

**Direction:**
- `.layout--row` — horizontal (left to right)
- `.layout--col` — vertical (top to bottom)

**Horizontal alignment:**
- `.layout--left`
- `.layout--center-x`
- `.layout--right`

**Vertical alignment:**
- `.layout--top`
- `.layout--center-y`
- `.layout--bottom`

**Combined:**
- `.layout--center` — center both axes

**Stretch:**
- `.layout--stretch` — stretch children in both directions
- `.layout--stretch-x` / `.layout--stretch-y` — single axis
- `.stretch-x` / `.stretch-y` — applied to individual children

## View

Container for plugin display sizes. Used by the TRMNL framework to select the right layout.

- `.view--full` — 800x480 full screen
- `.view--half_horizontal` — 800x240 top or bottom half
- `.view--half_vertical` — 400x480 left or right half
- `.view--quadrant` — 400x240 quarter screen

## Title Bar

Standard plugin header, typically placed at the bottom of each layout.

```html
<div class="title_bar">
  <img class="image" src="https://example.com/icon.svg">
  <span class="title">Plugin Name</span>
  <span class="instance">Instance Label</span>  <!-- optional -->
</div>
```

## Item

Structured list entry with optional metadata and emphasis levels.

```html
<div class="item">
  <div class="meta"></div>               <!-- optional, colored bar -->
  <div class="content">
    <span class="title title--small">Heading</span>
    <span class="description">Body text</span>
    <span class="label label--small">Tag</span>
  </div>
</div>
```

**Emphasis levels** (on `.item`):
- `.item--emphasis-1` — lightest (default)
- `.item--emphasis-2` — medium
- `.item--emphasis-3` — darkest/strongest

**Sub-elements:**
- `.meta` — colored sidebar indicator
- `.meta > .index` — numbered index
- `.icon` — optional icon between meta and content
- `.content` — main content area

## Typography

- `.title` / `.title--small` — headings
- `.description` — body text
- `.label` / `.label--small` — tags/badges
  - `.label--underline` — underlined variant
- `.value` / `.value--small` — data values
- `.rich-text` — formatted paragraphs

## List Overflow Management

Control how lists handle content that exceeds available space.

```html
<div class="layout layout--col"
     data-list-limit="true"
     data-list-max-height="410"
     data-list-hidden-count="true"
     data-list-max-columns="1">
  <!-- items -->
</div>
```

**Data attributes:**
- `data-list-limit="true"` — enable overflow handling
- `data-list-max-height="N"` — max height in pixels (or `"auto"`)
- `data-list-hidden-count="true"` — show "and N more" when items overflow
- `data-list-max-columns="N"` — max columns for multi-column layout

**Modern equivalents (on `.columns` container):**
- `data-overflow="true"`
- `data-overflow-max-cols="N"`
- `data-overflow-counter="true"`
- `data-overflow-max-height="N"`

## Content Control

- `data-content-limiter="true"` — auto-shrink font when content overflows
- `data-pixel-perfect="true"` — crisp text rendering for e-ink
- `.clamp--N` — limit text to N lines (e.g., `.clamp--3`)

## Spacing & Gaps

- `.spacing` — fixed margin/padding values
- `.gap--small` / `.gap` — spacing between flex/grid children

## Flex & Grid

- `.flex` / `.flex--row` / `.flex--col` — flexbox containers
- `.grid` — grid layouts with predefined columns
- `.columns` / `.column` — column-based layouts

## Visual Utilities

- `.background` — grayscale dithered patterns for 1-bit rendering
- `.border` — border patterns for varying intensities
- `.rounded` — border radius presets
- `.outline` — pixel-perfect rounded borders via border-image
- `.image` — dithered image rendering for e-ink
- `.text-stroke` — legible text on shaded backgrounds
- `.divider` — horizontal or vertical dividers

## Data Visualization

- `.table` — data tables optimized for e-ink
- `.chart` — chart container (supports Highcharts, Chartkick)
- `.progress` — progress bar styles

## Responsive & Visibility

- `.visibility` — show/hide based on display bit depth
- `.responsive` — adapt styles based on screen width with breakpoint prefixes

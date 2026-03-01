# Components

Reusable UI components: Chart, Item, Progress, Rich Text, and Table.

Source docs: [Chart](https://trmnl.com/framework/docs/chart) | [Item](https://trmnl.com/framework/docs/item) | [Progress](https://trmnl.com/framework/docs/progress) | [Rich Text](https://trmnl.com/framework/docs/rich_text) | [Table](https://trmnl.com/framework/docs/table)

## Chart

Data visualizations via Highcharts/Chartkick. **Animation must be disabled** for TRMNL screenshot capture.

Import Highcharts:
```html
<script src="https://trmnl.com/js/highcharts/12.3.0/highcharts.js"></script>
<!-- For pattern fills (multi-series): -->
<script src="https://trmnl.com/js/highcharts/12.3.0/pattern-fill.js"></script>
<!-- For gauge charts: -->
<script src="https://trmnl.com/js/highcharts/12.3.0/highcharts-more.js"></script>
```

### Line Chart (Chartkick wrapper)
```javascript
new Chartkick.LineChart("chart-id", data, {
  points: false, colors: ["black"],
  library: {
    animation: false, enableMouseTracking: false,
    plotOptions: { series: { lineWidth: 4, marker: { enabled: false } } },
    yAxis: { gridLineDashStyle: "shortdot", tickAmount: 5 },
    xAxis: { gridLineDashStyle: "dot" }
  }
});
```

### Multi-Series Line (direct Highcharts)
```javascript
Highcharts.chart('chart-id', {
  chart: { type: 'spline', height: 203 },
  animation: false, enableMouseTracking: false,
  series: [
    { data: [...], lineWidth: 4, color: "#000000", zIndex: 2 },
    { data: [...], lineWidth: 5, color: { pattern: { image: "https://trmnl.com/images/patterns/gray-5.png", width: 12, height: 12 }}, zIndex: 1 }
  ]
});
```

### Bar Chart
```javascript
{ chart: { type: 'column' },
  plotOptions: { column: { pointPadding: 0.05, groupPadding: 0.1 } },
  // Use pattern fills for different series: gray-3.png, gray-5.png, gray-7.png
}
```

### Gauge Chart
```javascript
{ chart: { type: 'gauge' },
  pane: { startAngle: -150, endAngle: 150 },
  yAxis: { min: 0, max: 100 },
  // Plot bands with pattern fills for filled/unfilled portions
}
```

### Common Config

| Option | Value | Purpose |
|---|---|---|
| `animation` | `false` | Required for screenshot capture |
| `enableMouseTracking` | `false` | Remove interactivity |
| `chart.height` | `260` / `284` / `203` | Chart height in px |
| `gridLineDashStyle` | `"shortdot"` / `"dot"` | Axis grid style |
| `thousands` | `","` | Number formatting |

### Markup Pattern
```html
<div id="my-chart" class="w--full"></div>
<script>Highcharts.chart('my-chart', { ... });</script>
```

## Item

Flexible list entry container with optional metadata, indexing, and icons.

### Variants

**With meta (colored sidebar):**
```html
<div class="item">
  <div class="meta"></div>
  <div class="content">
    <span class="title title--small">Heading</span>
    <span class="description">Body text</span>
    <span class="label label--small label--underline">Tag</span>
  </div>
</div>
```

**With emphasis:** `item--emphasis-1` (lightest) | `item--emphasis-2` | `item--emphasis-3` (darkest)

**With index:**
```html
<div class="item">
  <div class="meta"><span class="index">1</span></div>
  <div class="content">...</div>
</div>
```

**Simple (no meta):**
```html
<div class="item">
  <div class="content">...</div>
</div>
```

**With icon:**
```html
<div class="item">
  <div class="meta"></div>
  <div class="icon"><img src="icon.svg" class="w--[6cqw] h--[6cqw] portrait:w--[10cqw] portrait:h--[10cqw]"></div>
  <div class="content">...</div>
</div>
```

Note: `.list` class is deprecated. Use columns, flex, grid, or layout with Gap instead.

## Progress

### Progress Bar
```html
<div class="progress-bar">
  <div class="content">
    <span class="label">Label</span>
    <span class="value value--xxsmall">75%</span>
  </div>
  <div class="track"><div class="fill" style="width: 75%"></div></div>
</div>
```

**Sizes:** `progress-bar--xsmall` | `progress-bar--small` | `progress-bar` / `progress-bar--base` | `progress-bar--large`

**Emphasis:** `progress-bar--emphasis-2` | `progress-bar--emphasis-3`

### Progress Dots
```html
<div class="progress-dots">
  <div class="track">
    <div class="dot dot--filled"></div>
    <div class="dot dot--current"></div>
    <div class="dot"></div>
  </div>
</div>
```

**Sizes:** `progress-dots--xsmall` | `progress-dots--small` | `progress-dots` / `progress-dots--base` | `progress-dots--large`

**Dot states:** `dot--filled` (completed) | `dot--current` (active) | `dot` (empty)

## Rich Text

Formatted paragraph container with alignment and size variants.

```html
<div class="richtext richtext--center">
  <div class="content content--base">
    <p>Formatted text content</p>
  </div>
</div>
```

**Alignment (3 levels):**
- Container: `richtext--left` | `richtext--center` | `richtext--right`
- Content: `content--left` | `content--center` | `content--right`
- Text: `text--left` | `text--center` | `text--right`

**Content sizes:** `content--small` | `content--base` | `content--large` | `content--xlarge` | `content--xxlarge` | `content--xxxlarge`

**Width control:** Use Size utilities (e.g. `w--32`, `w--[250px]`, `w--full`)

Responsive: `lg:content--xxxlarge`, `portrait:content--small`

Integrates with Content Limiter (`data-content-limiter="true"`) and Pixel Perfect (`data-pixel-perfect="true"`).

## Table

Data tables optimized for e-ink rendering.

```html
<table class="table" data-table-limit="true">
  <thead><tr><th><span class="title">Column</span></th></tr></thead>
  <tbody><tr><td><span class="label">Cell</span></td></tr></tbody>
</table>
```

**Sizes:** `table` / `table--base` | `table--large` | `table--small` (alias: `table--condensed`) | `table--xsmall`

**Indexed:** `table table--indexed` with `<td><span class="meta"><span class="index">N</span></span></td>`

**Overflow:** `data-table-limit="true"` with optional `data-table-max-height` — auto-adds "and X more" row

**Clamping:** Apply `data-clamp="1"` on cell content (`<span class="label" data-clamp="1">`)

Responsive: `table--small lg:table--base`

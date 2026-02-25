# Highcharts Reference for TRMNL

Highcharts is the primary charting library for TRMNL plugins. Because TRMNL uses headless screenshot capture, certain settings are mandatory.

Full API reference: https://api.highcharts.com/highcharts/

## CDN

```html
<!-- Self-hosted via TRMNL CDN (preferred — avoids 429 from code.highcharts.com) -->
<script src="https://trmnl.com/js/highcharts/12.3.0/highcharts.js"></script>
<!-- For pattern fills (multi-series, bar charts) -->
<script src="https://trmnl.com/js/highcharts/12.3.0/pattern-fill.js"></script>
```

> **Do not use `code.highcharts.com`** — it rate-limits headless/automated requests (429).

---

## Mandatory Settings for TRMNL

```javascript
{
  animation: false,           // REQUIRED: disables animation so screenshot captures correctly
  enableMouseTracking: false, // disables hover tooltips (no mouse on e-ink)
  credits: { enabled: false } // removes "Highcharts.com" watermark
}
```

---

## Top-Level Options

### `chart`

```javascript
chart: {
  type: 'line',         // default series type: 'line', 'column', 'bar', 'area', 'gauge', 'pie', 'scatter'
  height: 260,          // pixels; common TRMNL values: 260, 284, 203
  width: null,          // null = fill container width
  backgroundColor: 'transparent',
  margin: [10, 44, 28, 36], // [top, right, bottom, left] — increase to fix clipped labels
  spacing: [10, 10, 15, 10],
  inverted: false,      // swap x/y axes
  animation: false,     // REQUIRED for TRMNL
  style: { fontFamily: 'Inter, sans-serif' }
}
```

### `title` / `subtitle`

```javascript
title: { text: null }   // null hides the title (recommended for TRMNL — space is precious)
```

### `legend`

```javascript
legend: { enabled: false }  // disable to save space
```

### `tooltip`

```javascript
tooltip: { enabled: false }  // disable (no hover on e-ink)
```

### `colors`

```javascript
colors: ["#000000"]     // monochrome for 1-bit e-ink
```

---

## `xAxis`

```javascript
xAxis: {
  type: 'datetime',       // 'linear' | 'logarithmic' | 'datetime' | 'category'
  categories: ['Mon', 'Tue', 'Wed'],  // explicit labels (overrides numeric)
  title: { text: null },
  labels: {
    style: { fontSize: '10px', color: '#000' },
    format: '{value:%b %e}',    // datetime format
    rotation: -45,
    step: 2                     // show every Nth label
  },
  gridLineWidth: 0,             // hide grid lines
  gridLineDashStyle: 'dot',     // 'Solid' | 'Dot' | 'ShortDot' | 'Dash' | 'ShortDash'
  tickInterval: 24 * 3600 * 1000,  // 1 day in ms for datetime axis
  tickPixelInterval: 120,       // approximate px between ticks
  min: null,
  max: null,
  lineWidth: 0,                 // hide axis line
  tickLength: 0                 // hide tick marks
}
```

### Datetime axis data format

```javascript
// Pass UTC timestamps in milliseconds
series: [{ data: [[1720224000000, 72], [1720310400000, 68]] }]

// Or use Date.UTC()
data: [[Date.UTC(2024, 0, 5), 72]]

// Highcharts also accepts ISO strings when pointStart/pointInterval are set:
series: [{
  pointStart: Date.UTC(2024, 0, 1),
  pointInterval: 3600 * 1000,  // 1 hour
  data: [72, 68, 65, 70]
}]
```

---

## `yAxis`

```javascript
yAxis: {
  title: { text: null },
  labels: {
    style: { fontSize: '10px' },
    format: '{value}°'
  },
  gridLineWidth: 1,
  gridLineDashStyle: 'shortdot',
  tickAmount: 5,           // force exact number of ticks
  min: null,
  max: null,
  opposite: false,         // true = right side
  reversed: false,
  lineWidth: 0,
  tickLength: 0
}
```

---

## `series`

```javascript
series: [{
  name: 'Temperature',
  type: 'line',            // overrides chart.type per-series
  data: [[ts, value], ...],
  color: '#000000',
  lineWidth: 2,
  marker: {
    enabled: false         // hide dots on data points
  },
  animation: false,
  enableMouseTracking: false,
  zIndex: 1               // layering for multi-series
}]
```

### Pattern fills (for multi-series differentiation on 1-bit)

Requires `pattern-fill.js`:

```javascript
color: {
  pattern: {
    image: 'https://trmnl.com/images/patterns/gray-5.png',
    width: 12,
    height: 12
  }
}
// Available patterns: gray-3.png, gray-5.png, gray-7.png
```

---

## `plotOptions`

```javascript
plotOptions: {
  series: {
    animation: false,
    enableMouseTracking: false,
    marker: { enabled: false },
    lineWidth: 2,
    states: { hover: { enabled: false } }
  },
  column: {
    pointPadding: 0.05,    // space within group
    groupPadding: 0.1,     // space between groups
    borderWidth: 0
  },
  area: {
    fillOpacity: 0.3
  }
}
```

---

## Chart Type Examples

### Line Chart

```javascript
Highcharts.chart('container', {
  chart: { type: 'line', height: 260, animation: false },
  title: { text: null },
  credits: { enabled: false },
  legend: { enabled: false },
  tooltip: { enabled: false },
  xAxis: { type: 'datetime', tickPixelInterval: 120, gridLineDashStyle: 'dot' },
  yAxis: { tickAmount: 5, gridLineDashStyle: 'shortdot', title: { text: null } },
  series: [{
    data: [["2024-06-09", 975], ["2024-06-10", 840]],
    color: '#000',
    lineWidth: 3,
    animation: false,
    enableMouseTracking: false,
    marker: { enabled: false }
  }]
});
```

### Bar / Column Chart

```javascript
Highcharts.chart('container', {
  chart: { type: 'column', height: 200, animation: false },
  title: { text: null },
  credits: { enabled: false },
  xAxis: { categories: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri'] },
  yAxis: { title: { text: null } },
  plotOptions: {
    column: { pointPadding: 0.05, groupPadding: 0.1, borderWidth: 0 }
  },
  series: [{ data: [10, 20, 15, 30, 25], color: '#000', animation: false }]
});
```

### Gauge Chart

```javascript
Highcharts.chart('container', {
  chart: { type: 'gauge', height: 200, animation: false },
  title: { text: null },
  credits: { enabled: false },
  pane: { startAngle: -150, endAngle: 150 },
  yAxis: {
    min: 0, max: 100,
    plotBands: [
      { from: 0, to: 33, color: { pattern: { image: '...gray-3.png', width: 12, height: 12 } } },
      { from: 33, to: 66, color: { pattern: { image: '...gray-5.png', width: 12, height: 12 } } },
      { from: 66, to: 100, color: '#000' }
    ]
  },
  series: [{ data: [72], animation: false, enableMouseTracking: false }]
});
```

---

## Debugging Tips

- **Chart not rendering**: Make sure `animation: false` is set — the screenshot capture may fire before animation completes.
- **Axis labels clipped**: Increase `chart.margin` on the clipped side, e.g. `margin: [10, 44, 28, 36]`.
- **`Highcharts is not defined`**: Add the `<script>` tag inside the same `{% template %}` block that uses it — trmnlp's bundled `plugins.js` does not include Highcharts.
- **Pattern fills not working**: Include `pattern-fill.js` after `highcharts.js`.
- **Chart too wide/narrow**: Set explicit `chart.height` and leave `chart.width: null` to fill container.

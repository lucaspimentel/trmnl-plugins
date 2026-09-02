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

## Chart configuration

Axis, series, `plotOptions` and per-chart-type configuration is ordinary Highcharts and is
documented at <https://api.highcharts.com/highcharts/>. It was transcribed here once; the copy added
nothing over the vendor's reference and could only go stale. What is TRMNL-specific is above and
below: the mandatory settings, the CDN choice, and the e-ink rendering quirks.

For a worked example in this repo, see the hourly chart in `plugins/weather/src/shared.liquid`.

---

## SVG vs HTML Text Size Mismatch

Highcharts renders axis labels as SVG `<text>` elements, but `useHTML: true` labels (e.g. plotLine labels with icons) render as HTML. At the same `fontSize`, **SVG text appears ~20-25% smaller than HTML text**. To make them look the same size visually, bump SVG font sizes up. For example, if HTML labels are 16px, use 20px for SVG axis labels.

This affects x-axis labels, y-axis labels, and any other SVG-rendered text compared to `useHTML: true` data labels or plotLine labels.

---

## Debugging Tips

- **Chart not rendering**: Make sure `animation: false` is set — the screenshot capture may fire before animation completes.
- **Axis labels clipped**: Increase `chart.margin` on the clipped side, e.g. `margin: [10, 44, 28, 36]`.
- **`Highcharts is not defined`**: Add the `<script>` tag inside the same `{% template %}` block that uses it — trmnlp's bundled `plugins.js` does not include Highcharts.
- **Pattern fills not working**: Include `pattern-fill.js` after `highcharts.js`.
- **Chart too wide/narrow**: Set explicit `chart.height` and leave `chart.width: null` to fill container.

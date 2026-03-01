# Foundation

The TRMNL framework uses a fixed hierarchy: **Screen > (Mashup >) View > Layout + Title Bar**.

Source docs: [Structure](https://trmnl.com/framework/docs/structure) | [Screen](https://trmnl.com/framework/docs/screen) | [View](https://trmnl.com/framework/docs/view) | [Layout](https://trmnl.com/framework/docs/layout) | [Title Bar](https://trmnl.com/framework/docs/title_bar) | [Columns](https://trmnl.com/framework/docs/columns) | [Mashup](https://trmnl.com/framework/docs/mashup)

## Structure

```html
<!-- Single view -->
<div class="screen">
  <div class="view view--full">
    <div class="layout layout--col"><!-- content --></div>
    <div class="title_bar">...</div>
  </div>
</div>

<!-- Mashup (multiple views) -->
<div class="screen">
  <div class="mashup mashup--1Lx1R">
    <div class="view view--half_vertical">
      <div class="layout">...</div>
      <div class="title_bar">...</div>
    </div>
    <div class="view view--half_vertical">
      <div class="layout">...</div>
      <div class="title_bar">...</div>
    </div>
  </div>
</div>
```

On the TRMNL Platform, `screen`/`view` wrappers are injected automatically. With trmnlp (BYOS), include the full hierarchy.

## Screen

Outermost container. Sets device dimensions, orientation, color depth, and padding.

**CSS Variables:**
- `--screen-w` (default 800px), `--screen-h` (default 480px)
- `--full-w`, `--full-h` (minus padding)
- `--ui-scale` (default 1), `--gap-scale` (default 1)
- `--color-depth` (default 1)

**Device variants:** `screen--og` (800x480, 1-bit), `screen--v2` (1040x780, 4-bit), `screen--amazon_kindle_2024` (718x540, 4-bit)

**Modifiers:**
- `screen--portrait` — swaps width/height
- `screen--no-bleed` — removes padding, extends content to edges
- `screen--dark-mode` — inverts colors, preserves images
- `screen--backdrop` — patterned background (1-bit) or solid gray (2/4-bit) with plain white views

## View

Container for plugin content. Size determines space allocation.

| Class | Dimensions |
|---|---|
| `view--full` | 800x480 |
| `view--half_horizontal` | 800x240 |
| `view--half_vertical` | 400x480 |
| `view--quadrant` | 400x240 |

Non-full views must be inside a Mashup.

## Layout

Exactly one `layout` per `view`. Content container with direction and alignment.

```html
<div class="layout layout--col layout--left layout--top">
  <!-- content -->
</div>
```

**Direction:** `layout--row` | `layout--col`

**Horizontal:** `layout--left` | `layout--center-x` | `layout--right`

**Vertical:** `layout--top` | `layout--center-y` | `layout--bottom`

**Combined:** `layout--center` (both axes)

**Stretch:** `layout--stretch` | `layout--stretch-x` | `layout--stretch-y`

**Child stretch:** `stretch-x` | `stretch-y` on individual elements

Responsive: `md:layout--row`, `portrait:layout--bottom`, `lg:portrait:layout--bottom`

Direct children are typically Columns, Grid, or Flex containers. Do not nest layouts.

## Title Bar

Sibling of `layout` inside a `view`. Auto-compacts in mashup contexts.

```html
<div class="title_bar">
  <img class="image" src="https://example.com/icon.svg">
  <span class="title">Plugin Name</span>
  <span class="instance">Instance Label</span>  <!-- optional -->
</div>
```

## Columns

Distributes same-type data across auto-balanced columns with overflow handling. Use inside Layout for variable-length lists where the system should figure out distribution.

```html
<div class="columns">
  <div class="column">
    <!-- items -->
  </div>
</div>
```

Use Columns over Grid/Flex when you have lists of same-type items and want automatic distribution.

## Mashup

Arranges multiple plugin views in one screen.

| Modifier | Layout |
|---|---|
| `mashup--1Lx1R` | 1 Left, 1 Right |
| `mashup--1Tx1B` | 1 Top, 1 Bottom |
| `mashup--1Lx2R` | 1 Left, 2 Right |
| `mashup--2Lx1R` | 2 Left, 1 Right |
| `mashup--2Tx1B` | 2 Top, 1 Bottom |
| `mashup--1Tx2B` | 1 Top, 2 Bottom |
| `mashup--2x2` | 2x2 Grid |

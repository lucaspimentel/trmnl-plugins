# Styling

Visual utilities for backgrounds, borders, text, images, rounded corners, outlines, scale, and strokes.

Source docs: [Background](https://trmnl.com/framework/docs/background) | [Text](https://trmnl.com/framework/docs/text) | [Border](https://trmnl.com/framework/docs/border) | [Rounded](https://trmnl.com/framework/docs/rounded) | [Outline](https://trmnl.com/framework/docs/outline) | [Image](https://trmnl.com/framework/docs/image) | [Image Stroke](https://trmnl.com/framework/docs/image_stroke) | [Text Stroke](https://trmnl.com/framework/docs/text_stroke) | [Scale](https://trmnl.com/framework/docs/scale)

## Background

Dithered grayscale fills via repeating pixel patterns. On 1-bit displays, these create the illusion of gray shades.

**Classes:** `bg--black`, `bg--gray-10` through `bg--gray-75` (in steps of 5), `bg--white`

```html
<div class="bg--gray-50">...</div>
```

Legacy `bg--gray-1` through `bg--gray-7` still work but are deprecated.

Responsive: `md:bg--gray-50`, `portrait:bg--gray-30`

## Text Color & Alignment

**Color:** `text--black`, `text--gray-10` through `text--gray-75`, `text--white` (dither patterns for 1-bit)

**Alignment:** `text--left` | `text--center` | `text--right` | `text--justify`

Legacy `text--gray-1` through `text--gray-7` still work.

Responsive: `md:text--center`, `portrait:text--right`, `lg:4bit:text--center`

Bit-depth prefixes work for alignment only. Text color adapts automatically by bit depth.

## Border

Dithered grayscale borders. v2 breaking change: new visual scale, works on both light and dark backgrounds.

**Horizontal:** `border--h-1` (black) through `border--h-7` (white)

**Vertical:** `border--v-1` through `border--v-7`

```html
<div class="border--h-4">...</div>
```

No responsive variants.

## Rounded

Border radius presets.

**Named sizes:** `rounded--none` (0px) | `rounded--xsmall` (5px) | `rounded--small` (7px) | `rounded` / `rounded--base` (10px) | `rounded--medium` (15px) | `rounded--large` (20px) | `rounded--xlarge` (25px) | `rounded--xxlarge` (30px) | `rounded--full` (9999px, pill)

**Arbitrary:** `rounded--[Npx]` where N is 0-50 (no responsive support)

**Corner-specific:** `rounded-tl--{size}`, `rounded-tr--{size}`, `rounded-br--{size}`, `rounded-bl--{size}`

**Side-specific:** `rounded-t--{size}`, `rounded-r--{size}`, `rounded-b--{size}`, `rounded-l--{size}`

Responsive (named sizes only): `md:rounded--large`, `portrait:rounded--small`

## Outline

Pixel-perfect rounded borders using CSS border-image with 9-slice composite.

```html
<div class="outline">Content with rounded border</div>
```

**Bit-depth behavior:**
- 1-bit: Uses `border-image` with dithered 9-slice for crisp corners
- 2-bit/4-bit: Falls back to standard CSS `border: 1px solid` with `border-radius: 10px`

**Screen backdrop:** `screen--backdrop` modifier gives mashup views a patterned/gray background instead of outlined borders.

## Image

**Dithering:** `image-dither` class to dither for 1-bit displays

**Object fit:** `image--fill` | `image--contain` | `image--cover`

```html
<img class="image image-dither rounded" src="...">
<img class="image image--cover" src="...">
```

## Image Stroke

Outlines on vector/transparent raster images.

**Sizes:** `image-stroke--small` (1px) | `image-stroke` / `image-stroke--base` (1.5px) | `image-stroke--medium` (2px) | `image-stroke--large` (2.5px) | `image-stroke--xlarge` (3px)

**Color:** Add `image-stroke--black` for dark backgrounds (default is white)

```html
<img class="image-stroke image-stroke--medium" src="icon.svg">
<img class="image-stroke image-stroke--black" src="icon-white.svg">
```

## Text Stroke

Outlined text for readability on shaded backgrounds. **Only works on pure black or white text** (not grayscale/dithered text).

**Sizes:** `text-stroke--small` (2px) | `text-stroke` / `text-stroke--base` (3.5px) | `text-stroke--medium` (4.5px) | `text-stroke--large` (6px) | `text-stroke--xlarge` (7.5px)

**Shade:** `text-stroke--{shade}` where shade is `black`, `gray-10` through `gray-75`, or `white`

```html
<span class="value value--large text-stroke">Outlined text</span>
<span class="value value--large text--white text-stroke text-stroke--black">White text, black stroke</span>
```

## Scale

Scales the entire interface via `--ui-scale` CSS variable. Available on 4-bit devices.

| Class | Scale |
|---|---|
| `screen--scale-xsmall` | 75% |
| `screen--scale-small` | 87.5% |
| `screen--scale-regular` | 100% (default) |
| `screen--scale-large` | 112.5% |
| `screen--scale-xlarge` | 125% |
| `screen--scale-xxlarge` | 150% |

```html
<div class="screen screen--v2 screen--scale-small">...</div>
```

Affects font sizes, line heights, component dimensions, and spacing that uses `--ui-scale`. Fixed pixel values and screen dimensions are unaffected.

# Elements

Basic building blocks: Title, Value, Label, Description, and Divider.

Source docs: [Title](https://trmnl.com/framework/docs/title) | [Value](https://trmnl.com/framework/docs/value) | [Label](https://trmnl.com/framework/docs/label) | [Description](https://trmnl.com/framework/docs/description) | [Divider](https://trmnl.com/framework/docs/divider)

> **Important — always prefer built-in CSS classes over custom `font-size` overrides.**
> Each class uses a font optimized for e-ink at its native size. Overriding `font-size` — especially on `.description` (NicoPups bitmap font at 16px) — breaks pixel-perfect rendering. Pick the closest built-in class instead.

## Title

Heading text (NicoClean font).

**Sizes:** `title--small` | `title` / `title--base` | `title--large` (30px) | `title--xlarge` (35px) | `title--xxlarge` (40px)

```html
<span class="title title--small">Small Title</span>
<span class="title">Base Title</span>
```

Responsive: `sm:`, `md:`, `lg:`, `portrait:` (e.g. `title--small lg:title--base`)

Unclamped by default in v2 — add `data-clamp="N"` explicitly if needed.

## Value

Numerical/textual values (Inter font).

**Sizes:** `value--xxsmall` (16px) | `value--xsmall` (20px) | `value--small` (26px) | `value` / `value--base` (38px) | `value--large` (58px) | `value--xlarge` (74px) | `value--xxlarge` (96px) | `value--xxxlarge` (128px) | `value--mega` (170px) | `value--giga` (220px) | `value--tera` (290px) | `value--peta` (380px)

**Tabular numbers:** `value--tnums` (monospaced digits for aligned columns)

```html
<span class="value value--large value--tnums">48,206.62</span>
```

Responsive: `md:value--large`, `portrait:value--small`

## Label

Tags/badges (NicoClean font).

**Sizes:** `label--small` | `label` / `label--base` | `label--large` | `label--xlarge` | `label--xxlarge`

**Styles:** `label` (plain) | `label--outline` (bordered) | `label--underline` | `label--gray` (muted) | `label--inverted` (high contrast)

Combine size and style: `class="label label--large label--outline"`

```html
<span class="label label--small label--underline">Tag</span>
```

Supports `data-clamp="N"` for truncation.

Responsive: `sm:`, `md:`, `lg:`, `portrait:`, `1bit:`, `2bit:`, `4bit:`

Bit-depth example: `1bit:label--inverted 2bit:label--outline 4bit:label--underline`

Legacy: `label--gray-out` still works, use `label--gray` instead.

Unclamped by default in v2.

## Description

Descriptive body text (NicoPups bitmap font — designed for 16px).

**Sizes:** `description` / `description--base` (16px) | `description--large` | `description--xlarge` | `description--xxlarge`

> NicoPups is a bitmap font. It only renders correctly at its native 16px size. Do not apply `font-size` overrides. Use `.value--*` (Inter) for different sizes.

```html
<span class="description" data-clamp="2">Long text...</span>
```

Supports `data-clamp="N"` for truncation.

Responsive: `sm:`, `md:`, `lg:`, `portrait:`

Unclamped by default in v2.

## Divider

Horizontal or vertical separators. Auto-detects background type for optimal contrast.

```html
<div class="divider"></div>       <!-- horizontal, auto-detected -->
<div class="divider--v"></div>    <!-- vertical, auto-detected -->
```

**Auto background detection:** Divider adapts to white, light, dark, or black backgrounds.

**Manual control:** `divider--on-white` | `divider--on-light` | `divider--on-dark` | `divider--on-black`

```html
<!-- Replaces: <div class="border--h-6 w--full"></div> -->
<div class="divider"></div>
```

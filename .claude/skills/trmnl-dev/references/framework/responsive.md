# Responsive Utilities

Three systems for adaptive layouts: size-based breakpoints, orientation variants, and bit-depth variants.

Source docs: [Responsive](https://trmnl.com/framework/docs/responsive) | [Visibility](https://trmnl.com/framework/docs/visibility) | [Responsive Test](https://trmnl.com/framework/docs/responsive_test)

## Size-Based Responsive

Mobile-first approach. Prefix any utility class with a breakpoint name. Style applies at that breakpoint and larger.

| Prefix | Min Width | Example Devices |
|---|---|---|
| `sm:` | 600px | Kindle 2024 |
| `md:` | 800px | TRMNL OG, TRMNL OG V2 |
| `lg:` | 1024px | TRMNL V2 |

```html
<span class="value md:value--large lg:value--xlarge">Responsive Value</span>
```

## Orientation-Based

Landscape is default. Use `portrait:` prefix for portrait-specific styles.

```html
<div class="flex flex--row portrait:flex--col gap">
  <div>Item 1</div>
  <div>Item 2</div>
</div>
```

## Bit-Depth Responsive

NOT progressive — each variant targets a specific bit-depth only.

| Prefix | Color Support | Example Devices |
|---|---|---|
| `1bit:` | Monochrome (2 shades) | TRMNL OG |
| `2bit:` | Grayscale (4 shades) | TRMNL OG V2 |
| `4bit:` | Grayscale (16 shades) | TRMNL V2, Kindle 2024 |

```html
<div class="h--36 w--36 1bit:bg--black 2bit:bg--gray-45 4bit:bg--gray-75"></div>
```

## Combining All Systems

Pattern: `size:orientation:bit-depth:utility` (each modifier is optional)

| Pattern | Example | When Active |
|---|---|---|
| `size:` | `md:value--large` | Medium+ screens |
| `orientation:` | `portrait:flex--col` | Portrait only |
| `bit-depth:` | `4bit:bg--gray-75` | 4-bit displays only |
| `size:orientation:` | `md:portrait:text--center` | Medium+ in portrait |
| `size:bit-depth:` | `lg:2bit:value--xlarge` | Large+ with 2-bit |
| `orientation:bit-depth:` | `portrait:2bit:value--small` | Portrait with 2-bit |
| `size:orientation:bit-depth:` | `md:portrait:4bit:gap--large` | All three combined |

More modifiers = higher CSS specificity.

## Component Support

| Component | Size | Orientation | Bit-Depth |
|---|---|---|---|
| Background | Yes | Yes | — |
| Border | No | No | — |
| Text | Yes | Yes | — |
| Visibility | Yes | Yes | Yes |
| Value | Yes | Yes | No |
| Label | Yes | Yes | Yes |
| Spacing | Yes | Yes | No |
| Layout | Yes | Yes | No |
| Gap | Yes | Yes | No |
| Flexbox | Yes | Yes | No |
| Rounded | Yes | Yes | No |
| Size | Yes | Yes | No |
| Grid | Yes | Yes | No |
| Clamp | Yes | Yes | No |
| Overflow | Yes | Yes | No |

## Visibility & Display

Control element visibility across devices.

**Display utilities:** `hidden` | `visible` | `block` | `inline` | `inline-block` | `flex` | `grid` | `table` | `table-row`

```html
<!-- Hidden by default, visible on medium+ -->
<div class="hidden md:visible">Content</div>

<!-- Visible by default, hidden on large -->
<div class="visible lg:hidden">Content</div>

<!-- Display as flex on medium+ -->
<div class="hidden md:flex">Content</div>

<!-- Device-specific: different display per device -->
<div class="hidden md:1bit:block md:2bit:flex lg:4bit:grid">Content</div>
```

All display utilities work with size, orientation, and bit-depth prefixes.

| Class | Target |
|---|---|
| `md:1bit:block` | TRMNL OG only (800px, 1-bit) |
| `md:2bit:flex` | TRMNL OG V2 only (800px, 2-bit) |
| `lg:4bit:grid` | TRMNL V2 only (1024px, 4-bit) |
| `sm:4bit:table` | Kindle 2024 only (600px, 4-bit) |

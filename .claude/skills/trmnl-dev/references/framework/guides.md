# Guides

Framework v2 overview, upgrade instructions, enhancement patterns, troubleshooting, and TRMNL X support.

Source docs: [v2 Overview](https://trmnl.com/framework/docs/v2_overview) | [Upgrade Guide](https://trmnl.com/framework/docs/upgrade_guide) | [Enhancement Guide](https://trmnl.com/framework/docs/enhancement_guide) | [Troubleshooting](https://trmnl.com/framework/docs/troubleshooting_guide) | [TRMNL X Guide](https://trmnl.com/framework/docs/trmnl_x_guide)

## Framework v2 Overview

v2 is the first open-source e-paper adaptive front-end framework. Key improvements:

- **Adaptive:** automatically adjusts to bit-depth, size, and orientation
- **Expanded palette:** 1-bit (2 shades) to 4-bit (16 shades)
- **Dynamic engines:** Overflow, Clamp, Content Limiter calculate available space automatically
- **Backwards compatible:** existing code continues to work

**New in v2:** Scale, Visibility, Aspect Ratio, Rounded utilities; Divider element; Progress component; Table Overflow engine

**Enhanced:** Clamp (rebuilt in JS), Overflow (smart columns, group headers), Content Limiter (dynamic), Item (meta-emphasis), Table (size variants)

**Breaking changes:**
- Border utility: new class names for multi-bit-depth support
- Clamping: Title, Label, Description are unclamped by default — add `data-clamp="N"` explicitly

## Upgrade Guide (v1 to v2)

### Borders
Search for `border--h-{N}` classes and re-evaluate the intensity using the updated scale. Class names are the same but visual output differs (now works on both light and dark backgrounds).

### Clamping
Search for `<span class="label">` (etc.) and add `data-clamp="N"` where truncation is needed. Legacy `clamp--N` classes still work but `data-clamp` is preferred.

## Enhancement Guide

### 1. Upgrade Engines
- **Overflow:** remove hard-coded values, let engine calculate space
- **Content Limiter:** same — drop fixed values
- **Table Overflow:** use new engine for automatic "& X more" rows

### 2. Apply Responsive Prefixes
- **Display size:** `sm:`, `md:`, `lg:` for different screen sizes
- **Bit depth:** `1bit:`, `2bit:`, `4bit:` for grayscale capabilities
- **Orientation:** `portrait:` for rotated displays

### 3. New Utilities
- Aspect Ratio, Rounded, Divider, Progress, Item meta-emphasis, Table size variants

## Troubleshooting Checklist

- **Single layout element:** exactly one `layout` per view, no nesting
- **No nested elements:** Labels/Descriptions can't contain other elements; use Rich Text for complex content
- **Clamping not working:** Labels/Descriptions can't contain rich text for clamping; use Rich Text + Content Limiter instead

## TRMNL X Guide

Source: [TRMNL X Guide](https://trmnl.com/framework/docs/trmnl_x_guide)

TRMNL X is a larger, 4-bit e-paper display (1040x780, 16 shades of gray) with portrait orientation support. The framework maintains backward compatibility — existing OG plugins work on X, but responsive prefixes unlock the X's full potential.

| Feature | TRMNL OG | TRMNL X |
|---------|----------|---------|
| Resolution | 800x480 | 1040x780 |
| Bit depth | 1-bit (B&W) | 4-bit (16 shades) |
| Orientation | Landscape only | Landscape + Portrait |
| Size prefix | `md:` | `lg:` |
| Bit-depth prefix | `1bit:` | `4bit:` |
| Screen classes | `screen--ogv2 screen--md screen--1bit` | `screen--v2 screen--lg screen--4bit` |

### The `--base` Modifier
Every element/component supports explicit `--base` size. Lets you reset to default at a breakpoint. Previously, if you set a smaller size for compact screens, there was no way to undo it at a larger breakpoint.
```html
<span class="title title--small lg:title--base">Dashboard</span>
<span class="value value--xsmall md:value--base">48,206</span>
```

Available on: Title, Value, Label, Description, Rich Text content, Table, Progress, Gap, Rounded, Text Stroke, Image Stroke.

### New Larger Typography
- **Value:** `value--mega` (170px), `value--giga` (220px), `value--tera` (290px), `value--peta` (380px)
- **Title:** `title--large` (30px), `title--xlarge` (35px), `title--xxlarge` (40px)
- **Label/Description/Rich Text:** new large/xlarge/xxlarge variants

### Container Query Units
Layout establishes a CSS Container Query context. Unlike viewport units, these work correctly inside mashup slots where available space is a fraction of the full screen.
```html
<div class="w--[50cqw]">Half the layout width</div>
<div class="h--[80cqh]">80% of layout height</div>
```
Available: `w--[Ncqw]`, `h--[Ncqh]` (0-100), plus min/max variants. All support responsive prefixes.

### Responsive Overflow Columns
```html
<div class="columns" data-overflow-max-cols="2" data-overflow-max-cols-lg="3">...</div>
```
Suffixes: `-sm`, `-md`, `-lg`, `-portrait`, `-sm-portrait`, `-md-portrait`, `-lg-portrait`

### Layout Improvements
- `stretch-x`/`stretch-y` now axis-correct relative to layout direction with proper `min-width`/`min-height` handling
- Include `min-width: 0`/`min-height: 0` to prevent flex overflow
- Grid `col--span-*` works with all responsive prefixes
- Item `.icon` gets same flex styling as `.content`
- Flex rows auto-stretch to tallest sibling

### Gap & Rounded Updates
- **Gap:** `gap--base` (responsive reset), `gap--auto` (space-evenly), `gap--distribute` (space-between), arbitrary from `gap--[0px]`
- **Rounded:** `rounded--base` (10px), arbitrary from `rounded--[0px]`

### Rich Text
- `content--center` works on 4-bit
- Max-width is size-aware (380px small, 640px medium, 780px large; full width in portrait)
- All content sizes support responsive prefixes

### Responsive Clamp
New `lg` breakpoint: `data-clamp-lg="4"`. Portrait orientation variants also available.

### Responsive Arbitrary Sizes
`w--[Npx]` and `h--[Npx]` now support responsive prefixes (max 800px).

### Landscape Default
`landscape:` prefixes work without explicit `.screen--landscape` class.

### Bug Fixes
- Title bar background bleed on 4-bit displays
- Half horizontal layout height calculation
- Flex column available height computation improved (uses sibling height summing)

### Designing for Both OG and X

When building templates that should work on both devices:

1. **Start with OG layout**, then enhance for X with `lg:` and `4bit:` prefixes
2. **Use `--base` to reset sizes** — set compact sizes for OG, reset to default for X:
   ```html
   <span class="value value--small lg:value--base">{{ temperature }}</span>
   ```
3. **Show more content on X** — use visibility utilities to reveal extra data:
   ```html
   <div class="hidden lg:block">Extra detail only visible on X</div>
   ```
4. **More columns on X** — use responsive overflow columns:
   ```html
   <div class="columns" data-overflow-max-cols="1" data-overflow-max-cols-lg="2">...</div>
   ```
5. **Handle portrait orientation** — X supports portrait; use `portrait:` prefix:
   ```html
   <div class="flex--row portrait:flex--col">...</div>
   ```
6. **Use container query units** for proportional sizing in mashup slots:
   ```html
   <div class="w--[100cqw] h--[50cqh]">Scales with container</div>
   ```
7. **Leverage 4-bit grayscale** — more subtle backgrounds and borders on X:
   ```html
   <div class="1bit:bg--gray-50 4bit:bg--gray-65">Adaptive background</div>
   ```

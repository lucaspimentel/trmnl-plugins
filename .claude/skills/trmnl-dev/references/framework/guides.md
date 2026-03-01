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

TRMNL X is a larger, 4-bit e-paper display. Key framework additions:

### The `--base` Modifier
Every element/component supports explicit `--base` size. Lets you reset to default at a breakpoint:
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
Layout establishes a CSS Container Query context. Unlike viewport units, these work correctly inside mashup slots.
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
- `stretch-x`/`stretch-y` now axis-correct relative to layout direction
- Include `min-width: 0`/`min-height: 0` to prevent flex overflow
- Grid `col--span-*` works with all responsive prefixes
- Item `.icon` gets same flex styling as `.content`

### Gap & Rounded Updates
- **Gap:** `gap--base` (responsive reset), `gap--auto` (space-evenly), `gap--distribute` (space-between), arbitrary from `gap--[0px]`
- **Rounded:** `rounded--base` (10px), arbitrary from `rounded--[0px]`

### Rich Text
- `content--center` works on 4-bit
- Max-width is size-aware (380px small, 640px medium, 780px large)
- All content sizes support responsive prefixes

### Responsive Clamp
New `lg` breakpoint: `data-clamp-lg="4"`

### Landscape Default
`landscape:` prefixes work without explicit `.screen--landscape` class.

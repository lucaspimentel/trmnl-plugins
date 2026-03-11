# Arrangement

Layout utilities for sizing, spacing, gaps, flex, grid, and aspect ratios.

Source docs: [Flex](https://trmnl.com/framework/docs/flex) | [Grid](https://trmnl.com/framework/docs/grid) | [Size](https://trmnl.com/framework/docs/size) | [Spacing](https://trmnl.com/framework/docs/spacing) | [Gap](https://trmnl.com/framework/docs/gap) | [Aspect Ratio](https://trmnl.com/framework/docs/aspect_ratio)

## Arbitrary bracket syntax `[Npx]` availability

Not all utility types support the `[Npx]` bracket syntax. When a value is not on the fixed scale and brackets aren't available, use inline styles.

| Utility | Bracket syntax | Example |
|---|---|---|
| `w--` / `h--` | Yes (`[0-800px]`, `[0-100cqw]`) | `w--[68px]`, `h--[225px]` |
| `gap--` | Yes (`[0-50px]`) | `gap--[12px]` |
| `rounded--` | Yes (`[0-50px]`) | `rounded--[3px]` |
| `m*--` / `p*--` (spacing) | **No** | Use `style="padding-top:30px;"` |

## Flex

Flexbox containers for row/column arrangements.

**IMPORTANT — Hidden defaults in `.flex` classes (from plugins.css):**

| Class | CSS applied (beyond the obvious) |
|---|---|
| `.flex` | `display:flex` + **`gap: var(--gap)` (default 10px)** |
| `.flex > *` | `min-width: 0` (auto-applied to all children) |
| `.flex:not([data-overflow]) > *` | `min-height: 0` (auto-applied to all children) |
| `.flex--col` | `flex-direction:column` + **`align-items: center`** |

**Consequences when migrating from inline `display:flex`:**
- Always pair `.flex` with an explicit gap class (`gap--none`, `gap--[2px]`, etc.) — otherwise you get an unwanted 10px gap
- `.flex--col` forces `align-items:center` — add `flex--stretch-x` or `flex--left` if you don't want centering
- The upside: `min-width:0` and `min-height:0` on children are automatic — no need for inline styles

```html
<div class="flex flex--row gap--small">...</div>
<div class="flex flex--col gap">...</div>
```

**Direction:** `flex--row` | `flex--col` | `flex--row-reverse` | `flex--col-reverse`

**Horizontal alignment:** `flex--left` | `flex--center-x` | `flex--right`

**Vertical alignment:** `flex--top` | `flex--center-y` | `flex--bottom`

**Container stretch:** `flex--stretch` | `flex--stretch-x` | `flex--stretch-y`

**Item stretch:** `stretch` | `stretch-x` | `stretch-y`

**Prevent shrink:** `shrink-0` or `no-shrink`

**Wrapping:** `flex--wrap` | `flex--nowrap` | `flex--wrap-reverse`

**Align content (multi-line):** `flex--content-start` | `flex--content-center` | `flex--content-end` | `flex--content-between` | `flex--content-around` | `flex--content-evenly` | `flex--content-stretch`

**Main-axis distribution:** `flex--between` | `flex--around` | `flex--evenly`

**Item-level:**
- Self alignment: `self--start` | `self--center` | `self--end` | `self--stretch`
- Grow/shrink: `grow` | `shrink-0`
- Flex shorthand: `flex-none` | `flex-initial`
- Basis: `basis--{size}` (e.g. `basis--36`)
- Order: `order--first` | `order--last` | `order--{N}` | `order---{N}`

**Inline:** `inline-flex`

Responsive: `md:flex--row`, `portrait:flex--col`, `md:portrait:flex--col`

## Grid

Grid layouts with column counts and spans.

```html
<div class="grid grid--cols-3 gap">
  <div class="col--span-2">Wide</div>
  <div>Normal</div>
</div>
```

**Column count:** `grid--cols-{N}` (e.g. `grid--cols-3`, `grid--cols-4`)

**Column spans:** `col--span-{N}` (e.g. `col--span-1`, `col--span-2`, `col--span-6`)

**Column layout:** `col` | `col--start` | `col--center` | `col--end`

**Row layout:** `row` | `row--start` | `row--center` | `row--end`

**Wrapping:** `grid--wrap` with `grid--min-{size}` for minimum track size

Responsive: `md:grid--cols-3`, `portrait:grid--cols-1`, `portrait:col--span-1`

## Size

Width and height utilities. Scale = N × 4px.

**Fixed sizes:** `w--{N}` / `h--{N}` — available values: 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 72, 80, 96

**IMPORTANT — the scale is NOT continuous.** There is no `w--13`, `w--15`, `w--17`, etc. After 12 the scale jumps: 12 → 14 → 16 → 20 → 24 → 28 → 32... Fractional values only exist for 0.5, 1.5, 2.5, 3.5. For sizes not on the scale, use the arbitrary bracket syntax.

**Arbitrary:** `w--[Npx]` / `h--[Npx]` where N is 0-800 (e.g. `w--[150px]`, `h--[68px]`). Use this for any pixel value not on the fixed scale.

**Dynamic:** `w--full` | `w--auto` | `w--min` | `w--max` | `h--full` | `h--auto` | `h--min` | `h--max`

**Container query:** `w--[Ncqw]` / `h--[Ncqh]` where N is 0-100 (% of layout dimensions)

**Min/max:**
- Fixed: `w--min-{size}`, `w--max-{size}`, `h--min-{size}`, `h--max-{size}`
- Arbitrary: `w--min-[Npx]`, `w--max-[Npx]`, `h--min-[Npx]`, `h--max-[Npx]`
- Dynamic: `w--min-full`, `w--max-full`, etc.
- Container query: `w--min-[Ncqw]`, `w--max-[Ncqw]`, `h--min-[Ncqh]`, `h--max-[Ncqh]`

Responsive: `md:w--36`, `portrait:h--[200px]`, `lg:w--[50cqw]`

Note: Arbitrary `[Npx]` sizes do NOT support responsive variants for non-size dimensions.

## Spacing

Margin and padding utilities. Scale = N × 4px.

**Margin:** `m--{size}`, `mt--{size}`, `mr--{size}`, `mb--{size}`, `ml--{size}`, `mx--{size}`, `my--{size}`

**Padding:** `p--{size}`, `pt--{size}`, `pr--{size}`, `pb--{size}`, `pl--{size}`, `px--{size}`, `py--{size}`

**Available sizes:** 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 72, 80, 96

**IMPORTANT — same scale gaps as Size.** No `mt--13`, `pt--7.5`, etc. After 12 the scale jumps. Fractional values only exist for 0.5, 1.5, 2.5, 3.5. **No arbitrary bracket syntax** (`pt--[30px]`) for spacing — use inline styles for values not on the scale.

Responsive: `md:my--{size}`, `portrait:px--{size}`, `lg:portrait:mt--{size}`

## Gap

Spacing between flex/grid children using CSS gap.

**Named sizes:** `gap--none` | `gap--xsmall` | `gap--small` | `gap` (base) | `gap--base` | `gap--medium` | `gap--large` | `gap--xlarge` | `gap--xxlarge`

**Arbitrary:** `gap--[Npx]` where N is 0-50 (no responsive support)

**Distribution:**
- `gap--auto` — `justify-content: space-evenly`
- `gap--distribute` — `justify-content: space-between` (first at start, last at end)
- `gap--space-between` — legacy alias for `gap--auto`

Responsive: `md:gap--large`, `portrait:gap--medium`, `lg:gap--xlarge` (named sizes only)

## Aspect Ratio

CSS aspect-ratio classes.

`aspect--auto` | `aspect--1/1` | `aspect--4/3` | `aspect--3/2` | `aspect--16/9` | `aspect--21/9` | `aspect--3/4` | `aspect--2/3` | `aspect--9/16` | `aspect--9/21`

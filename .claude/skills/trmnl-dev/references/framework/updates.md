# Framework Reference Updates

Corrections and clarifications discovered through hands-on plugin development, not captured in the official TRMNL docs.

## Spacing/Size scale is NOT continuous

The `w--`, `h--`, `m*--`, `p*--` scale (N × 4px) has gaps after 12:

**Available values:** 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 72, 80, 96

- No `w--13`, `w--15`, `w--17`, `pt--7.5`, `mt--13`, etc.
- Fractional values only: 0.5, 1.5, 2.5, 3.5 (no 4.5, 5.5, 7.5, etc.)

## Arbitrary bracket syntax `[Npx]` — not universal

| Utility | Bracket `[Npx]` | Fallback for off-scale values |
|---|---|---|
| `w--` / `h--` | Yes (0–800px) | `w--[68px]` |
| `gap--` | Yes (0–50px) | `gap--[12px]` |
| `rounded--` | Yes (0–50px) | `rounded--[3px]` |
| `m*--` / `p*--` | **No** | Use inline `style="padding-top:30px;"` |

## `.flex` hidden defaults

When migrating from inline `display:flex` to the `.flex` class:

- `.flex` adds a **default gap** (`var(--gap)`, 10px) — always pair with `gap--none` or an explicit gap class
- `.flex--col` adds **`align-items:center`** — use `flex--stretch-x` to override
- `.flex > *` gets `min-width:0` and `min-height:0` automatically — no need for inline min-width/min-height

## `shrink-0` vs `no-shrink`

Both exist. `shrink-0` sets `flex-shrink:0`. `no-shrink` also works but `shrink-0` is the more common convention.

## Recipe linter (`chef.rb`) inline style check

The TRMNL recipe linter counts raw substring occurrences of these CSS properties across **all** markup (HTML, `<style>`, `<script>`, Liquid comments, variable names):

`justify-content`, `padding`, `margin`, `background-color`, `border-radius`, `text-align`, `object-fit`, `font-size`

**Threshold: 6 total.** Exceeding this triggers: "Markup uses too many inline styles."

Workarounds for unavoidable cases:
- **CSS `font:` shorthand** avoids `font-size` substring: `.wi-lg { font: 64px/1 'weathericons'; }`
- **Framework classes** replace common properties: `flex--right` (justify-content), `text--center` (text-align), `pt--5` (padding-top)
- **JS computed property keys** avoid flagged Highcharts config keys: `['mar'+'gin']: [22, 8, 44, 8]`
- **Rename Liquid variables** containing flagged substrings (e.g. `padding_top` → `inset_top`)

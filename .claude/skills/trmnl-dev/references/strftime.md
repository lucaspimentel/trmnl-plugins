# strftime Format Code Reference

Used with the Liquid `date` filter: `{{ value | date: "format" }}`

Reference: https://strftime.net/

---

## Date

| Code | Description | Example |
|------|-------------|---------|
| `%Y` | 4-digit year | `2024` |
| `%y` | 2-digit year | `24` |
| `%C` | Century | `20` |
| `%m` | Month, zero-padded (01–12) | `01` |
| `%_m` | Month, space-padded | ` 1` |
| `%-m` | Month, no padding | `1` |
| `%B` | Full month name | `January` |
| `%b` | Abbreviated month name | `Jan` |
| `%d` | Day of month, zero-padded (01–31) | `05` |
| `%-d` | Day of month, no padding | `5` |
| `%e` | Day of month, space-padded | ` 5` |
| `%j` | Day of year (001–366) | `005` |
| `%A` | Full weekday name | `Monday` |
| `%a` | Abbreviated weekday name | `Mon` |
| `%u` | Weekday number, Monday=1 (1–7) | `1` |
| `%w` | Weekday number, Sunday=0 (0–6) | `1` |

---

## Time

| Code | Description | Example |
|------|-------------|---------|
| `%H` | Hour, 24-hour, zero-padded (00–23) | `14` |
| `%k` | Hour, 24-hour, space-padded (0–23) | `14` |
| `%-H` | Hour, 24-hour, no padding | `14` |
| `%I` | Hour, 12-hour, zero-padded (01–12) | `02` |
| `%l` | Hour, 12-hour, space-padded (1–12) | ` 2` |
| `%-I` | Hour, 12-hour, no padding | `2` |
| `%M` | Minute, zero-padded (00–59) | `05` |
| `%S` | Second, zero-padded (00–60) | `09` |
| `%p` | AM/PM uppercase | `PM` |
| `%P` | am/pm lowercase | `pm` |
| `%L` | Milliseconds (000–999) | `000` |

---

## Composed Formats

| Code | Equivalent | Example |
|------|-----------|---------|
| `%F` | `%Y-%m-%d` | `2024-01-05` |
| `%D` | `%m/%d/%y` | `01/05/24` |
| `%T` | `%H:%M:%S` | `14:05:09` |
| `%R` | `%H:%M` | `14:05` |
| `%r` | `%I:%M:%S %p` | `02:05:09 PM` |
| `%v` | `%e-%b-%Y` | ` 5-Jan-2024` |

---

## Week & Year

| Code | Description | Example |
|------|-------------|---------|
| `%V` | ISO 8601 week number (01–53) | `01` |
| `%W` | Week number, Monday first (00–53) | `01` |
| `%U` | Week number, Sunday first (00–53) | `01` |

---

## Timezone

| Code | Description | Example |
|------|-------------|---------|
| `%z` | UTC offset | `+0900` |
| `%Z` | Timezone abbreviation | `EST` |

---

## Epoch

| Code | Description | Example |
|------|-------------|---------|
| `%s` | Seconds since Unix epoch | `1704412800` |

---

## Padding Modifiers

Ruby strftime supports modifiers between `%` and the code:

| Modifier | Effect | Example |
|----------|--------|---------|
| `-` | No padding | `%-d` → `5` instead of `05` |
| `_` | Space padding | `%_m` → ` 1` instead of `01` |
| `0` | Zero padding (default for most) | `%0d` → `05` |

---

## Common Patterns for TRMNL

```liquid
{{ updated_at | date: "%b %-d" }}              → Jan 5
{{ updated_at | date: "%b %-d, %Y" }}          → Jan 5, 2024
{{ updated_at | date: "%-m/%-d" }}             → 1/5
{{ updated_at | date: "%a, %b %-d" }}          → Mon, Jan 5
{{ updated_at | date: "%-I:%M %p" }}           → 2:05 PM
{{ updated_at | date: "%H:%M" }}               → 14:05
{{ updated_at | date: "%b %-d, %-I:%M %p" }}   → Jan 5, 2:05 PM
{{ "now" | date: "%Y-%m-%d" }}                 → 2024-01-05 (render time)
{{ "now" | date: "%-I:%M %p" }}                → 2:05 PM (render time)
```

### With `ordinalize` (trmnl-liquid custom filter)

```liquid
{{ updated_at | ordinalize: "<<ordinal_day>> of %B" }}   → 5th of January
{{ updated_at | ordinalize: "%A the <<ordinal_day>>" }}  → Monday the 5th
```

### With `l_date` (trmnl-liquid, locale-aware)

```liquid
{{ updated_at | l_date: "%B %-d", "en" }}   → January 5
{{ updated_at | l_date: :long, "en" }}      → January 5, 2024
```

# Geographic telemetry (design, not yet implemented)

**Status: proposed.** Nothing in this document is in the code yet. The tags described under
[What to emit](#what-to-emit) do not exist; the only location data on a span today is
`weather.latitude` / `weather.longitude`, both `F1`, as listed in
[observability.md](observability.md).

The goal is to group forecast telemetry by place - country, subdivision, city - instead of by a
numeric coordinate, so a dashboard can answer "which regions see provider failures", "does cache hit
rate vary by geography", and "where are the users" without a human decoding coordinates.

## Why this is not just a nicer facet

A Datadog geomap **cannot** be driven by a coordinate string. Per the
[geomap docs](https://docs.datadoghq.com/dashboards/widgets/geomap/), the group-by tag must be a
country ISO code (alpha-2) or a country subdivision ISO code (ISO-3166-2), and for the points layer
"metric queries don't include geographic coordinates" - they need a join against a reference table
supplying lat/lon.

So the `weather.latitude` / `weather.longitude` tags on a span drive nothing on a geomap. Commit
`a3bcfef` ("Plot weather request locations on a map") suggests this was already attempted; ISO
codes are the missing prerequisite rather than a refinement.

## What to emit

| Tag | Format | Purpose |
|---|---|---|
| `weather.country_code` | ISO 3166-1 alpha-2, e.g. `US` | Geomap group-by; the only field safe to put in a metric |
| `weather.subdivision` | ISO-3166-2, e.g. `US-MA` | Finer geomap group-by, higher cardinality |
| `weather.city` | display name, e.g. `Boston` | Span tag only, for ad-hoc queries and top-lists |

Emit **codes, not display names**, for the first two. `"Massachusetts"` is unusable in a geomap.

### Span tags alone will not produce a map

The geomap's data sources are Log Events, Metric, RUM, SLO, and Security Signals - **not APM spans**.
Tagging a span gives facets, top-lists, and trace search, but no map. A map additionally needs either:

- a span-based metric grouped on the tag, or
- the ISO code in the `ForecastServed` log line, plus a category entry in
  `appsettings.json` under `Logging:Datadog:LogLevel` (and `DatadogLogAllowlistTests` updated).

Decide which surface is actually wanted before building: the tag is the easy half.

### Cardinality budget

Span-based metrics bill as custom metrics, so the group-by choice matters. Measured against ~223
distinct F1 cells in production:

| group-by | distinct values | x cache_status x provider | + units x limits |
|---|---|---|---|
| `country_code` | ~25-40 | ~320 timeseries | ~6,400 |
| `subdivision` | ~150 | ~1,200 | ~24,000 |
| `city` | ~300 | ~2,400 | ~48,000 |

**Group the metric on `country_code`.** Keep `city` as a span tag only, never in a metric alongside
the other facets.

## How to resolve a coordinate

Lazy and memoized, not precomputed.

| Concern | Decision |
|---|---|
| Dataset | Natural Earth **50m** admin-1 polygons, bundled in the image |
| Country / subdivision | **Point-in-polygon** |
| City | Nearest populated place, display-only |
| Memo key | Packed integer from the F1 cell, e.g. `(int)Math.Round(lat*10) * 10000 + (int)Math.Round(lon*10)` |
| Memo storage | Bounded, in its **own** `MemoryCache` instance |
| Failure | Non-throwing. Telemetry must never fail a forecast; unknown cells return a blank record |

Coordinates must be coarsened through `TrmnlApi.Observability.CoarseCoordinate` so the lookup keys on
exactly the value the span tags carry. That type documents two rounding hazards which have already
caused one bug each; do not reimplement `F1` rounding at the call site.

### Why lazy beats precomputing

Production runs ~500 requests/hour across **223 distinct F1 cells**, each re-requested about 2.24
times an hour. The memo is therefore tiny and warms within one refresh cycle:

| users | distinct F1 cells | memo footprint |
|---|---|---|
| ~500 (today) | ~223 | ~17 KB |
| 10x growth | ~2,200 | ~170 KB |

A full-planet precompute is the alternative, and it is far worse than it first appears. The F1 grid is
1,800 x 3,600 = **6,480,000** cells, not the 64,800 an easy miscalculation suggests (that multiplies
the degree ranges rather than the 0.1-degree steps). Land-only is ~1.9M cells, roughly 6-11 MB packed,
and a naive in-memory dictionary of that size costs 50 MB+ on a container that also holds the
forecast cache. Precomputing millions of cells to serve a few hundred is the wrong trade.

Bounding the precompute by radius does not rescue it either. Restricting to cells within 15 km of a
known city gets the artifact down to ~100 KB gzipped, but with users worldwide, anyone rural, in a
small town, or in a thinly-mapped region falls outside the radius and resolves to `unknown` - exactly
the users worth seeing in telemetry. Lazy nearest-place always resolves.

### Why point-in-polygon, not nearest-city

Nearest-centroid distance crosses borders freely: a cell in Windsor, Ontario resolves to Detroit, and
anywhere along the Rhine, the Pyrenees, or the US-Mexico border misassigns the country. Assigning a
country or subdivision is a containment question. Nearest-city is only valid for the city label
itself.

50m resolution is deliberate: its ~1-2 km positional error sits comfortably inside an 11 km cell, so
10m polygons would cost image size for accuracy that F1 coarsening discards anyway.

### Dataset licensing

Natural Earth is public domain. GeoNames (`cities15000`, `admin1CodesASCII`) is CC BY 4.0 and
**requires attribution**, so using it means shipping a NOTICE file in the image. Prefer Natural Earth
unless a GeoNames-only field is needed.

Existing NuGet options were checked and rejected: `ReverseGeocoder` (0.3.0) and
`Wibci.CountryReverseGeocode` are both low-activity and neither bundles its data.

### The memo is a DoS vector

`GET /api/v1/forecast` is anonymous and unthrottled - there is no rate limiting or auth anywhere in
`src/TrmnlApi/`. An unbounded memo keyed by F1 cell can be walked through all 6.48M cells by a
scripted caller, and unlike the forecast cache there is no upstream call to slow it down. The memo
must be bounded, and must not share the forecast cache's size budget (see
`WeatherCacheOptions.SizeLimit`): a place lookup must never be able to evict forecasts.

## PII

The city label is a deterministic function of the F1 coordinates **already on the span**, so it
carries no information the span does not already expose. There is no incremental leak. Two real
constraints, though:

- **The tags are coupled.** If the coordinate tags are ever coarsened or dropped to tighten the
  privacy threshold, a place label derived from F1 silently becomes the finest location signal on
  the span and defeats that change. Coarsen them together.
- **The aggregate is a user-location dataset.** This is a public plugin with hundreds of users, and
  TRMNL devices sit in homes, so these are home locations. If any users are in the EU, GDPR applies
  to the aggregate. This argues for ISO codes as the primary fields and `city` as optional.

Direct log submission does not get the Agent's sensitive-data scrubbing (see
[observability.md](observability.md#logs)), so anything added to a log line has to respect the same
F1 ceiling.

## Open question this would answer

The forecast cache keys on **F2** (~1.1 km, `WeatherCache.CacheKey`) while telemetry groups on
**F1** (~11 km). Nothing tags F2, so the number that decides whether nearby users share a cache entry
- distinct F2 keys per F1 cell - is structurally invisible today.

`weather.city` measures it directly: users per city *is* the sharing potential. Until then the only
read is indirect, via the raw `FreshFetch` / `FreshHit` counters at `GET /metrics`. A hit rate at or
below what the sub-hourly refresh cohort alone would produce implies F2 fragmentation is preventing
sharing, and that the cache key grain should move to F1 (or 0.05 degrees as a compromise for coastal
and mountain users, where 11 km spans a real gradient).

Note that `FreshTtl` (45 min, set in Railway) is deliberately below the 60 min default refresh
interval: an hourly device never self-hits and always gets a live fetch, which keeps worst-case
staleness on screen at 60 minutes rather than ~125. The cache exists for sub-hourly refreshers and
for nearby users, not for the default cadence. Do not "fix" it by raising it above 60 min.

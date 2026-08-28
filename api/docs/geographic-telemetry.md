# Geographic telemetry (design, not yet implemented)

**Status: proposed, decisions made.** Nothing in this document is in the code yet. The tags described
under [What to emit](#what-to-emit) do not exist; the only location data on a span today is
`weather.coord` / `weather.latitude` / `weather.longitude`, all `F1`, as listed in
[observability.md](observability.md).

The goal is to group forecast telemetry by place - country, subdivision, city - instead of by a
numeric coordinate, so a dashboard can answer "which regions see provider failures", "does cache hit
rate vary by geography", and "where are the users" without a human decoding coordinates.

Related: [place-input.md](place-input.md), which covers the inverse problem (a user-supplied place
name to coordinates) and specifies the v2 API. The two features meet in the middle, and that one was
built first, deliberately: see [Sequencing](#sequencing-build-v2-first).

## Decisions

| Question | Decision |
|---|---|
| Input precision | **F2** (~1.1 km), the already-snapped orchestrator values |
| Country + subdivision | Point-in-polygon against Natural Earth **50m admin-1** |
| City | Nearest populated place from GeoNames `cities15000` |
| No match (ocean, unmapped) | Snap to nearest within a bounded radius, else blank |
| Timing | **Synchronous, in the request path**, behind a memo |
| Surfaces | Span tags **and** the `ForecastServed` log line. No span-based metric today |
| Packaging | In-process project `TrmnlApi.Geo`, **not** a separate deployed service |

## Sequencing: build v2 first

**v2 has shipped**, so this is now a question of reading the data rather than deciding what to build
next. See [place-input.md](place-input.md).

This work only ever serves **coordinate** input. v2 takes a single free-form `place` parameter
accepting a city name or postal code, and forward geocoding returns country, country code, and a city
name directly, with no polygons involved.

Non-forked installs upgrade automatically, so most traffic has already moved to v2. If place input
dominates there, the polygon lookup serves a small minority and a much cheaper approximation may be
enough. v2 carries the `weather.input_kind` tag (`coordinates` or `place`) for exactly this reading.

Read that split on **v2 traffic only**, identified by the route. v1 takes coordinates and nothing
else, and forks will keep it alive for a while, so an unfiltered reading measures how many installs
have upgraded rather than what anyone prefers.

Read it from the **plugin** push onwards, too. v2 served production for a short window before the
plugin moved, and during it every install was still sending coordinates from the old template, so
that traffic reads `coordinates` regardless of what those users would have chosen.

What forward geocoding does **not** supply is an ISO-3166-2 subdivision code - Open-Meteo returns
`admin1` as a display name with a GeoNames id, verified against the live API. So if `weather.subdivision` matters, the polygon lookup remains the
only source of it, for both input paths.

## This is not about making the geomap work

An earlier draft of this document claimed a Datadog geomap cannot be driven by a coordinate. That is
true only of **metric** queries. Log Events geomaps do accept latitude/longitude, and the map is
already built and working off the `ForecastServed` log line. Commit `a3bcfef` ("Plot weather request
locations on a map") succeeded; it was not blocked on ISO codes.

So the map is done, and the reasons to do this work are the other ones:

- **Readable facets and top-lists.** Trace search grouped on `weather.city` beats grouped on
  `42.4,-71.1`.
- **Cardinality, if a span-based metric is ever added.** A metric cannot take a coordinate, and
  grouping on `country_code` collapses a few hundred distinct cells into a few dozen values. See
  [Cardinality budget](#cardinality-budget).
- **Measuring cache-sharing potential.** `weather.city` is the only practical read on whether the F2
  cache key is fragmenting entries that an F1 key would share. See
  [Open question this would answer](#open-question-this-would-answer).

## Where the coordinates come from

The plugin collects raw `latitude` / `longitude` numbers (`plugins/weather/src/settings.yml`). There
is no place name anywhere in the request to piggyback on, so reverse geocoding is genuinely required.

Datadog's built-in GeoIP processor is **not** an option. TRMNL fetches `polling_url` server-side, so
the client IP on an incoming forecast request belongs to TRMNL's infrastructure, not to the user's
home.

## What to emit

| Tag | Format | Source |
|---|---|---|
| `weather.country_code` | ISO 3166-1 alpha-2, e.g. `US` | NE admin-1 `iso_a2` |
| `weather.country` | display name, e.g. `United States` | static ISO name table keyed on the code |
| `weather.subdivision` | ISO-3166-2, e.g. `US-MA` | NE admin-1 `iso_3166_2` |
| `weather.subdivision_name` | display name, e.g. `Massachusetts` | NE admin-1 `name` |
| `weather.city` | display name, e.g. `Boston` | GeoNames nearest populated place |

Emit **codes as well as names** for country and subdivision. `"Massachusetts"` is unusable as a
geomap or metric group-by; `US-MA` is unreadable in a top-list. They are cheap, so carry both.

When a request carried a place name rather than coordinates, `weather.city` comes from the geocoding
result instead of the nearest-place lookup, because that is the place the user actually named. The
code fields still come from the polygon lookup either way. See
[place-input.md](place-input.md#how-the-two-features-layer).

### Surfaces

Both the span tags and the `ForecastServed` log message get these fields. Adding them to the log line
requires **no** change to `appsettings.json` or to `DatadogLogAllowlistTests`: the category
`TrmnlApi.Observability.ForecastServed` is already allowed at `Information`, and the allowlist filters
on category and level, not on message parameters. Only introducing a *new* logger category would move
the allowlist.

No span-based metric is planned yet. If one is added later, the constraint below applies.

### Cardinality budget

Span-based metrics bill as custom metrics, so a group-by choice would matter. Measured against ~223
distinct F1 cells in production:

| group-by | distinct values | x cache_status x provider | + units x limits |
|---|---|---|---|
| `country_code` | ~25-40 | ~320 timeseries | ~6,400 |
| `subdivision` | ~150 | ~1,200 | ~24,000 |
| `city` | ~300 | ~2,400 | ~48,000 |

**Any future metric groups on `country_code`.** `city` and `subdivision` stay span tags and log
attributes only, never a metric dimension alongside the other facets.

## How to resolve a coordinate

Lazy and memoized, not precomputed.

| Concern | Decision |
|---|---|
| Input | The **F2** coordinates the orchestrator has already snapped |
| Dataset, country + subdivision | Natural Earth **50m admin-1** polygons, bundled in the image |
| Dataset, city | GeoNames `cities15000`, bundled in the image |
| Country / subdivision | **Point-in-polygon** |
| City | Nearest populated place, display-only |
| Fallback | Nearest polygon or place **within a bounded radius** (start at 50 km), else blank |
| Memo key | Packed long from the F2 cell, `(latE2 + 9000) * 36001L + (lonE2 + 18000)` |
| Memo storage | Bounded, in its **own** `MemoryCache` instance |
| Failure | Non-throwing. Telemetry must never fail a forecast; unresolvable cells return a blank record |

### One dataset covers three fields

`ne_50m_admin_1_states_provinces` carries `iso_a2`, `iso_3166_2`, and `name` on every feature, so a
single containment query yields country code, subdivision code, and subdivision name together. A
separate countries layer is unnecessary. The country display name comes from a static ~250-row ISO
table keyed on `iso_a2`, not from a dataset.

### Why two datasets and not one

The fields pull in opposite directions, which is why neither source alone is enough:

- **GeoNames `admin1 code` is not reliably ISO-3166-2.** It is ISO for some countries and
  FIPS-derived for others. Natural Earth's admin-1 layer has a genuine `iso_3166_2` field and
  GeoNames does not, so the subdivision *code* has to come from Natural Earth regardless of any
  border-accuracy argument.
- **Natural Earth is thin on cities.** ~1,250 populated places at 50m and ~7,350 at 10m, against
  ~26,000 in GeoNames `cities15000`. The city label has to come from GeoNames or rural users all
  resolve to the same handful of metros.

### Why F2 and not F1

The orchestrator snaps both coordinates to the 0.01 degree cache grid before anything else uses them
(`WeatherForecastOrchestrator.cs`, top of `GetAsync`). Feeding those snapped values straight into the
lookup means no new rounding at the call site and none of the two-step hazard `CoarseCoordinate`
exists to contain: the lookup keys on exactly the value the cache keys on.

Do **not** coarsen to F1 first. F1 is the ceiling on what may be *emitted*, not a constraint on what
may be *read*. See [PII](#pii).

### Why point-in-polygon, not nearest-city

Nearest-centroid distance crosses borders freely: a cell in Windsor, Ontario resolves to Detroit, and
anywhere along the Rhine, the Pyrenees, or the US-Mexico border misassigns the country. Assigning a
country or subdivision is a containment question. Nearest-city is only valid for the city label
itself.

F2 sharpens this. At 11 km granularity a border error was arguably within the noise; at 1.1 km,
nearest-city would claim a precision the method does not have.

### 50m polygons, with 10m as a deliberate upgrade

50m resolution carries ~1-2 km of positional error. At F1 that sat comfortably inside an 11 km cell,
which is why an earlier draft treated 10m as pure waste. **At F2 that reasoning no longer holds**: the
polygon error now exceeds the 1.1 km cell.

Start at 50m anyway. Users within 2 km of a subdivision border are a small population and this is
telemetry, not billing. But it is now a deliberate accuracy tradeoff rather than a free one, and
`ne_10m_admin_1_states_provinces` is the upgrade path if border misassignment ever shows up as a real
problem.

### Coastal cells need the radius fallback

An F2 cell centre near a coast frequently lands in water, where containment returns nothing. Coastal
users are a meaningful share of a weather plugin's audience, so a pure-containment implementation
would silently drop them from the telemetry. Hence the bounded-radius snap: near enough to land, take
the nearest polygon; genuinely mid-ocean, return blank rather than inventing a country.

### Why lazy beats precomputing

Production runs ~500 requests/hour across ~223 distinct F1 cells. The F2 count is bounded above by
the user count, so expect a few hundred distinct cells and a memo in the low hundreds of KB, warming
within one refresh cycle. Interning the repeated country and subdivision strings keeps it smaller
still.

A full-planet precompute is the alternative, and F2 makes it far worse than it already was. The F1
grid is 1,800 x 3,600 = **6,480,000** cells (not the 64,800 an easy miscalculation suggests, which
multiplies the degree ranges rather than the 0.1-degree steps). The F2 grid is 18,000 x 36,000 =
**648,000,000** cells. Precomputing hundreds of millions of cells to serve a few hundred is not a
close call.

Bounding the precompute by radius does not rescue it either. Restricting to cells near a known city
shrinks the artifact, but with users worldwide, anyone rural, in a small town, or in a thinly-mapped
region falls outside the radius and resolves to blank - exactly the users worth seeing in telemetry.
Lazy resolution always resolves.

### The memo is a DoS vector

`GET /api/v1/forecast` is anonymous and unthrottled - there is no rate limiting or auth anywhere in
`src/TrmnlApi/`. An unbounded memo keyed by F2 cell can be walked through all 648M cells by a scripted
caller, and unlike the forecast cache there is no upstream call to slow it down. F2 offers 100x more
room to walk than F1 did, so the bound matters more, not less.

The memo must be bounded, and must not share the forecast cache's size budget (see
`WeatherCacheOptions.SizeLimit`): a place lookup must never be able to evict forecasts.

The same rule applies to the forward-geocoding memo in
[place-input.md](place-input.md#quota-and-abuse), and applies harder: its keys are unbounded free
text rather than a grid, so it additionally needs negative caching and an input length cap.

## Packaging: a project, not a service

Put this in `api/src/TrmnlApi.Geo` as its own project in the solution. Do **not** deploy it as a
separate service.

A network hop would contradict the synchronous in-path decision, turning a memo hit into a round-trip
on the forecast path. Worse, it would add a failure mode to the one thing that must not have one:
in-process, the worst case is a blank record; over a network it is timeouts, restarts, and cold
starts, forcing the call to become non-blocking with a fallback - which lands right back at "tag
unknown and move on". The scale does not justify it either. A few hundred distinct cells over a
container's lifetime is a lookup table, not a service. And a second container fights the deploy model:
the API runs a single pinned replica specifically to keep its cache warm, and a second service means
a second thing billed, healthchecked, deployed in lockstep, and monitored.

A separate project gets the organizational benefits - a clean seam, testable without the web host, a
swappable data-loading strategy behind an interface - at none of the operational cost. It is also the
easiest possible thing to extract later if a second consumer, a language change, or an independent
data-update cadence ever makes extraction worthwhile.

### Keeping the polygons out of RAM

The real risk in bundling Natural Earth is memory, not deployment. ~4,600 admin-1 multipolygons held
as live NetTopologySuite geometries is plausibly 60-100 MB resident, on a container that also holds
the forecast cache. **Measure this before committing to the approach.**

The fix is not a separate service, which only moves the memory somewhere it costs more. The fix is to
stop holding polygons in RAM. Ship the data as a **SQLite file with an R-tree index**, bundled in the
image:

1. The R-tree returns the handful of admin-1 polygons whose bounding boxes contain the point,
   typically one to three.
2. Only those polygons are decoded from a blob column, and exact point-in-polygon runs on them.
3. Resident memory stays flat. The OS page cache holds the few pages actually touched, and the
   working set is a few hundred cells.

The same file serves the nearest-city query. Cost is image size (roughly 20-50 MB) and a build step
to produce the file. Simplifying the polygons at build time with mapshaper is the other lever, and it
costs about the error already being accepted at 50m.

## PII

The place labels are a deterministic function of coordinates the span already carries in coarsened
form, and the *emitted* fields are all coarser than F1 - a city name locates a user less precisely
than `42.4,-71.1` does. Reading F2 to produce them introduces no leak, because what leaks is bounded
by what is emitted, not by what is read.

Three real constraints, though:

- **The tags are coupled.** If the coordinate tags are ever coarsened or dropped to tighten the
  privacy threshold, a place label silently becomes the finest location signal on the span and
  defeats that change. Coarsen them together.
- **The aggregate is a user-location dataset.** This is a public plugin with hundreds of users, and
  TRMNL devices sit in homes, so these are home locations. If any users are in the EU, GDPR applies
  to the aggregate. This argues for treating the ISO codes as the primary fields and `city` as
  optional.
- **Direct log submission skips scrubbing.** It does not get the Agent's sensitive-data scrubbing
  (see [observability.md](observability.md#logs)), so anything added to a log line has to respect the
  same F1 ceiling. The place fields do.

One cosmetic consequence of resolving from F2: two spans can carry identical F1 coordinate tags and
different city labels, because the labels were derived from finer input than the tags show. That is
more accurate, not less, but it means the city is not reproducible from the tagged coordinates alone.

### Dataset licensing

Natural Earth is public domain. GeoNames is CC BY 4.0 and **requires attribution**, so using
`cities15000` for the city label means shipping a NOTICE file in the image.

Existing NuGet options were checked and rejected: `ReverseGeocoder` (0.3.0) and
`Wibci.CountryReverseGeocode` are both low-activity and neither bundles its data.

## Open question this would answer

The forecast cache keys on **F2** (~1.1 km, `WeatherCache.CacheKey`) while telemetry emits **F1**
(~11 km). Nothing tags F2, so the number that decides whether nearby users share a cache entry -
distinct F2 keys per F1 cell - is structurally invisible today.

`weather.city` measures it directly: users per city *is* the sharing potential. Until then the only
read is indirect, via the raw `FreshFetch` / `FreshHit` counters at `GET /metrics`. A hit rate at or
below what the sub-hourly refresh cohort alone would produce implies F2 fragmentation is preventing
sharing, and that the cache key grain should move to F1 (or 0.05 degrees as a compromise for coastal
and mountain users, where 11 km spans a real gradient).

[place-input.md](place-input.md) attacks the same problem from the other end: every user who types
`Boston` converges on one canonical coordinate and therefore one cache entry, where today each types
slightly different numbers and lands in a private F2 cell. Place input may improve the hit rate
enough that regrinding the cache key becomes unnecessary.

Note that `FreshTtl` (45 min, set in Railway) is deliberately below the 60 min default
refresh interval: an hourly device never self-hits and always gets a live fetch, which keeps
worst-case staleness on screen at 60 minutes rather than ~125. The cache exists for sub-hourly
refreshers and for nearby users, not for the default cadence. Do not "fix" it by raising it above
60 min.

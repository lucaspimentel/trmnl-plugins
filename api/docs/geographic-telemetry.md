# Place names from coordinates (design, not yet implemented)

**Status: proposed, decisions made.** Nothing in this document is in the code yet. The tags described
under [What to emit](#what-to-emit) do not exist; the only location data on a span today is
`weather.coord` / `weather.latitude` / `weather.longitude`, all `F1`, as listed in
[observability.md](observability.md). On the response side, today's `place` block comes from
Open-Meteo's forward geocoding and is **omitted entirely** when the caller sent coordinates, so a
coordinate user currently sees no location on screen at all.

Bundled geographic data serves two goals, one user-facing and one internal:

1. **Show the resolved location on screen**, for every input shape rather than only for the users
   who typed a place name.
2. **Group forecast telemetry by place** - country, subdivision, city - instead of by a numeric
   coordinate, so a dashboard can answer "which regions see provider failures" or "where are the
   users" without a human decoding coordinates.

Both read the same lookup. They differ in what they do with the result, and the difference matters
enough to state once rather than leave to be inferred: see
[Display and telemetry are separate decisions](#display-and-telemetry-are-separate-decisions).

Related: [place-input.md](place-input.md), which covers the other direction (a user-supplied place
name to coordinates) and specifies the v2 API. That one was built first, deliberately, and this
document assumes it has shipped.

## The two use cases

Every forecast request resolves to a latitude/longitude pair before anything else happens. Only the
first step differs between the two input shapes:

| Step | Case 1: input is coordinates | Case 2: input is a place name or postal code |
|---|---|---|
| 1. Get coordinates | Parse them. **No geocoding request** | **Open-Meteo forward geocoding**, as today |
| 2. Fetch the forecast | By coordinates | By coordinates - identical |
| 3. Get a display name | Our own data | Our own data, **except `name`** - see below |
| 4. Emit telemetry | Our own data | Our own data |

Open-Meteo's geocoding API keeps exactly one job: **turning a user's typed place into coordinates**.
Every name and code that reaches a screen or a span comes from bundled data instead.

### Except the name the user typed

Step 3 is not quite symmetric, and making it symmetric would be a regression.

In case 2 the user typed `Stoneham, MA`. If the display name came from our nearest-place lookup, the
screen could read `Melrose` - not wrong, since it may genuinely be the nearest populated place to
those coordinates, but not what they asked for either. It reads as a bug, and it defeats the reason
the block exists: confirming that *the thing you asked for is the thing you got*. Substituting a
different nearby name breaks that confirmation exactly when a user most needs it.

So the rule is a hybrid, and it is the same rule this document already applies to `weather.city`:

| Field | Case 1 (coordinates) | Case 2 (place name) |
|---|---|---|
| `name` | Nearest populated place, from our data | **The geocoder's matched name** - the place the user named |
| `admin1` / subdivision | Our data | Our data |
| `country` / `country_code` | Our data | Our data |

That fixes the subdivision defects below on both paths without ever renaming a user's own input.

### What the input_kind reading is still for

[place-input.md](place-input.md) item 13 reads the `weather.input_kind` split on v2 traffic. That
reading was once the gate on whether this work happened at all, back when it served coordinate input
only. It is not any more: case 2 needs the subdivision codes just as much as case 1 does, so the
split now sizes the benefit rather than deciding it.

It is still worth reading, with the same two caveats as before. Read it on **v2 traffic only**,
identified by the route - v1 takes coordinates and nothing else, and forks keep it alive, so an
unfiltered reading measures how many installs have upgraded rather than what anyone prefers. And read
it from the **plugin push onwards**, because v2 served production for a short window before the
plugin moved, during which every install still sent coordinates from the old template.

## Why our own data rather than the geocoder

The obvious cheaper design is to ask a hosted service to turn coordinates back into a place, rather
than bundling polygons and a city list. The service already holds a paid Open-Meteo geocoding
subscription and already has the memo and negative-caching machinery built for the forward direction,
so a reverse endpoint would be a small addition.

It is rejected on **data quality**, not on cost or latency, and specifically on the quality of what
this vendor returns:

- **`admin1` is a display name, never a code.** Open-Meteo returns `Massachusetts` with a GeoNames id
  attached and no ISO 3166-2 field at all, verified against the live API. Codes are not a nicety
  here; see [The 18-character problem](#the-18-character-problem).
- **Subdivisions go missing.** Puerto Rico has been observed coming back with no `admin1` at all.
  Whatever the cause, a territory silently losing its subdivision is the class of defect a bundled
  dataset with a real `iso_3166_2` field does not have.

A reverse endpoint from the same vendor would inherit both, and switching vendors trades one opaque
dependency for another. Bundling is what actually buys control over the fields. The same bundle then
serves telemetry, which needs ISO codes that no forward geocoder supplies at all.

## What the user sees

The `place` block already exists in the v2 response and the title bar already renders it
(`plugins/weather/src/shared.liquid:456`, guarded by `{% if place %}`). Two things change, and
**neither is a template change**:

1. The block gets populated for coordinate input, where it is omitted today.
2. `admin1` carries a code rather than a display name.

### The 18-character problem

The title bar appends the subdivision only when the combined label fits in 18 characters
(`shared.liquid:455`):

```liquid
{% assign with_admin1 = place.name | append: ", " | append: place.admin1 %}
{% if with_admin1.size <= 18 %}{% assign place_label = with_admin1 %}{% endif %}
```

`Boston, Massachusetts` is 21 characters, so it silently renders as **`Boston`**. The same happens
for Pennsylvania, North Carolina, and every other long subdivision name. `Boston, MA` is 10 and fits.

This is why codes are load-bearing rather than cosmetic. The ambiguity mitigation that `Place.cs`
describes as the entire reason for the block - letting someone see they got Portland, Maine rather
than Portland, Oregon - is not working today for a large share of US users, because the state is
precisely the part being dropped.

## Display and telemetry are separate decisions

The two goals read one lookup but are not one decision. They have different accuracy thresholds and
different privacy consequences.

| | Display | Telemetry |
|---|---|---|
| Audience | The one user whose location it is | An aggregate dataset, retained and queried |
| A wrong-but-plausible name | **Actively harmful**: it is believed, so the mitigation misleads | Grouping noise |
| A blank result | Acceptable - renders nothing, exactly as today | Acceptable - an unfilterable bucket |
| Privacy weight | None. Showing users their own location is not an emission | Real. See [PII](#pii) |

The consequence that matters: **a field may be rendered without being tagged.** `weather.city` can be
withheld from telemetry on privacy grounds while `place.name` still reaches the screen, because
showing someone their own location is not the same act as retaining hundreds of users' locations in a
queryable store. An earlier draft conflated these and concluded city should be "optional". It is
optional as a *tag* and mandatory as a *display field*.

The other consequence: **display sets the accuracy bar**, and it is the higher of the two. A name
that is merely near enough to group by is not near enough to show.

## Decisions

| Question | Decision |
|---|---|
| Purpose | A user-facing display name **and** telemetry grouping, from one lookup |
| What Open-Meteo still does | Forward geocoding only: a typed place to coordinates |
| Display `name`, case 2 | The **geocoder's matched name**, not our nearest place |
| Codes and subdivision | Always bundled data, both cases |
| Input precision | **F2** (~1.1 km), the already-snapped orchestrator values |
| Country + subdivision | Point-in-polygon against Natural Earth **50m admin-1** |
| City | Nearest populated place from GeoNames, at the grain set in [How to resolve a coordinate](#how-to-resolve-a-coordinate) |
| No match (ocean, unmapped) | Snap to nearest within a bounded radius, else blank |
| Timing | **Synchronous, in the request path**, behind a memo and a time budget |
| Surfaces | The v2 `place` block, span tags, and the `ForecastServed` log line. No span-based metric today |
| Packaging | In-process project `TrmnlApi.Geo`, **not** a separate deployed service |

## This is not about making the geomap work

An earlier draft of this document claimed a Datadog geomap cannot be driven by a coordinate. That is
true only of **metric** queries. Log Events geomaps do accept latitude/longitude, and the map is
already built and working off the `ForecastServed` log line. Commit `a3bcfef` ("Plot weather request
locations on a map") succeeded; it was not blocked on ISO codes.

So the map is done, and the reasons to do this work are the other ones. The first of them is not a
telemetry reason at all:

- **Showing the location on screen**, for coordinate users who see nothing today and for everyone
  whose subdivision is currently dropped. See [What the user sees](#what-the-user-sees). This is the
  goal that makes the work worth doing on its own.
- **Readable facets and top-lists.** Trace search grouped on `weather.city` beats grouped on
  `42.4,-71.1`.
- **Cardinality, if a span-based metric is ever added.** A metric cannot take a coordinate, and
  grouping on `country_code` collapses a few hundred distinct cells into a few dozen values. See
  [Cardinality budget](#cardinality-budget).
- **Measuring cache-sharing potential** falls out of it, but is explicitly **not** a motivation. See
  [A side effect: cache sharing becomes visible](#a-side-effect-cache-sharing-becomes-visible).

## Where the coordinates come from

Coordinates reach the API two ways now: a user who typed a pair into the Location field, and a user
still on the deprecated `latitude` / `longitude` fields (`plugins/weather/src/settings.yml`). Neither
carries a place name to piggyback on, so a lookup from coordinates is genuinely required. The third
way - a typed place name - has a name already, which is what the hybrid rule in
[Except the name the user typed](#except-the-name-the-user-typed) preserves.

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
code fields still come from the polygon lookup either way. This is the same hybrid the display side
uses, deliberately, so a span and a screen never disagree about where a user is: see
[Except the name the user typed](#except-the-name-the-user-typed).

### Surfaces

Three surfaces now, not two: the **`place` block in the v2 response**, the span tags, and the
`ForecastServed` log message. The response block is the user-facing one and is the reason the
accuracy bar is where it is. Adding them to the log line
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
| Dataset, city | GeoNames `cities1000`, bundled in the image - see [City grain is set by display](#city-grain-is-set-by-display) |
| Country / subdivision | **Point-in-polygon** |
| City | Nearest populated place, display-only |
| Fallback | Nearest polygon or place **within a bounded radius**, else blank. Radius differs by surface: generous for codes, tight for a displayed city name |
| Memo key | Packed long from the F2 cell, `(latE2 + 9000) * 36001L + (lonE2 + 18000)` |
| Memo storage | Bounded, in its **own** `MemoryCache` instance |
| Failure | Non-throwing **and time-budgeted**. A lookup must never fail *or* delay a forecast; on either, return a blank record |

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
  ~26,000 in GeoNames `cities15000` and ~150,000 in `cities1000`. The city label has to come from
  GeoNames or rural users all resolve to the same handful of metros - which was tolerable when this
  was a grouping key and is not once it is on a screen.

### City grain is set by display

`cities15000` was chosen when the city label was telemetry only. For grouping traces, "nearest
populated place within 50 km" is acceptable noise. On a title bar it is a visible error, and a
plausible wrong name is worse than no name at all: the user reads `Boston`, believes it, and the
ambiguity mitigation has actively misled them rather than merely failed.

So go finer. GeoNames publishes `cities15000`, `cities5000`, `cities1000` and `cities500`;
`cities1000` is roughly 150,000 rows, which is nothing behind an R-tree and still small enough to
bundle. Revisit the fallback radius at the same time - 50 km is far too generous for something a
human reads, even if it remains reasonable for assigning a country code.

The two surfaces may legitimately disagree here: a distant nearest-city is good enough to tag and
not good enough to render. Blank on screen is an acceptable outcome, since the title bar already
renders nothing when `place` is absent.

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

Coastal cells are also where the display and telemetry radii pull apart hardest. A cell 30 km off
shore can still be tagged with the right country, and should be; naming a city for it on screen is a
guess the user has no way to check.

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
  to the aggregate. This argues for treating the ISO codes as the primary **tags** and `weather.city`
  as the optional one. It says nothing about `place.name`: rendering users their own location is not
  a retained dataset, so the display field is unaffected by this constraint. See [Display and
  telemetry are separate decisions](#display-and-telemetry-are-separate-decisions).
- **Direct log submission skips scrubbing.** It does not get the Agent's sensitive-data scrubbing
  (see [observability.md](observability.md#logs)), so anything added to a log line has to respect the
  same F1 ceiling. The place fields do.

One cosmetic consequence of resolving from F2: two spans can carry identical F1 coordinate tags and
different city labels, because the labels were derived from finer input than the tags show. That is
more accurate, not less, but it means the city is not reproducible from the tagged coordinates alone.

### Dataset licensing

Natural Earth is public domain. GeoNames is CC BY 4.0 and **requires attribution**, so shipping a
NOTICE file in the image is unconditional: the city label is a display field now, not a tag that
could be dropped on privacy grounds, so there is no version of this design that omits GeoNames.

Existing NuGet options were checked and rejected: `ReverseGeocoder` (0.3.0) and
`Wibci.CountryReverseGeocode` are both low-activity and neither bundles its data.

## A side effect: cache sharing becomes visible

Recorded because it is true and easy to forget, **not** as a reason to build any of this. The cache
grain is settled: F2 for requesting and caching, F1 for telemetry and logging. Nothing below is a
proposal to change it.

The forecast cache keys on **F2** (~1.1 km, `WeatherCache.CacheKey`) while telemetry emits **F1**
(~11 km). Nothing tags F2, so the number that decides whether nearby users share a cache entry -
distinct F2 keys per F1 cell - is structurally invisible today.

`weather.city` would measure it directly, for free, once it exists: users per city *is* the sharing
potential. Until then the only
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

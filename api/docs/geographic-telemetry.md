# Bundled geographic data

**Status: live on staging, verified. Production untouched.** `api/src/TrmnlApi.Geo` serves both
directions - a typed place to coordinates, and coordinates to a label - from one bundled SQLite
file, built by `api/tools/GeoDataBuilder` and fetched into the image by pinned URL and sha256
(`api/Dockerfile`). The tags under [What to emit](#what-to-emit) are live, alongside the `F1`
coordinate tags listed in [observability.md](observability.md).

**What's actually running on staging right now:** the dataset. The build log shows the real
`.gz` URL inside the fetch step and `/opt/geo/geo.sqlite: OK` from the checksum, which is the only
proof that matters - see the note under step 5 for why a green deployment is not.

Verified against `https://trmnl-plugins-staging.up.railway.app/api/v2/forecast` on 2026-08-29:

| Request | `place` |
|---|---|
| `?place=42.36,-71.06` | `Boston, MA, US` - was `null` before the dataset, the reason coordinate users saw no location at all |
| `?place=Boston` | `admin1: "MA"`, the ISO code the vendor never returns. It answered `"Massachusetts"` the day before |
| `?place=02180&country=US` | `Stoneham, MA` |
| `?place=02180` | `Guri-si, Seoul, KR` - unchanged for anyone who has not set a country, which is the point of the setting being a preference |
| `?place=1.87,-157.4` | `Banana, Kiribati, KI` with **no** subdivision code, rather than the invented `KI-X01~` |

**Nothing has been touched on `production`**: its `GEO_DATA_URL` and `GEO_DATA_SHA256` are still
unset, so it is still serving the vendor-geocoder degrade path.

### The first build (2026-08-28)

The four upstream files were fetched and the builder run for the first time. It produced
`geo.sqlite` at **111.5 MB**: 4,595 admin-1 features (381,377 points), 3,865 subdivision names,
170,856 cities with 723,911 aliases, and 1,080,715 postal rows, in 12 seconds.

Three things the first real run settled:

**The builder could never have worked as written.** `Shapefile.ReadAllFeatures` defaults to a
strict polygon builder, and Natural Earth 10m admin-1 contains rings it rejects outright - it threw
on the first one. It now reads with `GeometryBuilderMode.QuickFixInvalidShapes`, which repairs ring
orientation and hole nesting. The alternatives are both worse: `SkipInvalidShapes` silently drops
the offending subdivision, which is the exact defect class this document says bundling exists to
prevent, and `IgnoreInvalidShapes` skips validation so a hole can be built as a shell, quietly
corrupting every point-in-polygon answer inside it.

**The ~10-25 MB size estimate was about the wrong thing.** The polygons are **3.0 MB** - the
simplify and trim pass works better than estimated. The 111.5 MB is tabular: postal 23 MB, city
aliases 13 MB, cities 8 MB, and roughly 64 MB of indexes and two R-trees. `VACUUM` recovers
nothing. Step 4 below already anticipated "~100 MB", so the two figures were always describing
different things; the estimate under [Keeping the polygons out of
RAM](#keeping-the-polygons-out-of-ram) only ever covered geometry.

**Both defects that justified this project are fixed by the bundle**, confirmed through the runtime
reader rather than raw SQL: `42.36,-71.06` gives `US-MA` - the ISO code the vendor never returns -
and Puerto Rico keeps its subdivision as `US-PR`, the input the vendor drops entirely.

### Three defects the first artifact exposed

Real data broke assumptions that a fixture could not.

**A bounding box is worthless for a scattered feature.** Natural Earth ships Kiribati as a single
admin-1 feature whose islands straddle the antimeridian, so its box spans **349 degrees of
longitude**. The nearest-subdivision pass ranked candidates by box distance, which is zero anywhere
inside that box, so `0,-140` - open Pacific, about 1,100 km from the nearest Kiribati land -
returned country `KI`. It now ranks by distance to the polygon itself
(`PolygonBlob.DistanceKm`). Mid-ocean returns blank again, real Kiribati land still resolves, and
coastal near-misses still keep their state.

**The R-tree query did not wrap either**, which the fix above exposed rather than caused. Every
stored box has a longitude in [-180, 180], so a padded search box reaching past either end matched
nothing on the far side: a point at 179.9W found no country at all. Blank, not wrong, for everyone
in Fiji, Kiribati and the eastern edge of New Zealand. Both the subdivision and nearest-city
queries now split such a box into its two real ranges.

**Bare postal codes regressed against the vendor**, which is what the Country setting in
[place-input.md](place-input.md) now answers.

**Natural Earth's invented codes are now dropped rather than carried.** The layer assigns a code to
every territory with no ISO entry - `KI-X01~` for Kiribati, `-99-X11~` and country `-1` for
Somaliland - and passing those through would put them in a facet list beside real codes looking
exactly as authoritative, while being unusable as a group-by. The builder now stores **null** for
any code that is not a real ISO 3166-1 alpha-2 or ISO 3166-2: 188 subdivision codes and 12 country
codes, which also removes the `country` table's `-1` row, where a dozen unrelated territories had
been collapsed into one whose name was whichever was written last.

Null here means "this place has no ISO code", not "unknown". **The names are kept**, so those
places still resolve and still label themselves: Kiritimati reports no subdivision code and still
reads `Kiribati`, and Hargeisa reports no code in either column and still reads `Somaliland`. The
screen is unchanged, because `SubdivisionLabel` already preferred the name whenever the code was
not a clean alpha-2 suffix. Only the tags changed.

This made `admin1.iso_3166_2` and `admin1.iso_a2` nullable, so `GeoSchema.Version` is **2**. The
version is checked at boot, and no artifact has been published, so nothing in the wild reads the
old shape.

A **query grouping by `weather.subdivision` or `weather.country_code` must therefore expect the tag
to be absent**, and should fall back to `weather.subdivision_name` or `weather.country` rather than
treating the group as missing data.

Separately, and **deliberately not changed**: four subdivisions have a real ISO code whose prefix
disagrees with the country column. Three are correct as they stand - `US-PR` with country `PR`, and
`NL-SX` with country `SX`, are both a territory holding its own alpha-2 - and nulling on that
signal would have destroyed the Puerto Rico case this project exists to fix. The fourth was the
editorial one, settled below.

### A disputed territory keeps its outline and loses its labels

Natural Earth files `UA-43` and `UA-40`, Crimea and Sevastopol, under country `RU` while carrying
Ukraine's own ISO codes. Printing either answer on a weather screen makes a claim this project has
no business making, and the only alternative on offer is to believe a different map. So neither is
printed: `ContestedTerritories` in the builder writes those two features with their geometry and
**no attribution at all** - no subdivision code, no country code, no subdivision name, no country.

**Deleting them would not have been the same as declining to answer.** The lookup falls through to
the nearest neighbouring polygon whenever nothing contains the point, so a deleted Crimea hands
Kerch to Krasnodar Krai four kilometres across the strait, and the rest of the peninsula to Kherson
- the same claim as before, arrived at by accident and wrong about the subdivision as well. The
regression test for this asserts the drift does not happen, and it fails with country `XA` against
a fixture where the outline is deleted rather than blanked.

The names being nullable is what makes this storable, so `admin1.admin_name` and
`admin1.subdiv_name` joined the two code columns in being nullable. `GeoSchema.Version` stays at
**2**: a reader that tolerates null reads an older artifact correctly, because the columns were
simply never null there. Only a change that would make a reader *misread* an old artifact bumps it.

Checked against the runtime reader on the rebuilt file. Simferopol, Sevastopol, Kerch and Yalta all
come back as the city name and nothing else - `empty=False`, because the city is still an honest
label - while Kherson stays `Kherson, UA-65, Ukraine` and Krasnodar stays `RU-KDA, Russia`.

### Open, and where to pick up (2026-08-29)

Staging serves the dataset and the smoke tests pass. What is left is a look at a real screen and
production. Nothing below is deployed-and-broken; the struck-through items are done, kept here
because the reasoning is worth more than the checkbox.

1. ~~**`TK - New Zealand` is wrong in the Country dropdown.**~~ Fixed. `TK` is Tokelau; the option
   list had been generated from the bundled `country` table, whose name column is Natural Earth's
   `admin`, and that names the *sovereign* for a territory. A check for names shared by two codes
   found `TK` was the only one this corrupts, so the two labels were corrected in `settings.yml`
   by hand and the list re-sorted rather than teaching the generator an override table:
   `TK - Tokelau`, and `SS - S. Sudan` spelled out as `SS - South Sudan`. `PS - West Bank` is
   Natural Earth's name for what ISO calls Palestine, and changing that one takes a position, so it
   is left alone deliberately.

   The `country` table itself still says `TK` is called New Zealand, and that is harmless:
   `ResolveQualifier` collects every code a typed name matches into a set, so `Sydney, New Zealand`
   widens to `{NZ, TK}` and still finds the New Zealand city. It never resolves to Tokelau instead.

2. ~~**Re-push the plugin.**~~ Done twice: once for the raw `{{ country }}` in `polling_url`, and
   again with the two corrected labels.

3. **Confirm on the device.** The API is verified by request, not by screen. With location `00784`
   and the country set, the title bar should read `Guayama, PR` where it read `Mokotów, MZ`. A
   forced refresh from the plugin settings page is the quickest way to see it. Attempted through
   the browser once and abandoned when the extension disconnected; the live `declared=US` line in
   the logs is the evidence standing in for it.

4. **Production is untouched** and still has no dataset: `GEO_DATA_URL` and `GEO_DATA_SHA256` are
   unset there. Promoting means setting both on the `production` environment, pushing to `main`,
   confirming the build log the same way as step 5 below, then `push-plugin.sh --env prod`.

5. ~~**Crimea and Sevastopol.**~~ Settled: they keep their outlines and carry no attribution at
   all. See [the section above](#a-disputed-territory-keeps-its-outline-and-loses-its-labels).

6. ~~**The uncompressed `geo.sqlite` on the release.**~~ Deleted from `geo-data-20260828`, which now
   carries only the `.gz`. The 2026-08-29 release was published with the `.gz` alone.

7. **Then wait about a week** and read `weather.geocoder`, per step 9. That reading is what licenses
   retiring the vendor geocoder, and it is the whole point of the exercise.

The `ForecastServed` log now carries `declared=`, the parsed country preference, which is what
would have answered the dropdown question above in one look instead of several.

### Rollout checklist (pick up here next session)

1. ~~**Get the four upstream files**~~ Done. `ne_10m_admin_1_states_provinces.shp` (+ `.dbf`/`.shx`)
   from Natural Earth, and `cities1000.txt`, `admin1CodesASCII.txt` and the *postal*
   `allCountries.txt` from GeoNames. Fetched to a scratch directory, not into this repo.
2. ~~**Run the builder**~~ Done, after the reader fix above.
3. ~~**Check the artifact size**~~ Done: 111.5 MB, of which 3.0 MB is geometry. See above.
4. ~~**Publish `geo.sqlite` as a GitHub release asset**~~ Done. The current one is
   [`geo-data-20260829`](https://github.com/lucaspimentel/trmnl-plugins/releases/tag/geo-data-20260829),
   the rebuild that blanks the disputed territories; `geo-data-20260828` is the first build and is
   kept for the image that shipped from it. The release notes carry the attribution this obliges us
   to give: Natural Earth is public domain, GeoNames is CC BY 4.0. Verified downloadable
   anonymously, which is what the image build does.

   | | |
   |---|---|
   | `GEO_DATA_URL` | `https://github.com/lucaspimentel/trmnl-plugins/releases/download/geo-data-20260829/geo.sqlite.gz` |
   | `GEO_DATA_SHA256` | `e078cebe70c1fcab850666a1dd145affcb44bb72c8eebf72b89952223a76a3d5` |

   **The asset is gzipped**: 45.7 MB against 111.5 MB, so a build pulls 66 MB less. The image is
   the same size either way, because it is unpacked during the build - this buys build time and
   release bandwidth, not disk.

   The image **decompresses before it checks**, so `GEO_DATA_SHA256` is the hash of the SQLite file
   the service opens, not of the wrapper. Changing the compression therefore does not change the
   hash, which was verified by round-tripping the first asset. A truncated download fails the
   checksum rather than leaving a database that opens and is quietly short.

   Only the `.gz` is published now. Both releases carried an uncompressed `geo.sqlite` at one
   point, and pinning that one by mistake fails the build at the `gunzip` step rather than shipping
   anything wrong - checked, along with the empty-URL and 404 paths - but it has been deleted
   anyway so the mistake is not available to make.

5. ~~**Set `GEO_DATA_URL` and `GEO_DATA_SHA256`**~~ Set on the **staging** service
   (`trmnl-plugins-api` project, `trmnl-plugins` service, `staging` environment).

   Setting a variable does not rebuild anything on its own, and **a Markdown-only commit does not
   either**: the service's watch patterns are `/api/**` and `!**/*.md`, so a docs push is reported
   `SKIPPED` and the old image keeps serving. Picking up a changed variable needs a code change
   under `/api` or a manual redeploy. Two docs pushes were skipped this way before that was
   understood.

   **A build started before those variables existed will not pick them up, and looks like a
   success.** The Dockerfile's fetch step is a no-op when the URL is empty, so the build log reads
   `RUN mkdir -p /opt/geo && if [ -n "" ]; then ...` and the image ships with no dataset. That is
   exactly what happened on the first attempt. Confirm a build actually fetched by looking for the
   real URL inside that `RUN` line in the build log, not by the deployment going green. The URL is
   substituted into the command, so changing it invalidates the layer cache on its own.
6. ~~**Re-run the smoke test** against staging~~ Done, 2026-08-29. Results in the table at the top
   of this document.
7. **Check the new Country dropdown on a real device** alongside the title bar: it is 247 options
   and has only been linted, never seen in the TRMNL settings UI. Then **check on a real device** (push the plugin to staging with `bash tools/push-plugin.sh
   plugins/weather --dry-run` first to review, then without `--dry-run`), confirm the title bar
   renders correctly for both a coordinate-based and a place-based install.
8. **Promote to production**: same two build args, on the `production` environment, then push the
   prod plugin.
9. **After about a week**, read `weather.geocoder` in the `ForecastServed` logs. A quiet
   `open-meteo` count is what licenses deleting `OpenMeteoGeocodingClient` and the paid Open-Meteo
   geocoding subscription - not before.

Several decisions in the original design note were **wrong**, and were corrected during
implementation by measuring against the live APIs and the actual datasets. Where this document now
disagrees with what it once said, the disagreement is marked. The corrections are: 50m polygons are
unusable rather than a tradeoff; the ISO country-name table is unnecessary; the memory estimate was
an artifact of the geometry library rather than of the data; and forward geocoding moved in-house
too, which the note had left with the vendor.

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
| 1. Get coordinates | Parse them. **No geocoding request** | **Bundled data first**, Open-Meteo only on a miss |
| 2. Fetch the forecast | By coordinates | By coordinates - identical |
| 3. Get a display name | Our own data | Our own data, **except `name`** - see below |
| 4. Emit telemetry | Our own data | Our own data |

Open-Meteo's geocoding API keeps exactly one job, and now only when the bundled data cannot do it:
**turning a user's typed place into coordinates**. Every name and code that reaches a screen or a
span comes from bundled data. See [Forward geocoding moved in-house
too](#forward-geocoding-moved-in-house-too).

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
| `name` | Nearest populated place, from our data | **The matched city's name** - the place the user named. A postal code matches no name, so it falls back to the nearest populated place |
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

1. The block gets populated for coordinate input, where it was omitted before.
2. `admin1` carries a short label rather than the vendor's display name. See [The display
   rule](#the-display-rule).

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
than Portland, Oregon - was not working for a large share of US users, because the state is
precisely the part being dropped. `Vancouver, British Columbia` (27) and `Sydney, New South Wales`
(23) truncate the same way.

### The display rule

**Corrects the note's "`admin1` carries a code".** A code is right for the US and wrong elsewhere:
56% of the subdivision codes in the 10m layer are alphabetic, and the rest are numeric. France and
Japan are numeric, so `FR-59` on a title bar reads `Lille, 59` and `JP-46` reads `Kagoshima, 46`. A
numeric code is worse than a name.

So `place.admin1` carries the best available **short** label, not raw ISO:

- The alphabetic ISO subdivision part when there is one: `US-MA` gives `MA`.
- Otherwise the subdivision display name: `FR-59` gives `Nord`, never `59`.

One field, **no `shared.liquid` change**, and the 18-character rule stays as the final guard. The
full ISO code still goes to telemetry, where numeric is both correct and readable.

The United Kingdom is the exception that is neither. The 10m layer has 232 GB features and they are
districts, not nations, so the code path renders `Cambridge, CAM`. GB is on a name-first list, which
gives `Cambridge, Cambridgeshire` - too long for the title bar, so it truncates to `Cambridge`.
Longer and honest beats short and meaningless. The implementation is
`TrmnlApi.Geo.SubdivisionLabel`.

## Forward geocoding moved in-house too

**This is new; the note left forward geocoding with the vendor.** Testing against the live API found
the vendor is the problem in three ways, not one, and the third is disqualifying:

| Input | Open-Meteo | Bundled data |
|---|---|---|
| `00784` (Guayama PR) | **No results** | Guayama, PR (17.98, -66.11) |
| `00784, PR` / `Guayama, PR` | **No results** | Guayama, PR |
| `Guayama` | Works, but `admin1='Guayama'`, so it renders **"Guayama, Guayama"** | "Guayama, PR" |
| `Munich, DE` / `Paris, FR` / `Toronto, CA` | **No results** | All resolve |
| `Munich, Germany` / `Toronto, Canada` | Works | Works |

Two-letter **country** qualifiers mostly fail, while two-letter **US state** qualifiers work. The
plugin's own placeholder is `Boston, MA` (`plugins/weather/src/settings.yml`), which teaches a
pattern that silently fails outside the US. A Puerto Rico user had **no working input form at all**.

A ~30-line ranker over `cities1000` - exact name or alias match, comma qualifiers filtered by
country code, country name, subdivision code and subdivision name, ranked by population -
reproduced **12 of 12** of Open-Meteo's answers, including `Portland` to Oregon, `Portland, ME` to
Maine, `Cambridge` to GB and `Munich` to DE. Coordinates agree: `Boston` gives 42.36,-71.06, which
is what `WeatherV2EndpointTests` already asserted.

### Local first, vendor as the fallback

Open-Meteo is called only when the local search finds nothing, and `weather.geocoder`
(`local` / `open-meteo` / `none`) records which path served. Nobody ends up worse off than before,
and the vendor retires when the fallback count goes quiet - the same measured retirement v1 gets.

This matters because **we do not log place inputs**, so there is no corpus to replay a ranking
regression against. The fallback *is* the safety net. It also covers the one thing the local
geocoder deliberately does not do: the vendor forgives some misspellings and exact-and-alias
matching does not.

### Postal codes supply coordinates only

Their place names are unusable as labels. `CA M5V` is
`"Downtown Toronto (CN Tower / King and Spadina / Railway Lands / ...)"` and `GB SW1A` is
`"Westminster Abbey"`, so the label always comes from the city lookup instead. The postal table
carries country, code and coordinates and nothing else.

GeoNames postal is 1,826,904 rows over 121 countries. Where one code exists in several countries -
`75001` is both the first arrondissement of Paris and Addison, Texas - the candidates rank by the
largest population within 15 km of the centroid, which picks Paris. The radius has to be small for
that to mean anything: at 60 km, Addison borrows Dallas and the comparison stops being about the
postal code at all.

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
| What Open-Meteo still does | Forward geocoding, and only when the bundled data misses |
| Display `name`, case 2 | The **matched city's name**, not our nearest place |
| Display `admin1` | The **short label**: alphabetic ISO part, else the subdivision name |
| Codes and subdivision | Always bundled data, both cases |
| Input precision | **F2** (~1.1 km), the already-snapped orchestrator values |
| Country + subdivision | Point-in-polygon against Natural Earth **10m admin-1**. 50m is unusable; see below |
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
| `weather.country_code` | ISO 3166-1 alpha-2, e.g. `US`. **Absent** for a territory with no ISO entry | NE admin-1 `iso_a2` |
| `weather.country` | display name, e.g. `United States of America` | NE admin-1 `admin` |
| `weather.geocoder` | `local` / `open-meteo` / `none` | which path resolved the input |
| `weather.subdivision` | ISO-3166-2, e.g. `US-MA`. **Absent** where there is no ISO code | NE admin-1 `iso_3166_2` |
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
| Dataset, country + subdivision | Natural Earth **10m admin-1** polygons, bundled in the image |
| Dataset, city | GeoNames `cities1000`, bundled in the image - see [City grain is set by display](#city-grain-is-set-by-display) |
| Country / subdivision | **Point-in-polygon** |
| City | Nearest populated place, display-only |
| Fallback | Nearest polygon or place **within a bounded radius**, else blank. Radius differs by surface: generous for codes, tight for a displayed city name |
| Memo key | Packed long from the F2 cell, `(latE2 + 9000) * 36001L + (lonE2 + 18000)` |
| Memo storage | Bounded, in its **own** `MemoryCache` instance |
| Failure | Non-throwing **and time-budgeted**. A lookup must never fail *or* delay a forecast; on either, return a blank record |

### One dataset covers three fields

`ne_10m_admin_1_states_provinces` carries `iso_a2`, `iso_3166_2`, `name` **and `admin`** on every
feature, so a single containment query yields country code, country name, subdivision code and
subdivision name together. A separate countries layer is unnecessary.

**Corrects the note's "static ~250-row ISO name table".** `admin` - the country display name - is
populated on all 4,596 features, so the table is redundant and is not built. The same column is what
resolves a spelled-out qualifier such as `Munich, Germany` to a country code.

### GeoNames codes are not a shortcut

An early idea was to skip polygons entirely and take the subdivision straight from the GeoNames
`admin1` column on the nearest city. That would have been US-only: **89% of GeoNames admin1 codes
are numeric** (`CA.01`, not `CA-ON`). The column is still carried, because it is what makes
`Portland, ME` resolve, but it cannot produce an ISO code. Polygons earn their place.

### Why two datasets and not one

The fields pull in opposite directions, which is why neither source alone is enough:

- **GeoNames `admin1 code` is not reliably ISO-3166-2.** It is ISO for some countries and
  FIPS-derived for others. Natural Earth's admin-1 layer has a genuine `iso_3166_2` field and
  GeoNames does not, so the subdivision *code* has to come from Natural Earth regardless of any
  border-accuracy argument.
- **Natural Earth is thin on cities.** ~1,250 populated places at 50m and ~7,350 at 10m, against
  ~26,000 in GeoNames `cities15000` and 170,856 in `cities1000` (28 MB, 82% of rows carrying
  alternate names inline). The city label has to come from
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

### 10m is the only option; 50m is unusable

**Corrects the note's "start at 50m, 10m is the upgrade path".** That framing presented a tradeoff
that does not exist. Counted directly:

| Layer | Features | Countries covered | `iso_3166_2` usable |
|---|---|---|---|
| `ne_50m_admin_1_states_provinces` | 294 | **9** (RU, US, IN, ID, CN, BR, CA, AU, ZA) | - |
| `ne_10m_admin_1_states_provinces` | 4,596 | **241** | 4,595 of 4,596 |

At 50m, every user outside those nine countries resolves to blank. That is not a resolution
tradeoff, it is missing data. 10m is the only option, its coverage is effectively complete, and
Puerto Rico is one feature carrying `US-PR`.

The geometry is 1,295,319 points, which is ~21 MB as float64 and ~10 MB as float32. Simplified at
build time to 0.01 degrees - finer than the grid the query point has already been snapped to - it is
smaller again.

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

**Corrects the note's "60-100 MB resident".** That figure was an artifact of NetTopologySuite's
object-per-coordinate model, not of the data: the 1,295,319 points in the admin-1 layer are ~10 MB
packed as float32. So NetTopologySuite is a **build-time** dependency of `GeoDataBuilder` only. The
runtime unpacks its own flat blobs (`TrmnlApi.Geo.PolygonBlob`) and never constructs a geometry
object.

The storage decision stands on its own merits anyway. Ship the data as a **SQLite file with an
R-tree index**, bundled in the image:

1. The R-tree returns the handful of admin-1 polygons whose bounding boxes contain the point,
   typically one to three.
2. Only those polygons are decoded from a blob column, and exact point-in-polygon runs on them.
3. Resident memory stays flat. The OS page cache holds the few pages actually touched, and the
   working set is a few hundred cells.

The same file serves the nearest-city query, the forward geocoder and the postal lookup. Cost is
image size and a build step to produce the file. `GeoDataBuilder` simplifies the polygons at build
time (Douglas-Peucker, 0.01 degrees by default), drops every column nothing reads, and vacuums; the
artifact is 60-120 MB without that trimming.

### The file layout

| Table | Contents |
|---|---|
| `admin1` | `iso_3166_2, iso_a2, admin_name, subdiv_name, geom` blob, plus an R-tree over bounding boxes |
| `country` | `iso_a2` to display name, derived from the admin-1 layer's own `admin` column |
| `admin1_name` | GeoNames division names, so `Portland, Oregon` resolves like `Portland, OR` |
| `city` | `name, normalized_name, country, admin1, lat, lon, population`, plus an R-tree and an alias table |
| `postal` | `country, code, lat, lon` only - no place names |

The schema lives in `TrmnlApi.Geo.GeoSchema` rather than in the builder, so the writer and every
reader are looking at one definition, and the tests build a small fixture database from it.

### Building and shipping the artifact

```bash
dotnet run --project api/tools/GeoDataBuilder -- --input <dir> --output geo.sqlite
```

The input directory needs `ne_10m_admin_1_states_provinces.shp` (with its `.dbf` and `.shx`),
`cities1000.txt`, `admin1CodesASCII.txt` and the postal `allCountries.txt`. Publish the result as a
GitHub release asset and point `GEO_DATA_URL` / `GEO_DATA_SHA256` at it in `api/Dockerfile`, which
verifies the checksum the same way the tracer tarball is verified.

Leave `GEO_DATA_URL` empty and the image still runs: `Geo__DatabasePath` finds nothing, the null
implementations are registered, every query goes to the vendor geocoder and no location is shown. A
service that will not boot without a 100 MB download is a worse outage than one that shows no
location.

**Nobody owns the refresh cadence.** Bundling means a data update rides a deliberate release, and no
one is named for checking Natural Earth or GeoNames revisions.

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

Natural Earth is public domain. GeoNames is CC BY 4.0 and **requires attribution**, so `api/NOTICE`
is copied into the image unconditionally: the city label is a display field now, not a tag that
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

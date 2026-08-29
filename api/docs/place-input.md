# Place input and API v2

**Status: shipped (items 1-11 below).** `/api/v2/forecast` is live on staging and production, and
the plugin pushed to both sends `place`. Every non-forked install moved to v2 when the plugin was
pushed. What is left is items 12-14, all gated on reading the telemetry this rollout produces.
`GET /api/v1/forecast` is unchanged: it still requires `latitude` and `longitude` as separate
numeric query parameters (`Endpoints/RequestValidator.cs`) and returns plain text on every error
path (`Endpoints/WeatherEndpoint.cs`), which was reconfirmed against production after v2 shipped.

The Open-Meteo geocoding behaviour described below was checked against the live API rather than
assumed, including the customer host, which was confirmed working through the deployed staging
service.

The goal is to let a user identify their location however they naturally would - a city name, a
postal code, or a pasted coordinate pair - instead of looking up two decimal numbers before the
plugin will work.

Related: [geographic-telemetry.md](geographic-telemetry.md), which covers the inverse problem
(coordinates to place names for telemetry). The two features meet in the middle; see
[How the two features layer](#how-the-two-features-layer).

## Plan

Decisions and unverified items are recorded in full elsewhere in this document; this table tracks
status only.

| # | Item | Status |
|---|---|---|
| 1 | Error shape scope: geocoding failures only, or also the 502 when all providers fail | Settled: every device-visible failure, including `weather_unavailable` |
| 2 | Whether `provider` and `fake` carry over to v2 | Settled: `provider` kept as-is; `fake` stayed v1-only and is reached on v2 through a test scenario instead - see [Test scenarios](#test-scenarios) |
| 3 | v2 schema specifics: `place` block fields, stable error codes | Settled: see [Response shape](#response-shape-v2) |
| 4 | Verify the `customer-geocoding-api.open-meteo.com` host | Verified against the deployed staging service |
| 5 | Whether a removed `custom_field`'s stored value still interpolates into `polling_url` | Unverified, not blocking: the plan keeps the fields declared |
| 6 | `PlaceResolver` - input sniffing | Done, as `PlaceInput.Parse` (pure sniffing) plus `PlaceResolver` (the async lookup and memo) |
| 7 | Geocoding client | Done: free/customer switch, absent-`results` handling, `PPL*` filter, bounded memo with negative caching |
| 8 | v2 endpoint and response models | Done: `/api/v2/forecast`, verified end-to-end on staging and again on production |
| 9 | `weather.input_kind` span tag | Done, plus full error tagging; both tags and the two spans that originally carried them were later consolidated onto the request's own span - see [Telemetry](#telemetry) |
| 10 | Plugin v2: `settings.yml` gains `place`, template renders the place block and the error | Done. `place`, `latitude` and `longitude` are all `optional: true`, so either form can be given and coordinates can be cleared; the coordinate fields are labelled deprecated - see [Migrating the plugin](#migrating-the-plugin). A Show Location setting was added on the same route afterwards, since the matched place is reassurance a user only needs until they have had it - see [The `place` block](#the-place-block) |
| 11 | Ship staging, then production, then push the plugin | Done, in that order, and confirmed on hardware: every test scenario was stepped through on a real screen from the plugin's Place setting |
| 12 | Watch v1 route traffic; retire the endpoint when it goes quiet | Now measurable. Whatever still reaches v1 is fork traffic, since every non-forked install has moved |
| 13 | Read `weather.input_kind` on v2 traffic; decides whether the polygon work happens at all | Now measurable, but only from the plugin push onwards. Before it, installs sent coordinates from the old template, so earlier v2 traffic reads `coordinates` regardless of what users would choose |
| 14 | If it does: measure Natural Earth memory first, then `TrmnlApi.Geo` and the SQLite R-tree | Deferred, gated on data |

## Decisions

| Question | Decision |
|---|---|
| Input shape | A **single** free-form `place` parameter |
| Coordinates | Detected by parsing, not by a separate parameter |
| Geocoder | **Bundled data first**, Open-Meteo forward geocoding on a miss. Originally the vendor alone; see [geographic-telemetry.md](geographic-telemetry.md#forward-geocoding-moved-in-house-too) |
| Ambiguity | **Take the most prominent match.** Qualifiers typed into `place` narrow it, and a **Country** setting settles what is left |
| Errors | HTTP **200** with a renderable error in the body, for **every failure the device can see** |
| Error body | A stable `code`, a human `message`, and an actionable `hint` |
| Response shape | A nested **`place`** object beside the existing forecast keys |
| Debug parameters | `provider` carries over from v1 unchanged. `fake` does not: v2 selects it, and every other result, through a `place` sentinel |
| Versioning | New **`/api/v2/`** endpoint. `/api/v1/` is frozen, and retired once fork traffic stops |

## Why a new version rather than extending v1

The plugin is public and has been forked. A fork gets its own copy of `settings.yml`, with
`latitude={{ latitude }}&longitude={{ longitude }}` and a v1 URL baked in, and receives no further
updates. There is no mechanism to change a fork's settings and no way to know who forked it, so those
devices keep polling v1 for as long as they stay installed.

Non-forked installs upgrade automatically, so they move to v2 on their own. That makes v1 **retirable
rather than permanent**: it lives until fork traffic stops. Watch the v1 route and remove the endpoint
once it goes quiet. The only judgement call is how long quiet has to be, since a dormant fork looks
exactly like a dead one. Until then v1 should stay a thin edge over shared internals: see
[Sharing internals](#sharing-internals-across-versions).

Versioning also buys the freedom to change the response schema, which the error-shape decision below
requires and which v1 cannot absorb without breaking the forks' Liquid templates.

## Input: one field, sniffed

v2 takes a single `place` parameter and decides what it is by parsing it. A coordinate pair is just
one of the things `place` accepts, so no separate numeric parameters are needed for new users.

v2 does still accept `latitude` and `longitude`, but only as a transition affordance for installs that
upgrade with coordinates already saved: see [Migrating the plugin](#migrating-the-plugin). `place`
wins whenever it is present and not blank, whitespace included; the coordinate parameters are
consulted only when it is absent or empty. Both should be removed once telemetry shows nobody is
arriving with coordinates alone.

| Input | Detected as | Path |
|---|---|---|
| `42.35843	-71.05977` | coordinates | parsed directly, no geocoding call |
| `42.35,71.05` | coordinates | parsed directly, no geocoding call |
| `42.35, -71.05` | coordinates | parsed directly, no geocoding call |
| `02180` | not coordinates | bundled search, Open-Meteo on a miss |
| `Boston, MA` | not coordinates | bundled search, Open-Meteo on a miss |
| `SW1A 1AA` | not coordinates | bundled search, Open-Meteo on a miss |

The rule: normalize whitespace, split on `[,\s]+`, and treat the value as coordinates only when there
are **exactly two tokens, both parse as invariant-culture doubles, and both fall in range** (latitude
-90 to 90, longitude -180 to 180). Everything else goes to the geocoder.

That is safe against all of the above. A bare postal code is one token. A UK postcode is two
non-numeric tokens. A name with a comma splits into non-numeric tokens. Parse invariant-culture only:
accepting a comma as a decimal separator would make `42,35` ambiguous with a coordinate pair.

Sniffing correctly is not the same as resolving. `SW1A 1AA` parses as a name and reaches the
geocoder, which returns no match - Open-Meteo does not index UK-style alphanumeric postcodes. It is
in the table as a parsing example, not a working input.

Coordinate input costs no geocoding call, so the parse order also keeps existing-style usage off the
Open-Meteo quota entirely.

### The coordinate-order hazard

Separate `latitude` and `longitude` fields make an order mistake almost impossible. A single field
invites one, and it cannot be detected: both orderings are valid coordinates. `42.35,71.05` is a
point in Kazakhstan, and a user who meant Boston gets a plausible-looking screen showing the wrong
continent's weather.

This is accepted, not solved. It compounds with the ambiguity decision below, so both silent failure
modes should be called out in the plugin's field description and README rather than only here.

## Resolution

1. Normalize the input: trim, casefold for the memo key, collapse internal whitespace.
2. Probe for coordinates per the rule above. If it parses, resolution is done.
3. Otherwise search the bundled dataset, which ranks by population and honours both a typed
   qualifier and the caller's `country`. On a miss, call Open-Meteo forward geocoding and take
   the first result.
4. Snap the resulting coordinates to F2, exactly as `WeatherForecastOrchestrator` already does.
5. Enter the existing cache and provider path unchanged.

Step 4 must happen **before** the forecast cache lookup. Resolve afterwards and the cache fragments by
input form; resolve before and every user who typed `Boston` converges on one cache entry.

### Client

Mirror `OpenMeteoClient`: it already switches between a free base URL and a `customer-` prefixed one
based on whether `OPEN_METEO_API_KEY` is set, and appends `apikey` to the query
(`Services/OpenMeteoClient.cs`). The geocoding equivalents are `geocoding-api.open-meteo.com/v1/search`
and `customer-geocoding-api.open-meteo.com/v1/search`. Both are confirmed working: the free host
directly, and the `customer-` one through the deployed staging service, which has the API key set.

Postal codes resolve through the same `search` call, confirmed against the live API. No separate
postal dataset is needed.

Two response details that will bite an implementation:

- **On no match the `results` key is absent entirely**, not an empty array. A query for `zzzzqqqq`
  returns `{"generationtime_ms": ...}` and nothing else, so deserialization has to treat the property
  as missing rather than as empty.
- **Not every result is a populated place.** `Portland, ME` returns the city first, then `Portland
  Point` (`CAPE`), `Cushing Island` (`ISL`), the airport (`AIRP`) and a historic district (`PRK`).
  Taking the first result blindly can hand back a headland for some inputs, so filter to `PPL*`
  feature codes before picking. Verified against the live API that this keeps everything wanted:
  cities arrive as `PPL`, `PPLA`, `PPLA2` or `PPLC`, and a postal code resolves to a plain `PPL`
  (`02180` returns Stoneham, Massachusetts).

### Ambiguity: first result wins

Open-Meteo returns a ranked list, and taking the top of it is effectively "most prominent match".
Verified against the live API: `Portland` resolves to Oregon (population 652,503) ahead of Maine, and
`Springfield` resolves to Missouri ahead of Illinois.

The request still asks for more than one result, because the ranking interleaves populated places
with the headlands and airports above: `count=1` would return whatever ranked first regardless of
feature code, leaving the filter nothing to choose from.

This is deliberate. It never errors, needs no qualifier syntax, and keeps the screen populated. Two
things make it tolerable, and both are cheaper than a disambiguation UI.

**Qualifiers already work, for free.** The search is full-text, so `Portland, ME` and
`Portland, Maine` both return Portland, Maine ahead of the much larger Oregon one. No syntax, no
parsing, no extra parameter on this side - it only needs saying in the plugin's field description.
That is the primary mitigation.

**The resolved place is shown on screen.** The plugin renders what the API actually picked (see
[Response shape](#response-shape-v2)), so a user who gets the wrong Portland can see it rather than
inferring it from a suspicious temperature.

#### Postal codes collide across countries

Worse than the city case, and also verified: `75001` returns **Paris, France** ahead of Addison,
Texas. A user in Addison typing their own ZIP gets French weather, and unlike an ambiguous city name
a postal code feels unambiguous, so nothing prompts them to qualify it.

The same two mitigations apply - the rendered place name catches it, a country qualifier fixes it -
but this one is worth calling out in the field description rather than leaving for a user to
discover.

##### Resolved by a Country setting, once the data was bundled

The vendor ranking above is not what the bundled dataset does, and the difference is a regression
rather than an improvement. The local geocoder ranks colliding codes by the largest population
sitting on them, which sends `02180` to **Seoul** rather than to Stoneham, Massachusetts. The
collision is much wider than the vendor's behaviour suggested: `02180` is a real code in six
countries, `10001` in ten, and **88% of US ZIP codes** are shared with at least one other country.

Three fixes were considered and rejected. Ranking differently only moves which country loses.
Returning a miss on an ambiguous code preserves today's answers exactly, but sends 36,361 of the
41,488 US ZIPs to the vendor on every cache miss, which is the cost the bundling exists to avoid.
Inferring the country from the `units` setting makes the same typed input resolve differently for
a US user who prefers metric.

So the plugin asks. A **Country** dropdown supplies `country=<alpha-2>` on v2, and it is a
*preference rather than a filter*, which is the whole of the design:

| Case | Behaviour |
|---|---|
| Country unset, `Auto`, or unparseable | Ranked by population, exactly as before. This is what every install predating the setting and every fork sends |
| Code exists in the declared country | That country wins outright |
| Code exists nowhere near it - `Tokyo` with `US` declared | Still resolves. A preference must never turn a correct input into a miss |
| A country typed into Location - `75001, FR` | The typed qualifier wins. What you said this time beats what you set once |

The same preference breaks city ties, so bare `Boston` stays Massachusetts for everyone who has
not said otherwise and becomes Lincolnshire for someone who declared `GB`.

**A declared country accepts its own postal territories.** GeoNames files Puerto Rico under `PR`,
not `US`, so declaring the United States originally matched no row for `00784` at all, fell through
to population, and answered **Warsaw** - which also has a 00784. Declaring your country correctly
and getting another continent is worse than not declaring it. `PostalJurisdictions` maps a
sovereign onto the territories whose codes its postal system issues: `US` also accepts
`PR`/`VI`/`GU`/`AS`/`MP`, `FR` also accepts the `97xxx` and `98xxx` territories, and so on. The
relationship is **one-directional** - declaring `PR` keeps only Puerto Rico, because that choice is
more precise, not less. Membership is about which postal system issues the code, not about
sovereignty in any wider sense.

Two limits worth stating. The preference reaches the **local geocoder only**: on a fall-through the
vendor still ranks by prominence, so a declared country cannot fix a code the bundled data misses.
And the dropdown carries `US - United States of America`, split back to the code by Liquid in
`polling_url`; the API ignores anything that is not two ASCII letters rather than erroring, so a
mis-edited URL degrades to `Auto` instead of costing someone their forecast.

### Quota and abuse

`/api/v1/forecast` is anonymous and unthrottled, and v2 will be too. Free-text input changes the
threat from a bounded numeric grid to unbounded cardinality.

On a paid Open-Meteo plan this is a **cost** problem rather than an availability one: random input
burns quota rather than getting the service throttled and taking real users down with it. It still
needs bounding:

- Cap the accepted input length and reject obviously junk input before it reaches the client.
- Memoize resolutions in a **bounded** cache, in its own `MemoryCache` instance, never sharing
  `WeatherCacheOptions.SizeLimit`. A place lookup must not be able to evict forecasts. This is the
  same rule the reverse-geocoding memo follows, for the same reason: see
  [geographic-telemetry.md](geographic-telemetry.md#the-memo-is-a-dos-vector).
- **Negative-cache misses.** Misses are the cheap case for an attacker to generate; without this,
  every repeat of the same garbage string does full work.

## How the two features layer

Open-Meteo's geocoding response carries `country_code` (ISO alpha-2), `country`, and `name`, but
`admin1` is a **display name** ("Massachusetts") carrying only a GeoNames `admin1_id`, never an
ISO-3166-2 code. Confirmed against the live API. So forward geocoding alone cannot populate
`weather.subdivision` as [geographic-telemetry.md](geographic-telemetry.md) specifies it, and the
polygon lookup stays the only source of that field.

The two features layer rather than compete:

| Field | Source |
|---|---|
| Coordinates | Given directly, or from forward geocoding |
| `country_code`, `subdivision` | **Always** the Natural Earth polygon lookup, run on the final coordinates |
| `city` / `name` | The matched city when the input named one, else the GeoNames nearest-place fallback |

Running the polygon lookup for both input paths keeps the ISO codes consistent and gives one code
path instead of two sources of truth. Using the geocoded name for `city` is strictly better than
nearest-place when it is available, because it is the place the user actually named.

The same rule now governs what reaches the **screen**, not only what reaches a span: the bundled
data populates the `place` block on coordinate input too, and supplies a short subdivision label in
place of Open-Meteo's display name. That makes this document the source of `place` only for the
matched `name`; everything else in the block comes from
[geographic-telemetry.md](geographic-telemetry.md#the-two-use-cases).

Forward geocoding has since moved in-house as well, with Open-Meteo kept as the fallback for a local
miss. Everything about *parsing* below is unchanged - the same free-form `place`, the same
coordinate probe - but the search that follows hits the bundled data first, which is what makes
`00784` and `Munich, DE` resolve at all. See [Forward geocoding moved in-house
too](geographic-telemetry.md#forward-geocoding-moved-in-house-too).

### This was built first

v2 emits the `weather.input_kind` tag from [Telemetry](#telemetry), which makes the split between
coordinate and place input measurable. That measurement was once expected to decide whether the
polygon work happened at all. It no longer does: the bundled data became user-facing as well as
internal, and both input paths need it, so the split now sizes the benefit rather than gating it.
See [geographic-telemetry.md](geographic-telemetry.md#what-the-input_kind-reading-is-still-for).

## Response shape (v2)

Two additions over v1, and one invariant that reaches further than either of them: **every failure
the device can see is returned as HTTP 200 with a populated `error` object.**

### The `place` block

The resolved location, echoed back beside the forecast:

```json
{
  "place": {
    "name": "Portland",
    "admin1": "Maine",
    "country": "United States",
    "country_code": "US",
    "latitude": 43.66,
    "longitude": -70.26
  },
  "current": { },
  "hourly": { },
  "daily": { },
  "meta": { }
}
```

`current`, `hourly`, `daily`, and `meta` keep their v1 shapes.

The plugin renders this block, which makes it required rather than decorative: it is how a user finds
out they got Paris instead of Addison. See [Ambiguity](#ambiguity-first-result-wins).

The block is **omitted when the request carries `show_place=no`**, which is the plugin's Show
Location setting. Custom field values are unreadable from Liquid, so a setting that only affects
rendering still has to make the round trip; suppressing the block at the source is what a template
that cannot read the setting can act on. Showing it is the default, and an unrecognized value shows
it, so nothing that omits the parameter changes behavior. A user who turns it off gives up the
ambiguity mitigation above, which is the point of it being a choice rather than a default.

`admin1` is a short display label, not a raw ISO code: the alphabetic ISO subdivision part where
there is one (`US-MA` gives `MA`), and the subdivision name where the code is numeric (`FR-59` gives
`Nord`). The full ISO code goes to telemetry instead. See [The display
rule](geographic-telemetry.md#the-display-rule).

**The block is now populated for coordinate input too.** Everything but `name` comes from the
bundled reverse lookup on every path, so a coordinate caller sees a location where before they saw
none - which is how a transposed pair becomes visible. The block is omitted only when the lookup
finds no name at all: mid-ocean, or a coordinate pair with no settlement in range. Nothing is
invented to fill it.

### The error object

```json
{
  "error": {
    "code": "place_not_found",
    "message": "No place matches zzzzqqqq.",
    "hint": "Try adding a state or country, as in Portland, ME."
  }
}
```

`code` is stable and is what a template branches on; `message` and `hint` are wording and may change.
That split matters because a forked plugin's conditionals outlive any phrasing decision made here.

| `code` | Raised when | Typical `hint` |
|---|---|---|
| `place_missing` | Neither `place` nor a coordinate pair was supplied | Point at the plugin's own settings field |
| `place_invalid` | Two numeric tokens that fall outside latitude or longitude range, or saved `latitude`/`longitude` parameters that are unusable on their own | Name the likely swapped-order mistake |
| `place_not_found` | The geocoder matched nothing, or matched nothing that is a populated place | Suggest a qualifier |
| `request_invalid` | A parameter the **plugin** supplies, not the user, was rejected: `units`, `hours`, `days`, `provider` | Say it is a plugin problem |
| `weather_unavailable` | Every provider failed and no cached entry, fresh or stale, was usable, **or** the geocoder itself was unreachable | Say it is temporary |

A geocoder **outage** is deliberately not `place_not_found`. The two are indistinguishable in the
response body if they share a code, but they are opposites to the person reading the screen: a miss
means the input needs changing, an outage means the input was fine and the service was not.
`weather_unavailable` already carries the right hint, and reusing it costs no extra template arm.

`request_invalid` covers what v1 answers with a 400. None of those parameters comes from anything
a user types, so reaching one means a fork's polling URL is malformed, but the device cannot tell
the difference: a permanent 400 walks it into the degraded state exactly as a permanent 404 would.
Returning it as a rendered message is also the only way its owner ever finds out.

The single exception to the whole rule is a **cancelled request**, which keeps its status code. The
client is already gone, so there is no screen to render an error object onto and no poll left to
count against the install.

`message` should quote back what the user actually typed. Custom field values are not readable from
templates, so the response body is the only place the typed input can come from, and on an error
there is no `place` block to carry it.

### What returning 200 costs

**The span must still be tagged as an error.** A 200 that represents a failure will otherwise make the
Datadog error rate blind to exactly the failures worth seeing. Set the error tags on the request's
own span independently of the status code, which is also where error rate reads them natively.

**A non-2xx now means something narrower.** After this change a 5xx from v2 indicates the API itself
broke, not that the weather did. That is a more useful signal than today's mixed one, but any alerting
written against v1 status codes has to be revisited.

**TRMNL sees every response as successful**, so a failure now replaces the last good screen instead of
leaving it in place. For provider outages the existing cache absorbs this already: `weather_unavailable`
fires only after both the fresh and the stale windows are exhausted, so a brief blip still serves the
cached forecast and never reaches the error path. A geocoding failure has no such cushion, but it is
not transient either: the same bad input fails identically on every poll, which is exactly why it must
not be signalled with a status code. See below.

### Why not a status code plus a payload

The obvious refinement is to return a non-2xx **and** a renderable body, so Datadog sees a failure
without the plugin losing its message. Rejected, for one specific reason.

TRMNL counts polling failures and eventually stops refreshing the plugin altogether, putting it in a
**degraded state** that the user has to clear by hand from the plugin settings page. The help centre
says only that "if this happens too frequently, we will stop trying"; the threshold is not published.

That makes the split persistent-versus-transient rather than whose-fault-it-is:

- `place_missing`, `place_invalid`, and `place_not_found` are persistent by construction. Bad input
  fails on every poll, forever, so a status code here would not merely show an error once, it would
  walk the plugin into degraded state and demand a manual reset. Strictly worse than a blank screen.
- `weather_unavailable` is genuinely transient and rare, so a status code would be safe there, and
  arguably better: TRMNL would keep the last good forecast rather than replace it with a message.

Only the last of the four is a candidate, so the hybrid buys one code and costs a second render path.
Left as a possible later carve-out rather than part of this design.

The observability argument for it does not hold up either. Error rate and error tracking read span
tags, not the HTTP status, so a 200 tagged as an error is counted correctly. The status code is not
the lever that feature needs.

Whether TRMNL parses the body of a non-2xx response at all is **undocumented and unverified**. If the
carve-out is ever revisited, test it on a scratch plugin rather than this one, and on a real device:
local `trmnlp serve` has already proven to differ from hardware on custom field resolution.

## What v1 keeps

Unchanged for as long as it exists: `latitude` and `longitude` as separate required parameters, the
current JSON schema, and plain-text error responses with their existing status codes. No new fields,
because a fork's template may iterate structures it does not expect to grow.

v1 does gain the place **telemetry** tags from
[geographic-telemetry.md](geographic-telemetry.md#what-to-emit) internally, since those are emitted
server-side and are invisible to the caller.

## Migrating the plugin

Forked installs carry their own copy of `settings.yml` and never receive updates, so they stay on v1
indefinitely. Every non-forked install upgrades automatically, and that is where the care is needed:
those users get the new `settings.yml` with an empty `place` field, and their saved coordinates have
to keep working until they choose to type a place.

Keep `latitude` and `longitude` **declared** in `custom_fields` through the transition, and have
`polling_url` send all three parameters. Removing the fields only hides them from the UI, and whether
a removed field's stored value still interpolates into `polling_url` is unverified - not something to
bet every upgraded screen on. Declared fields interpolate for certain.

All three fields are `optional: true`, which is what makes either form workable: an upgraded user can
save without typing a place, and a user who has typed one can clear the coordinates. Without it every
field is required, and the migration is blocked from both directions. The key is undocumented in the
skill reference but is used by TRMNL's own plugins, and the server round-trips it back on push.

`place`'s description says that saved coordinates still work if it is left blank, or upgraded users
would see an empty field and assume the plugin is broken. `latitude` and `longitude` are labelled
deprecated and point at `place`, noting that a pasted `latitude, longitude` pair works there, so an
exact position does not have to be given up to migrate.

Drop the coordinate fields, and the v2 fallback that reads them, once telemetry shows no requests
arriving with coordinates alone.

Note that auto-upgrade means every non-forked install moves the moment v2 is pushed. Nothing in v1's
history has had that exposure, so a v2 defect is a fleet-wide outage rather than a slow rollout.

### Where the error renders

The template already has the branch. All four layouts guard on `{% if current and current.temperature %}`
and fall through to a `wi-na` icon over fixed text: `full.liquid:16`, `half_horizontal.liquid:19`,
`half_vertical.liquid:15`, and `quadrant.liquid:12`, which shortens the wording for the smaller slot.
`title_bar` renders outside the branch and degrades on its own, since `shared.liquid` guards the whole
timestamp span behind `{% if updated_at %}`, leaving just the instance name.

So this is not new markup, it is a reason attached to markup that exists. Today that branch fires
whenever `current.temperature` is nil, which makes a mistyped place, an expired key, a provider
outage, and a malformed `polling_url` produce one identical screen. v2 changes the trigger from
absent data to a reported error: each layout now has an `{% elsif error %}` arm rendering
`error.message` and, where the slot has room, `error.hint`, keeping the fixed text as the fallback
for a response that carries neither forecast nor error. `title_bar` also renders the resolved place
name (`shared.liquid:448`), which is the on-screen half of the ambiguity mitigation.

That mitigation is narrower in practice than it looks. `place` is only populated when the input was
geocoded, so a user who pastes a coordinate pair sees no place name and gets no signal that the pair
was swapped. Closing that needs the reverse lookup, which is item 13's decision.

## Sharing internals across versions

Keep the divergence at the edge. Both versions should share the resolver, the orchestrator, the
forecast cache, the trimmer, and the provider chain, and differ only in parameter parsing and
response serialization. Because the forecast cache keys on coordinates, v1 and v2 traffic for the
same location shares entries automatically.

Concretely, v2 adds a place resolver and a v2 endpoint plus response models. It should not need to
touch `WeatherForecastOrchestrator`, `WeatherCache`, or the providers.

## Telemetry

The tags go on the automatically instrumented `aspnet_core.request` span. This service starts no
spans of its own: a wrapping span was measured covering 892ms of a 1004ms request, so it timed the
entry span over again, while the calls worth timing separately - the geocoding and forecast requests
- already get their own client spans. The entry span is also the only one guaranteed to exist for
every failure, since an unset place and a place that resolves to nothing never reach the
orchestrator at all.

| Tag | Values | Purpose |
|---|---|---|
| `weather.input_kind` | `coordinates`, `place`, `missing`, `invalid` | Whether the reverse-geocoding work is worth building |
| `weather.error_code` | the `code` values above, plus `client_cancelled` | Which failure, without parsing a message |

Both are low cardinality and safe in a metric. The first two values answer the sequencing question;
the other two come free from the same parse and say how often input arrives unusable.

**Every error response also sets the span's error flag**, with the `code` as the error type. This is
what pays for returning 200: error rate and error tracking read span tags rather than the status
code, so without it every v2 failure would read as a clean success. A cancelled request is
deliberately excluded - the client left, which is not the service failing.

No `api.version` tag is needed - the versions are separate paths, so the route already carries it.
Because these tags share a span with `http.route`, filtering them to v2 is an ordinary facet rather
than a trace-level query. That filter matters, because v1 is coordinates-only by definition and the
interesting read is what **v2** users choose.

## Test scenarios

Most of the failures above were only reachable by causing them. Two could not be produced on
purpose at all: `weather_unavailable` needs every provider down at once, and a stale serve needs a
cache entry to have aged out of its fresh window. That left the templates' error branch shipping
unseen, since nothing could put one on a screen deliberately.

v2 therefore reads a sentinel in `place`: **`place=test:<name>`** returns a canned result.

| `place` | Result |
|---|---|
| `test:place_missing` | 200 + `error.code=place_missing` |
| `test:place_invalid` | 200 + `error.code=place_invalid` |
| `test:place_not_found` | 200 + `error.code=place_not_found` |
| `test:request_invalid` | 200 + `error.code=request_invalid` |
| `test:weather_unavailable` | 200 + `error.code=weather_unavailable` |
| `test:stale` | A real forecast reported as `meta.cache=stale_served`, six hours old |
| `test:precipitation` | A real forecast filled with random precipitation - v1's `fake` parameter |
| `test:499` | 499, empty. What a device that hung up mid-request produces |
| `test:500` | 500, `text/plain`, from the real unhandled-exception handler |
| `test:502` | 502, `text/plain`. v1's body when every provider fails |
| any other name | `request_invalid` listing the names that exist |

Matched case-insensitively, since these are typed into a web form. A colon cannot appear in a place
name, so nothing a real user enters collides with the prefix; `Testerton` is geocoded normally.

The sentinel rides in `place` rather than a query parameter of its own because `place` is a custom
field the plugin already forwards verbatim. Selecting a scenario is therefore typing into the
plugin's settings - no edit to `polling_url`, no push, no revert - which is the point, since these
exist to be stepped through one at a time while watching a screen. That is what closed out item 11:
every scenario was checked on a real device this way, rather than by reading API responses.

Not gated by environment. The service has no environment switch anywhere, and these are read-only
canned responses; the two that fetch a forecast stand in a fixed location so the scenario itself
cannot be the thing that fails. Each sets a `weather.test_scenario` span tag, so test polls stay
filterable and are never mistaken for real traffic. `test:500` does log through the real unhandled
-exception path and will appear in error tracking, which is the point of it.

The five errors are built by `WeatherErrors`, the same factory the real failures use, so a preview
cannot show a message that differs from the one a user would get. `test:precipitation` and v1's
`fake` call one shared implementation for the same reason.

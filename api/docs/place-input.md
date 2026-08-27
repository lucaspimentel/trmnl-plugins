# Place input and API v2 (design, not yet implemented)

**Status: proposed.** Nothing in this document is in the code yet. Today `GET /api/v1/forecast`
requires `latitude` and `longitude` as separate numeric query parameters
(`Endpoints/RequestValidator.cs`) and returns plain text on every error path
(`Endpoints/WeatherEndpoint.cs`).

The Open-Meteo geocoding behaviour described below was checked against the live API rather than
assumed, with the one exception noted under [Client](#client).

The goal is to let a user identify their location however they naturally would - a city name, a
postal code, or a pasted coordinate pair - instead of looking up two decimal numbers before the
plugin will work.

Related: [geographic-telemetry.md](geographic-telemetry.md), which covers the inverse problem
(coordinates to place names for telemetry). The two features meet in the middle; see
[How the two features layer](#how-the-two-features-layer).

## Decisions

| Question | Decision |
|---|---|
| Input shape | A **single** free-form `place` parameter |
| Coordinates | Detected by parsing, not by a separate parameter |
| Geocoder | Open-Meteo forward geocoding, on the paid customer endpoint |
| Ambiguity | **Take the first result.** Qualifiers already work via full-text search |
| Errors | HTTP **200** with a renderable error in the body, not a status code |
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
| `02180` | not coordinates | Open-Meteo search |
| `Boston, MA` | not coordinates | Open-Meteo search |
| `SW1A 1AA` | not coordinates | Open-Meteo search, which finds nothing |

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
3. Otherwise call Open-Meteo forward geocoding and take the first result.
4. Snap the resulting coordinates to F2, exactly as `WeatherForecastOrchestrator` already does.
5. Enter the existing cache and provider path unchanged.

Step 4 must happen **before** the forecast cache lookup. Resolve afterwards and the cache fragments by
input form; resolve before and every user who typed `Boston` converges on one cache entry.

### Client

Mirror `OpenMeteoClient`: it already switches between a free base URL and a `customer-` prefixed one
based on whether `OPEN_METEO_API_KEY` is set, and appends `apikey` to the query
(`Services/OpenMeteoClient.cs`). The geocoding equivalents are `geocoding-api.open-meteo.com/v1/search`
and `customer-geocoding-api.open-meteo.com/v1/search`. The free host is confirmed working; the
`customer-` one is **not** verified, because checking it needs the API key.

Postal codes resolve through the same `search` call, confirmed against the live API. No separate
postal dataset is needed.

Two response details that will bite an implementation:

- **On no match the `results` key is absent entirely**, not an empty array. A query for `zzzzqqqq`
  returns `{"generationtime_ms": ...}` and nothing else, so deserialization has to treat the property
  as missing rather than as empty.
- **Not every result is a populated place.** `Portland, ME` returns the city first and `Portland
  Point`, a `CAPE`, second. Taking the first result blindly can hand back a headland or a mountain
  for some inputs, so filter to `PPL*` feature codes before picking.

### Ambiguity: first result wins

Open-Meteo returns a ranked list, so `count=1` is effectively "most prominent match". Verified
against the live API: `Portland` resolves to Oregon (population 652,503) ahead of Maine, and
`Springfield` resolves to Missouri ahead of Illinois.

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
| `city` | The geocoding result when there was one, else the GeoNames nearest-place fallback |

Running the polygon lookup for both input paths keeps the ISO codes consistent and gives one code
path instead of two sources of truth. Using the geocoded name for `city` is strictly better than
nearest-place when it is available, because it is the place the user actually named.

### This should be built first

v2 has to emit the `weather.input_kind` tag from [Telemetry](#telemetry), so that the split between
coordinate and place input becomes measurable. That measurement is what decides whether the polygon
work is worth building at all; the argument lives in
[geographic-telemetry.md](geographic-telemetry.md#sequencing-build-v2-first).

## Response shape (v2)

Two changes over v1.

**A `place` block.** The resolved location, echoed back: name, country, country code, subdivision,
and the coordinates actually used. The template today cannot show the user where the forecast is
*for*. The plugin will render this, which makes it a required field rather than a nice-to-have: it is
how a user finds out they got Paris instead of Addison. See
[Ambiguity](#ambiguity-first-result-wins).

**An error field, returned with HTTP 200.** A non-200 gives the plugin nothing renderable, so a user
who mistypes a place name sees a stale or blank screen with no explanation. v2 returns 200 with a
populated error object (a stable machine-readable code plus a short human message sized for the
screen) so the template can say what went wrong.

### Open decision: how far the error shape extends

This is not specific to geocoding. `WeatherEndpoint.cs` returns `Results.Text` for every validation
failure and for the 502 when all providers fail, so the plugin currently cannot render **any** error.
If the screen should show "couldn't find that place", it should probably also show "weather providers
are unavailable" rather than silently going stale.

Deciding that is broader than this feature and is left open here. Whatever the answer, two
consequences of returning 200 have to be handled:

- **The span must still be tagged as an error.** A 200 that represents a failure will otherwise make
  the Datadog error rate blind to exactly the failures worth seeing.
- **TRMNL's retry and staleness behaviour changes**, because every response now looks successful to
  it. A transient upstream failure that currently leaves the last good screen in place would instead
  replace it with an error message. That may not be the better outcome for a brief blip, which argues
  for keeping genuinely transient failures on the existing non-200 path.

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

The `place` field's description should say that saved coordinates still work if it is left blank, or
upgraded users will see an empty field and assume the plugin is broken.

Drop the coordinate fields, and the v2 fallback that reads them, once telemetry shows no requests
arriving with coordinates alone.

Note that auto-upgrade means every non-forked install moves the moment v2 is pushed. Nothing in v1's
history has had that exposure, so a v2 defect is a fleet-wide outage rather than a slow rollout.

## Sharing internals across versions

Keep the divergence at the edge. Both versions should share the resolver, the orchestrator, the
forecast cache, the trimmer, and the provider chain, and differ only in parameter parsing and
response serialization. Because the forecast cache keys on coordinates, v1 and v2 traffic for the
same location shares entries automatically.

Concretely, v2 adds a place resolver and a v2 endpoint plus response models. It should not need to
touch `WeatherForecastOrchestrator`, `WeatherCache`, or the providers.

## Telemetry

One new span tag, beyond the place tags in the other document:

| Tag | Values | Purpose |
|---|---|---|
| `weather.input_kind` | `coordinates`, `place` | Whether the reverse-geocoding work is worth building |

Low cardinality and safe in a metric.

No `api.version` tag is needed - the versions are separate paths, so the route already carries it.
One wrinkle when querying: the route sits on the ASP.NET Core request span while `weather.input_kind`
belongs on the custom `weather.forecast` span, so cross-tabulating them is a trace-level query rather
than a single-span facet. That filter matters, because v1 is coordinates-only by definition and the
interesting read is what **v2** users choose.

## Open questions

- **Whether the error shape extends to upstream failures**, per the open decision above.
- **Whether `provider` and `fake` carry over to v2.** Both are debugging affordances rather than user
  features, and a new version is the cheap moment to drop them.

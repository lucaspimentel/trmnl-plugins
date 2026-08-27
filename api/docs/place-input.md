# Place input and API v2 (design, not yet implemented)

**Status: proposed.** Nothing in this document is in the code yet. Today `GET /api/v1/forecast`
requires `latitude` and `longitude` as separate numeric query parameters
(`Endpoints/RequestValidator.cs`) and returns plain text on every error path
(`Endpoints/WeatherEndpoint.cs`).

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
| Ambiguity | **Take the first result.** No qualifier syntax, no candidate list |
| Errors | HTTP **200** with a renderable error in the body, not a status code |
| Versioning | New **`/api/v2/`** endpoint. `/api/v1/` keeps its current contract permanently |

## Why a new version rather than extending v1

The plugin is public and has been forked. A fork carries its own `polling_url` with
`latitude={{ latitude }}&longitude={{ longitude }}` baked into `settings.yml`, pointing at this API,
and every device running that fork will keep polling that URL for as long as it stays installed.
There is no mechanism to update a fork's settings, and no way to know who forked it.

So v1 is not deprecable in any meaningful sense. Treat it as permanent. That is affordable only if v1
stays a thin edge over shared internals: see [Sharing internals](#sharing-internals-across-versions).

Versioning also buys the freedom to change the response schema, which the error-shape decision below
requires and which v1 cannot absorb without breaking the forks' Liquid templates.

## Input: one field, sniffed

v2 takes a single `place` parameter and decides what it is by parsing it. `latitude` and `longitude`
are **not** v2 parameters at all; a coordinate pair is just one of the things `place` accepts.

| Input | Detected as | Path |
|---|---|---|
| `42.35843	-71.05977` | coordinates | parsed directly, no geocoding call |
| `42.35,71.05` | coordinates | parsed directly, no geocoding call |
| `42.35, -71.05` | coordinates | parsed directly, no geocoding call |
| `02180` | not coordinates | Open-Meteo search |
| `Boston, MA` | not coordinates | Open-Meteo search |
| `SW1A 1AA` | not coordinates | Open-Meteo search |

The rule: normalize whitespace, split on `[,\s]+`, and treat the value as coordinates only when there
are **exactly two tokens, both parse as invariant-culture doubles, and both fall in range** (latitude
-90 to 90, longitude -180 to 180). Everything else goes to the geocoder.

That is safe against all of the above. A bare postal code is one token. A UK postcode is two
non-numeric tokens. A name with a comma splits into non-numeric tokens. Parse invariant-culture only:
accepting a comma as a decimal separator would make `42,35` ambiguous with a coordinate pair.

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
and `customer-geocoding-api.open-meteo.com/v1/search`; confirm both against Open-Meteo's current docs
before wiring them.

Postal codes resolve through the same `search` call. No separate postal dataset is needed.

### Ambiguity: first result wins

Open-Meteo returns a ranked list, so `count=1` is effectively "most prominent match". `Springfield`,
`Portland`, and `Paris` will each silently resolve to whichever the ranking favours.

This is deliberate: it never errors, needs no qualifier syntax, and keeps the screen populated. The
cost is that a user in Portland, Maine gets Portland, Oregon weather until they notice, and nothing
in the system will tell them. Rendering the resolved place name on screen (see
[Response shape](#response-shape-v2)) is the mitigation - the user sees what the API decided.

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
`admin1` is a **display name** ("Massachusetts") with a GeoNames id attached, not an ISO-3166-2 code.
So forward geocoding alone cannot populate `weather.subdivision` as
[geographic-telemetry.md](geographic-telemetry.md) specifies it.

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
*for*; this is a user-visible feature, not just plumbing, and it is what makes the first-result
ambiguity decision tolerable.

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

Unchanged, permanently: `latitude` and `longitude` as separate required parameters, the current JSON
schema, and plain-text error responses with their existing status codes. No new fields, because a
fork's template may iterate structures it does not expect to grow.

v1 does gain the place **telemetry** tags from
[geographic-telemetry.md](geographic-telemetry.md#what-to-emit) internally, since those are emitted
server-side and are invisible to the caller.

## Sharing internals across versions

Keep the divergence at the edge. Both versions should share the resolver, the orchestrator, the
forecast cache, the trimmer, and the provider chain, and differ only in parameter parsing and
response serialization. Because the forecast cache keys on coordinates, v1 and v2 traffic for the
same location shares entries automatically.

Concretely, v2 adds a place resolver and a v2 endpoint plus response models. It should not need to
touch `WeatherForecastOrchestrator`, `WeatherCache`, or the providers.

## Telemetry

Two new span tags, beyond the place tags in the other document:

| Tag | Values | Purpose |
|---|---|---|
| `api.version` | `v1`, `v2` | Adoption, and whether v1 traffic is forks or just un-migrated users |
| `weather.input_kind` | `coordinates`, `place` | Whether the reverse-geocoding work is worth building |

Both are low cardinality and safe in a metric.

## Open questions

- **Migrating this plugin's existing users.** Removing the `latitude` and `longitude` custom fields in
  favour of a single `place` field leaves existing installs with an empty `place` and a broken screen
  until each user reconfigures. Keeping all three fields during a transition and choosing between them
  in `polling_url` may work, but `polling_url` interpolation is plain Liquid and whether it supports a
  conditional there needs verifying before the plan depends on it.
- **Whether the error shape extends to upstream failures**, per the open decision above.
- **Whether `provider` and `fake` carry over to v2.** Both are debugging affordances rather than user
  features, and a new version is the cheap moment to drop them.

# TODO

- [x] Replace Open-Meteo, OR add support for multiple weather APIs with user-selectable provider via config
  - Current implementation: `api/src/TrmnlApi/Services/OpenMeteoClient.cs` calls Open-Meteo and `WeatherTransformer.cs` translates WMO codes to condition labels + icon classes
  - Plugin polls `https://trmnl-plugins-api.azurewebsites.net/api/v1/forecast` every 30min (see `plugins/weather/src/settings.yml`)
  - Researched alternatives (May 2026):
    - **Pirate Weather** (top pick): free 10K calls/mo, open-source, Dark Sky compatible, returns `icon` + `summary` fields directly (simpler than WMO mapping). Docs: https://pirateweather.net/en/latest/API/
    - **WeatherAPI.com**: 1M calls/mo free tier, single endpoint for current+hourly+daily
    - **OpenWeatherMap One Call 3.0**: 1K calls/day, requires credit card
    - **Tomorrow.io**: 500 calls/day, richest data (air quality, pollen)
    - **NWS api.weather.gov**: free + no key, but US-only and multi-call
    - **WeatherKit**: 500K/mo, but JWT auth + paid Apple Developer account
  - For multi-provider option: add a `provider` query param to `WeatherFunction`, introduce `IWeatherProvider` abstraction, and expose plugin setting (`field_type: select`) in `plugins/weather/src/settings.yml`
  - Touchpoints: `IOpenMeteoClient`, `OpenMeteoClient`, `IWeatherTransformer`, `WeatherTransformer`, `Models/OpenMeteo/*`, `Functions/WeatherFunction.cs`, `Program.cs` (DI), tests
  - **Design (multi-provider via `?provider=...` query param)**:
    - New `IWeatherProvider` interface: `string Name { get; }` + `Task<WeatherResponse> GetForecastAsync(lat, lon, metric, ct)`. Each provider owns its full pipeline (HTTP → JSON → icon map → canonical `WeatherResponse`); transformer is no longer a separate service.
    - DI via .NET keyed services: `AddKeyedSingleton<IWeatherProvider>("open-meteo", ...)` and `("pirate-weather", ...)`. `WeatherProviderResolver` validates the query param and resolves from `IServiceProvider.GetRequiredKeyedService`. Default = `open-meteo`. Unknown values → 400.
    - `WeatherCache` key must include provider name (else switching providers hits stale entries from the other).
    - `Meta` record gains `Provider` field so responses self-identify which upstream served them.
    - Plugin side: add `custom_fields` entry in `plugins/weather/src/settings.yml` with `field_type: select` (options: Open-Meteo, Pirate Weather) and append `&provider={{ weather_provider }}` to the polling URL.
    - Pirate Weather specifics: API key in URL path (env var `PIRATE_WEATHER_API_KEY`), unix-second timestamps (convert using `raw.Timezone` IANA tz to keep `"yyyy-MM-ddTHH:mm"` strings), humidity is 0..1 (multiply by 100), `isDay` derived from icon suffix `-day`/`-night`. URL: `https://api.pirateweather.net/forecast/{key}/{lat},{lon}?units=us|si&exclude=minutely,alerts,flags`. Icon strings: `clear-day`, `clear-night`, `rain`, `snow`, `sleet`, `wind`, `fog`, `cloudy`, `partly-cloudy-day`, `partly-cloudy-night` → `wi-*` classes in new `Mappings/PirateIconMap.cs`.
    - Decision needed before step 4: keep `WeatherCode: int` (synthesize stable int from Pirate icon) OR change to `string` (icon key). The latter is cleaner but breaks the documented field shape in `plugins/weather/CLAUDE.md`; check `plugins/weather/src/shared.liquid` for numeric uses.
  - **Migration order (each step independently shippable, prod endpoint stays stable):**
    1. ✅ **DONE** — Introduce `IWeatherProvider`. Wrap existing logic as `OpenMeteoProvider` (no behavior change, no new query param yet). Existing tests still pass.
    2. ✅ **DONE** — Add `WeatherProviderResolver` + keyed DI registration. `WeatherFunction` resolves via the resolver but only `open-meteo` is registered. Default behavior unchanged.
    3. ✅ **DONE** — Add `provider` query param parsing, propagate provider name to cache key, add `Meta.Provider` field. Still only one provider available.
    4. ✅ **DONE** — Add `PirateWeatherProvider` + `Models/PirateWeather/PirateWeatherResponse.cs` DTOs + `PirateIconMap.cs` + tests. Register as second keyed provider.
    5. ✅ **DONE** — Configure `PIRATE_WEATHER_API_KEY` app setting in Azure, update `local.settings.json` for dev, add `weather_provider` select field to `plugins/weather/src/settings.yml` and reference it in the polling URL. Deploy + smoke-test both providers.
  - Effort estimate: ~4-6 hours total for steps 1-5 (1h DTOs+client, 1.5h icon map verification against screenshots, 1h transformer wiring, 1h env var/Azure config, 1h end-to-end smoke test)
- [ ] Weather plugin: show the data source in the title bar
  - API already exposes `meta.provider` (`open-meteo` or `pirate-weather`) on every `/v1/forecast` response
  - Likely insertion point: `plugins/weather/src/shared.liquid` `title_bar` template (currently shows plugin name + "Updated {time}"), or as a small label/icon near the current conditions
  - Consider a short pretty label map: `open-meteo` → "Open-Meteo", `pirate-weather` → "Pirate Weather"
- [ ] Weather plugin: show a visual indicator when the displayed forecast is stale
  - API exposes `meta.cache` (`fresh_hit` / `fresh_fetch` / `stale_served`), `meta.age_seconds`, and `meta.upstream` (status + error message) when the most recent upstream call failed
  - "Stale" most naturally maps to `meta.cache == "stale_served"`, i.e. upstream is down and we're serving an older cached response — a badge or icon in the `title_bar` would surface this without disrupting the main view
  - May also want to surface the upstream error briefly (e.g. tooltip-style or icon) for at-a-glance debugging

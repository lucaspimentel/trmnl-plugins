# TODO

- [ ] Replace Open-Meteo, OR add support for multiple weather APIs with user-selectable provider via config
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

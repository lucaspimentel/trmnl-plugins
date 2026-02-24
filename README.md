# trmnl-plugins

Plugins for [TRMNL](https://usetrmnl.com/), an e-ink display device.

## Plugins

- **[mbta-alerts](./plugins/mbta-alerts)** - Displays service alerts from the Massachusetts Bay Transportation Authority (MBTA)
- **[weather](./plugins/weather)** - Displays current conditions, hourly chart, and 5-day forecast using Open-Meteo

## Plugin Structure

Each plugin directory uses the trmnlp `src/` layout:

```
plugins/<name>/
  .trmnlp.yml                 # local dev config
  fields.txt                  # API data field docs
  src/
    settings.yml              # API endpoint, refresh interval, metadata
    shared.liquid             # reusable Liquid templates
    full.liquid               # full screen layout
    half_horizontal.liquid
    half_vertical.liquid
    quadrant.liquid
```

## Local Development

Use [trmnlp](https://github.com/usetrmnl/trmnlp) to preview plugins locally:

```bash
gem install trmnl_preview   # or use Docker
cd plugins/mbta-alerts
trmnlp serve                # preview at http://localhost:4567
```

## Tools

Utilities for interacting with the TRMNL API. Fetch the current screen image and display it in Sixel format in the terminal.

- **[Get-Trmnl-Image.ps1](./tools/Get-Trmnl-Image.ps1)** - PowerShell script with manual Sixel encoding (black/white output)
- **[Trmnl.Cli](./tools/Trmnl.Cli/)** - .NET 9 console app using the [SixPix](https://www.nuget.org/packages/SixPix) library (full color)

Both require `TRMNL_DEVICE_ID` and `TRMNL_DEVICE_API_KEY` environment variables.

## Resources

- [TRMNL Website](https://usetrmnl.com/)
- [TRMNL Documentation](https://docs.usetrmnl.com/)
- [trmnlp - Local Preview Tool](https://github.com/usetrmnl/trmnlp)
- [TRMNL Design System](https://trmnl.com/framework/docs)

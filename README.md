# trmnl-plugins

Plugins for [TRMNL](https://usetrmnl.com/), an e-ink display device.

## Plugins

### [MBTA Alerts](./plugins/mbta-alerts)

Displays service alerts from the Massachusetts Bay Transportation Authority (MBTA), filtered to subway and light rail.

![MBTA Alerts](./plugins/mbta-alerts/screenshot.png)

### [Weather](./plugins/weather)

Displays current conditions, 24-hour temperature/precipitation chart, and 6-day forecast with weather icons.

![Weather](./plugins/weather/screenshot.png)

## Plugin Structure

Each plugin directory uses the trmnlp `src/` layout:

```
plugins/<name>/
  .trmnlp.yml                 # local dev config
  fields.txt                  # API data field docs
  src/
    settings.yml              # API endpoint, refresh interval, metadata (must be in src/)
    shared.liquid             # reusable Liquid templates
    full.liquid               # full screen layout
    half_horizontal.liquid
    half_vertical.liquid
    quadrant.liquid
```

## Local Development

### Static build preview

```bash
bash tools/build-preview.sh plugins/<name>                                        # build only
bash tools/build-preview.sh plugins/<name> --screenshot                           # build + screenshot → render.png
bash tools/build-preview.sh plugins/<name> --screenshot --1bit                    # build + screenshot + 1-bit conversion
bash tools/build-preview.sh plugins/<name> --screenshot --output my-shot.png      # custom output filename
```

`build-preview.sh` runs `trmnlp build` (fetches live data, renders all layouts) then wraps each HTML file with the TRMNL screen shell so it renders correctly in any browser.

Flags:
- `--screenshot`: captures `full.html` at 800×480 via playwright-cli (requires HTTP server on port 8765)
- `--1bit`: converts the screenshot to 1-bit black/white (no dithering) using ImageMagick (`magick`)
- `--output <filename>`: output filename (default: `render.png`); relative paths resolve under `<plugin-dir>`

To view the output, start a local HTTP server (required — `file://` URLs are blocked by browsers). Keep it running in the background between rebuilds:

```bash
cd plugins/<name>/_build && python -m http.server 8765
# open http://localhost:8765/full.html
```

### Live preview with trmnlp

```bash
cd plugins/<name>
trmnlp serve          # preview at http://localhost:4567
```

### Manual screenshots with playwright-cli

```bash
playwright-cli open --browser=msedge http://localhost:8765/full.html
playwright-cli resize 800 480
# wait ~3 seconds for Highcharts/fonts to render
playwright-cli screenshot --filename=plugins/<name>/render.png
playwright-cli close
```

## Tools

- **[build-preview.sh](./tools/build-preview.sh)** - Build static HTML previews for any plugin (wraps `trmnlp build` output with TRMNL screen shell); `--screenshot` flag also captures `render.png` via playwright-cli
- **[Get-Trmnl-Image.ps1](./tools/Get-Trmnl-Image.ps1)** - Fetch current TRMNL screen image and display in Sixel format (black/white); saves timestamped PNG files
- **[Trmnl.Cli](./tools/Trmnl.Cli/)** - .NET 9 app that fetches and displays the current screen image in Sixel (full color)

`Get-Trmnl-Image.ps1` and `Trmnl.Cli` require `TRMNL_DEVICE_ID` and `TRMNL_DEVICE_API_KEY` environment variables (stored in 1Password item "trmnl").

## Resources

- [TRMNL Website](https://usetrmnl.com/)
- [TRMNL Documentation](https://docs.trmnl.com/)
- [trmnlp - Local Preview Tool](https://github.com/usetrmnl/trmnlp)
- [TRMNL Design System](https://trmnl.com/framework/docs)

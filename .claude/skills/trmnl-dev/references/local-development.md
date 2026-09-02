# Local Development with trmnlp

[trmnlp](https://github.com/usetrmnl/trmnlp) is a local dev server for previewing plugins.

## Install

```bash
# Via RubyGems (gemspec requires Ruby >= 3.4)
gem install trmnl_preview

# Or via Docker
docker run --publish 4567:4567 --volume "$(pwd):/plugin" trmnl/trmnlp serve
```

## Workflow

```bash
trmnlp init my_plugin     # scaffold a new plugin
cd my_plugin
trmnlp serve              # start local preview at http://localhost:4567
# Edit templates — preview auto-reloads on save

trmnlp login              # authenticate with TRMNL API key (or set $TRMNL_API_KEY env var)
trmnlp push --force       # upload plugin to your TRMNL device (--force skips confirmation prompt)
```

For existing plugins on the TRMNL server:

```bash
trmnlp login
trmnlp clone my_plugin <id>   # download from server
cd my_plugin
trmnlp serve                  # develop locally
trmnlp push --force           # upload changes
```

## Static HTML / PNG Render (`trmnlp build`)

`trmnlp build` writes static HTML for every layout to `_build/`. Add `--png` (trmnl_preview ≥ 0.8.1) to also rasterize each layout to a PNG:

```bash
trmnlp build                                                  # _build/<layout>.html only
trmnlp build --png --width 800 --height 480 --color-depth 1   # OG 1-bit (2 colors)
trmnlp build --png --width 1040 --height 780 --color-depth 4  # 4-bit (16 grays)
```

- `--color-depth` is 1-8; it genuinely quantizes (1 → true 1-bit, 4 → 16 grays). JS runs, so Highcharts renders.
- **Limitation:** the wrapper is a bare `<div class="screen">` — no `screen--md/lg`, `screen--1bit/4bit`, or `screen--portrait`. So only the default (OG) layout renders; the TRMNL X responsive layout and portrait do not. `--width`/`--height` only resize the canvas (OG layout sits top-left with empty space), and sub-full layouts render on a full-size canvas rather than their mashup slot.
- Good for a fast OG sanity check. For a true X / portrait / in-slot preview, this repo wraps the build output in real device screen classes via `tools/build-preview.sh` (see root `CLAUDE.md`).

## .trmnlp.yml — Local Dev Config

This file configures the local preview server (not uploaded to TRMNL):

```yaml
---
watch:
  - src
  - .trmnlp.yml

custom_fields:
  api_key: "{{ env.MY_API_KEY }}"   # interpolate environment variables

time_zone: America/New_York

variables:
  trmnl:
    plugin_settings:
      instance_name: My Plugin Dev
```

- `watch`: directories to watch for auto-reload
- `custom_fields`: values for custom fields defined in settings.yml; supports `{{ env.VAR }}` interpolation
- `variables`: override template variables for local testing
- `time_zone`: IANA timezone injected into `trmnl.user`

## trmnlp Project Structure

trmnlp expects the following layout, with `.trmnlp.yml` at the project root and all plugin
files under `src/`:

```
my-plugin/
  .trmnlp.yml
  src/
    settings.yml        ← trmnlp reads this (not the root-level one)
    shared.liquid
    full.liquid
    half_horizontal.liquid
    half_vertical.liquid
    quadrant.liquid
```

**Critical**: trmnlp reads `settings.yml` from `src/settings.yml` (not the plugin root).
If `settings.yml` is in the wrong location, polling will not work — the Poll button will
appear to succeed but all data will be zero/empty.

Plugins in this repository use this layout: `.trmnlp.yml` at the plugin root,
all liquid files and `settings.yml` under `src/`.

## Static vs Live Data

`.trmnlp.yml` has **no `data:` key**. Its recognized keys are `watch`, `custom_fields`,
`variables`, `custom_filters`, `time_zone`, `framework_asset_host`, `transform_runtime`,
`serverless_daemon_url`, and `serverless_daemon_api_key`. Anything else is ignored silently.

There are three ways to render without hitting the live API:

1. **`variables:` in `.trmnlp.yml`** — deep-merged over the assembled data hash, so its keys land
   where the API response's keys would. Mirror the response's own root shape: `data:` for an
   array-rooted API, the response's top-level keys directly for an object-rooted one.

   ```yaml
   variables:
     trmnl:
       plugin_settings:
         instance_name: My Plugin
     data:              # array-rooted API
       - some_field: value
   ```

2. **`static_data` in `src/settings.yml`**, with `strategy: static`. A JSON string, parsed and
   merged in place of a poll.

3. **The polled-data cache.** trmnlp writes the last successful poll to `data.json` under the XDG
   cache directory (`$XDG_CACHE_HOME/trmnl/data.json`, else `~/.cache/trmnl/data.json`) and reads
   it back on the next render. Dropping a saved response there replays it.

To go back to live data, remove the `variables:` overrides (or the `static_data`) and restart the
server so it re-reads `.trmnlp.yml` and polls fresh.

## Killing and Restarting the Server (Windows)

Port 4567 must be free before starting. To kill a stuck server:

```powershell
$conns = Get-NetTCPConnection -LocalPort 4567 -ErrorAction SilentlyContinue
foreach ($c in $conns) { Stop-Process -Id $c.OwningProcess -Force -ErrorAction SilentlyContinue }
```

Then restart from the plugin directory:

```bash
cd my-plugin
trmnlp serve
```

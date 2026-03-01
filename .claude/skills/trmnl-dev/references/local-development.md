# Local Development with trmnlp

[trmnlp](https://github.com/usetrmnl/trmnlp) is a local dev server for previewing plugins.

## Install

```bash
# Via RubyGems (requires Ruby 3.x)
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

## Static vs Live Data in .trmnlp.yml

For development with static sample data, add a `data:` block under `variables:`:

```yaml
variables:
  trmnl:
    plugin_settings:
      instance_name: My Plugin
  data:
    some_field: value
    nested:
      field: value
```

To switch to live API data, **remove the `data:` block entirely**. trmnlp will
automatically poll the `polling_url` from `src/settings.yml` on startup and when
the Poll button is clicked.

**Note**: When switching from static to live data, restart the trmnlp server so it
picks up the updated `.trmnlp.yml` and re-polls the API fresh.

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

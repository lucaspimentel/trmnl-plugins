# CLAUDE.md

TRMNL e-ink display plugins. Plugins live under `plugins/`, shared API backend under `api/`.

For TRMNL docs: https://docs.trmnl.com/go/llms.txt (append `.md` to any `docs.trmnl.com/go/...` URL for leaner Markdown).

## Critical Gotchas

- `settings.yml` must be at `src/settings.yml` — trmnlp ignores one at the plugin root
- `polling_url` interpolation: use `{{ keyname }}` (plain Liquid), not `##{{ keyname }}`
- Flex children that should shrink need `min-width: 0` — `plugins.js` measures widths before layout, so without it they expand to full container width
- Recipe linter counts raw substrings of `font-size`, `padding`, `margin`, etc. across ALL markup (including JS, comments, variable names) — max 6 total. See `references/framework/updates.md` for workarounds.

## Deploy a Plugin

```bash
cd plugins/<name>
trmnlp push --force    # --force skips confirmation prompt
```

## Build Preview

```bash
bash tools/build-preview.sh plugins/<name>                                  # build all variants (og, x, x-portrait)
bash tools/build-preview.sh plugins/<name> --device x                       # TRMNL X only (landscape + portrait)
bash tools/build-preview.sh plugins/<name> --device x --orientation portrait # X portrait only
bash tools/build-preview.sh plugins/<name> --screenshot                     # + screenshot all variants × all layouts
bash tools/build-preview.sh plugins/<name> --screenshot --1bit              # + 1-bit B&W conversion
bash tools/build-preview.sh plugins/<name> --screenshot --device x --layout full  # screenshot X full only
```

Output goes to `_build/{og,x,x-portrait}/`. Each subdirectory gets the TRMNL wrapper:
- `https://trmnl.com/css/latest/plugins.css` + `https://trmnl.com/js/latest/plugins.js`
- Inter font (Google Fonts)
- OG: `<div class="screen screen--1bit screen--ogv2 screen--md screen--1x">`
- X: `<div class="screen screen--4bit screen--v2 screen--lg screen--1x">`
- X portrait: same + `screen--portrait`

## Docker Sandbox Template

Build and run as a Docker sandbox template with all tools pre-installed (trmnlp, playwright-cli, .NET 10, Azure Functions Core Tools, ImageMagick, Python 3, PowerShell, Ruby):

```bash
# Build the template
docker build -t trmnl-plugins:v1 .

# Run a sandbox with it
docker sandbox run -t trmnl-plugins:v1 claude .
```

## Credentials

`TRMNL_DEVICE_ID`, `TRMNL_DEVICE_API_KEY`, and `TRMNL_API_KEY` (for `trmnlp login`) are in **1Password item "trmnl"**:

```bash
op item get trmnl --fields label=TRMNL_DEVICE_ID,label=TRMNL_DEVICE_API_KEY --reveal
```

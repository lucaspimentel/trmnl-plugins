# CLAUDE.md

TRMNL e-ink display plugins. Plugins live under `plugins/`, shared API backend under `api/`.

For TRMNL docs: https://docs.trmnl.com/go/llms.txt (append `.md` to any `docs.trmnl.com/go/...` URL for leaner Markdown).

## Critical Gotchas

- `settings.yml` must be at `src/settings.yml` — trmnlp ignores one at the plugin root
- `polling_url` interpolation: use `{{ keyname }}` (plain Liquid), not `##{{ keyname }}`
- Flex children that should shrink need `min-width: 0` — `plugins.js` measures widths before layout, so without it they expand to full container width

## Deploy a Plugin

```bash
cd plugins/<name>
trmnlp push --force    # --force skips confirmation prompt
```

## Build Preview

```bash
bash tools/build-preview.sh plugins/<name>                        # build only
bash tools/build-preview.sh plugins/<name> --screenshot           # + screenshot full layout
bash tools/build-preview.sh plugins/<name> --screenshot --1bit    # + 1-bit B&W conversion
bash tools/build-preview.sh plugins/<name> --screenshot --layout all  # all layouts
```

The wrapper injected into each `_build/*.html` file:
- `https://trmnl.com/css/latest/plugins.css` + `https://trmnl.com/js/latest/plugins.js`
- Inter font (Google Fonts)
- `<div class="screen screen--1bit screen--ogv2 screen--md screen--1x">`

## Credentials

`TRMNL_DEVICE_ID`, `TRMNL_DEVICE_API_KEY`, and `TRMNL_API_KEY` (for `trmnlp login`) are in **1Password item "trmnl"**:

```bash
op item get trmnl --fields label=TRMNL_DEVICE_ID,label=TRMNL_DEVICE_API_KEY --reveal
```

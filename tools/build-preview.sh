#!/usr/bin/env bash
# build-preview.sh — run trmnlp build and inject screen classes into built HTML
# Usage: ./tools/build-preview.sh plugins/weather [--screenshot] [--1bit] [--layout <name>] [--output <dir>]
# Output: plugins/weather/_build/*.html (screen classes injected, ready to open in a browser)
#
# --screenshot:        take a screenshot of the specified layout (default: full).
#                      Requires playwright-cli and a running HTTP server on port 8765
#                      serving <plugin-dir>/_build/.
# --1bit:              convert screenshot to 1-bit black/white (no dithering) using ImageMagick.
#                      Only applies when --screenshot is also passed.
# --layout <name>:     layout to screenshot: full, half_horizontal, half_vertical, quadrant, or all.
#                      Default: full. Only applies when --screenshot is also passed.
# --output <dir>:      output directory for screenshots (default: <plugin-dir>).
#                      Directory is created if it doesn't exist.
#                      Screenshot filenames are auto-generated as render-<layout>.png.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

PLUGIN_DIR=""
SCREENSHOT=false
ONEBIT=false
LAYOUT="full"
OUTPUT_DIR=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --screenshot) SCREENSHOT=true ;;
    --1bit) ONEBIT=true ;;
    --layout) LAYOUT="$2"; shift ;;
    --output) OUTPUT_DIR="$2"; shift ;;
    *) PLUGIN_DIR="$1" ;;
  esac
  shift
done

if [[ -z "$PLUGIN_DIR" ]]; then
  echo "Usage: build-preview.sh <plugin-dir> [--screenshot] [--1bit] [--layout <name>] [--output <dir>]" >&2
  exit 1
fi

# Resolve plugin dir relative to repo root if not absolute
if [[ "$PLUGIN_DIR" != /* ]]; then
  PLUGIN_DIR="$REPO_ROOT/$PLUGIN_DIR"
fi

BUILD_DIR="$PLUGIN_DIR/_build"

cd "$PLUGIN_DIR"
trmnlp build
cd - > /dev/null

SCREEN_CLASSES="screen screen--1bit screen--ogv2 screen--md screen--1x"

built_files=()
for file in "$BUILD_DIR"/*.html; do
  # Inject screen classes into the outer empty <div class=""> that trmnlp generates
  sed -i "s|<div class=\"\">|<div class=\"${SCREEN_CLASSES}\">|" "$file"
  echo "Wrapped: $file"
  built_files+=("$(basename "$file")")
done

echo "Done. Built: ${built_files[*]}"

if $SCREENSHOT; then
  # Resolve output directory
  if [[ -z "$OUTPUT_DIR" ]]; then
    SCREENSHOT_DIR="$PLUGIN_DIR"
  elif [[ "$OUTPUT_DIR" == /* ]]; then
    SCREENSHOT_DIR="$OUTPUT_DIR"
  else
    SCREENSHOT_DIR="$PLUGIN_DIR/$OUTPUT_DIR"
  fi
  mkdir -p "$SCREENSHOT_DIR"

  # Viewport dimensions per layout
  viewport_for_layout() {
    case "$1" in
      full)             echo "800 480" ;;
      half_horizontal)  echo "800 240" ;;
      half_vertical)    echo "400 480" ;;
      quadrant)         echo "400 240" ;;
      *) echo "Unknown layout: $1" >&2; exit 1 ;;
    esac
  }

  screenshot_layout() {
    local layout="$1"
    local dims viewport_w viewport_h
    dims=$(viewport_for_layout "$layout")
    viewport_w="${dims%% *}"
    viewport_h="${dims##* }"
    local render_png="$SCREENSHOT_DIR/render-${layout}.png"

    echo "Taking screenshot of ${layout} (${viewport_w}x${viewport_h}) → $render_png"
    playwright-cli open --browser=msedge "http://localhost:8765/${layout}.html"
    playwright-cli resize "$viewport_w" "$viewport_h"
    sleep 3
    playwright-cli screenshot --filename="$render_png"
    playwright-cli close
    echo "Screenshot saved: $render_png"

    if $ONEBIT; then
      if command -v magick &>/dev/null; then
        magick "$render_png" -colorspace Gray -threshold 60% -type Bilevel "$render_png"
        echo "Converted to 1-bit (no dithering): $render_png"
      else
        echo "ImageMagick not found — skipping 1-bit conversion"
      fi
    fi
  }

  if [[ "$LAYOUT" == "all" ]]; then
    for l in full half_horizontal half_vertical quadrant; do
      if [[ -f "$BUILD_DIR/${l}.html" ]]; then
        screenshot_layout "$l"
      else
        echo "Skipping ${l} (no ${l}.html found)"
      fi
    done
  else
    screenshot_layout "$LAYOUT"
  fi
fi

#!/usr/bin/env bash
# build-preview.sh — run trmnlp build and generate preview HTML for all device variants
# Usage: ./tools/build-preview.sh plugins/weather [--device <name>] [--orientation <value>] [--screenshot] [--1bit] [--layout <name>] [--output <dir>]
# Output: plugins/weather/_build/{og,x,x-portrait}/*.html
#
# --device <name>:        og, x, or all (default: all).
# --orientation <value>:  landscape, portrait, or all (default: all).
#                          OG portrait is skipped (not a real device configuration).
# --layout <name>:        full, half_horizontal, half_vertical, quadrant, or all (default: all).
#                          Only affects --screenshot.
# --screenshot:           take screenshots of each variant × layout.
#                          Requires playwright-cli and a running HTTP server on port 8765
#                          serving <plugin-dir>/_build/.
# --1bit:                 convert screenshots to 1-bit black/white (no dithering) using ImageMagick.
#                          Only applies when --screenshot is also passed.
# --output <dir>:         output directory for screenshots (default: <plugin-dir>).
#                          Directory is created if it doesn't exist.
#                          Screenshot filenames: render-<variant>-<layout>.png.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

PLUGIN_DIR=""
DEVICE="all"
ORIENTATION="all"
SCREENSHOT=false
ONEBIT=false
LAYOUT="all"
OUTPUT_DIR=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --device) DEVICE="$2"; shift ;;
    --orientation) ORIENTATION="$2"; shift ;;
    --screenshot) SCREENSHOT=true ;;
    --1bit) ONEBIT=true ;;
    --layout) LAYOUT="$2"; shift ;;
    --output) OUTPUT_DIR="$2"; shift ;;
    *) PLUGIN_DIR="$1" ;;
  esac
  shift
done

if [[ -z "$PLUGIN_DIR" ]]; then
  echo "Usage: build-preview.sh <plugin-dir> [--device <name>] [--orientation <value>] [--screenshot] [--1bit] [--layout <name>] [--output <dir>]" >&2
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

# Expand --device into device list
DEVICES=()
case "$DEVICE" in
  og)  DEVICES=(og) ;;
  x)   DEVICES=(x) ;;
  all) DEVICES=(og x) ;;
  *)   echo "Unknown device: $DEVICE (expected: og, x, all)" >&2; exit 1 ;;
esac

# Expand --orientation into orientation list
ORIENTATIONS=()
case "$ORIENTATION" in
  landscape) ORIENTATIONS=(landscape) ;;
  portrait)  ORIENTATIONS=(portrait) ;;
  all)       ORIENTATIONS=(landscape portrait) ;;
  *)         echo "Unknown orientation: $ORIENTATION (expected: landscape, portrait, all)" >&2; exit 1 ;;
esac

# Build variant list from device × orientation
VARIANTS=()
for dev in "${DEVICES[@]}"; do
  for orient in "${ORIENTATIONS[@]}"; do
    # Skip OG portrait — not a real device configuration
    if [[ "$dev" == "og" && "$orient" == "portrait" ]]; then continue; fi

    if [[ "$dev" == "og" ]]; then
      VARIANTS+=("og|screen screen--1bit screen--ogv2 screen--md screen--1x")
    elif [[ "$orient" == "portrait" ]]; then
      VARIANTS+=("x-portrait|screen screen--4bit screen--v2 screen--lg screen--1x screen--portrait")
    else
      VARIANTS+=("x|screen screen--4bit screen--v2 screen--lg screen--1x")
    fi
  done
done

if [[ ${#VARIANTS[@]} -eq 0 ]]; then
  echo "No variants to build (OG portrait is not supported)." >&2
  exit 1
fi

# Generate variant subdirectories from base HTML
for variant in "${VARIANTS[@]}"; do
  name="${variant%%|*}"
  classes="${variant#*|}"
  variant_dir="$BUILD_DIR/$name"
  mkdir -p "$variant_dir"
  for file in "$BUILD_DIR"/*.html; do
    base="$(basename "$file")"
    cp "$file" "$variant_dir/$base"
    sed -i "s|<div class=\"\">|<div class=\"${classes}\">|" "$variant_dir/$base"
  done
  echo "Wrapped: $name/"
done

# Clean up base HTML files (they still have empty class="")
rm -f "$BUILD_DIR"/*.html

echo "Done. Variants: $(printf '%s ' "${VARIANTS[@]%%|*}")"

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

  # Expand --layout into layout list
  if [[ "$LAYOUT" == "all" ]]; then
    LAYOUTS=(full half_horizontal half_vertical quadrant)
  else
    LAYOUTS=("$LAYOUT")
  fi

  # Viewport dimensions for a variant + layout
  viewport_for_layout() {
    local variant="$1" layout="$2" w h
    case "$layout" in
      full)            w=800; h=480 ;;
      half_horizontal) w=800; h=240 ;;
      half_vertical)   w=400; h=480 ;;
      quadrant)        w=400; h=240 ;;
      *) echo "Unknown layout: $layout" >&2; exit 1 ;;
    esac
    if [[ "$variant" == x* ]]; then
      case "$layout" in
        full)            w=1040; h=780 ;;
        half_horizontal) w=1040; h=390 ;;
        half_vertical)   w=520;  h=780 ;;
        quadrant)        w=520;  h=390 ;;
      esac
    fi
    if [[ "$variant" == *-portrait ]]; then
      echo "$h $w"
    else
      echo "$w $h"
    fi
  }

  screenshot_layout() {
    local variant="$1" layout="$2"
    local dims viewport_w viewport_h
    dims=$(viewport_for_layout "$variant" "$layout")
    viewport_w="${dims%% *}"
    viewport_h="${dims##* }"
    local render_png="$SCREENSHOT_DIR/render-${variant}-${layout}.png"

    echo "Taking screenshot of ${variant}/${layout} (${viewport_w}x${viewport_h}) → $render_png"
    playwright-cli open --browser=msedge "http://localhost:8765/${variant}/${layout}.html"
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

  for variant in "${VARIANTS[@]}"; do
    name="${variant%%|*}"
    for layout in "${LAYOUTS[@]}"; do
      if [[ -f "$BUILD_DIR/$name/${layout}.html" ]]; then
        screenshot_layout "$name" "$layout"
      else
        echo "Skipping ${name}/${layout} (not found)"
      fi
    done
  done
fi

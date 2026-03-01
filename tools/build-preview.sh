#!/usr/bin/env bash
# build-preview.sh — run trmnlp build and wrap outputs with the TRMNL screen shell
# Usage: ./tools/build-preview.sh plugins/weather [--screenshot] [--1bit] [--output <filename>]
# Output: plugins/weather/_build/*.html (wrapped, ready to open in a browser)
#         plugins/weather/render.png (if --screenshot is passed)
#
# --screenshot:        open full.html in Edge at 800x480, wait 3s for Highcharts/fonts,
#                      save screenshot to <plugin-dir>/render.png, then close.
#                      Requires playwright-cli and a running HTTP server on port 8765
#                      serving <plugin-dir>/_build/.
# --1bit:              convert screenshot to 1-bit black/white (no dithering) using ImageMagick.
#                      Only applies when --screenshot is also passed.
# --output <filename>: output filename for the screenshot (default: render.png).
#                      Relative paths are resolved relative to <plugin-dir>.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

PLUGIN_DIR=""
SCREENSHOT=false
ONEBIT=false
OUTPUT_FILE="render.png"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --screenshot) SCREENSHOT=true ;;
    --1bit) ONEBIT=true ;;
    --output) OUTPUT_FILE="$2"; shift ;;
    *) PLUGIN_DIR="$1" ;;
  esac
  shift
done

if [[ -z "$PLUGIN_DIR" ]]; then
  echo "Usage: build-preview.sh <plugin-dir> [--screenshot]" >&2
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

for file in "$BUILD_DIR"/*.html; do
  layout_content=$(cat "$file")

  cat > "$file" <<EOF
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <link rel="stylesheet" href="https://trmnl.com/css/latest/plugins.css">
  <script src="https://trmnl.com/js/latest/plugins.js"></script>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:ital,opsz,wght@0,14..32,100..900;1,14..32,100..900&display=swap" rel="stylesheet">
  <style>
    body { margin: 0; background: white; }
    .screen { width: 800px; height: 480px; overflow: hidden; }
  </style>
</head>
<body class="environment trmnl">
  <div class="${SCREEN_CLASSES}">
    <div class="view view--$(basename "$file" .html)">
${layout_content}
    </div>
  </div>
</body>
</html>
EOF

  echo "Wrapped: $file"
done

echo "Done. Open _build/full.html in a browser."

if $SCREENSHOT; then
  # Resolve output path: absolute as-is, relative resolved under plugin dir
  if [[ "$OUTPUT_FILE" == /* ]]; then
    RENDER_PNG="$OUTPUT_FILE"
  else
    RENDER_PNG="$PLUGIN_DIR/$OUTPUT_FILE"
  fi
  echo "Taking screenshot → $RENDER_PNG"
  playwright-cli open --browser=msedge http://localhost:8765/full.html
  playwright-cli resize 800 480
  sleep 3
  playwright-cli screenshot --filename="$RENDER_PNG"
  playwright-cli close
  echo "Screenshot saved: $RENDER_PNG"
  if $ONEBIT; then
    if command -v magick &>/dev/null; then
      magick "$RENDER_PNG" -colorspace Gray -threshold 60% -type Bilevel "$RENDER_PNG"
      echo "Converted to 1-bit (no dithering): $RENDER_PNG"
    else
      echo "ImageMagick not found — skipping 1-bit conversion"
    fi
  fi
fi

#!/usr/bin/env bash
# build-preview.sh — run trmnlp build and wrap outputs with the TRMNL screen shell
# Usage: ./tools/build-preview.sh plugins/weather [--screenshot]
# Output: plugins/weather/_build/*.html (wrapped, ready to open in a browser)
#         plugins/weather/render.png (if --screenshot is passed)
#
# --screenshot: open full.html in Edge at 800x480, wait 3s for Highcharts/fonts,
#               save screenshot to <plugin-dir>/render.png, then close.
#               Requires playwright-cli and a running HTTP server on port 8765
#               serving <plugin-dir>/_build/.

set -e

PLUGIN_DIR=""
SCREENSHOT=false

for arg in "$@"; do
  case "$arg" in
    --screenshot) SCREENSHOT=true ;;
    *) PLUGIN_DIR="$arg" ;;
  esac
done

if [[ -z "$PLUGIN_DIR" ]]; then
  echo "Usage: build-preview.sh <plugin-dir> [--screenshot]" >&2
  exit 1
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
  RENDER_PNG="$PLUGIN_DIR/render.png"
  echo "Taking screenshot → $RENDER_PNG"
  playwright-cli open --browser=msedge http://localhost:8765/full.html
  playwright-cli resize 800 480
  sleep 3
  playwright-cli screenshot --filename="$RENDER_PNG"
  playwright-cli close
  echo "Screenshot saved: $RENDER_PNG"
fi

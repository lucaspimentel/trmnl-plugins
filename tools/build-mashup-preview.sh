#!/usr/bin/env bash
# build-mashup-preview.sh — render a plugin view inside a Fluid Mashup (mashup--3x3) cell
# Usage: ./tools/build-mashup-preview.sh plugins/weather [--device <name>] [--orientation <value>]
#                                        [--cell <CxR>[:layout]]... [--screenshot] [--1bit] [--output <dir>]
#
# Fluid Mashup puts the chosen view inside a .mashup-cell that carves up a 3x3 grid. The cell,
# not the view, owns the size, so a view can land in a slot no standalone layout ever sees
# (a 3x1 banner, a 1x3 column). This builds those slots so they can be looked at.
#
# --cell <CxR>[:layout]:  cell size in grid tracks, COLUMNS x ROWS (1..3 each). Repeatable.
#                          Note issue #7 labels the same shapes rows x columns, so its
#                          "1x3" (the wide banner) is 3x1 here and its "3x1" (the tall
#                          column) is 1x3. Check the shape, not the label.
#                          Default cells: 3x1, 1x1, 1x3 — every shape that issue reports.
#                          The layout suffix overrides which view is placed in the cell;
#                          the default per size is the one core would pick by shape.
# --device, --orientation, --screenshot, --1bit, --output: as in build-preview.sh.
#
# Output: <plugin-dir>/_build/{og,x,x-portrait}/mashup-<CxR>.html
# --screenshot starts its own HTTP server on port 8765 and stops it again, unless one is
#   already listening there, in which case it must be serving <plugin-dir>/_build/.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

PLUGIN_DIR=""
DEVICE="all"
ORIENTATION="all"
SCREENSHOT=false
ONEBIT=false
OUTPUT_DIR=""
CELLS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --device) DEVICE="$2"; shift ;;
    --orientation) ORIENTATION="$2"; shift ;;
    --cell) CELLS+=("$2"); shift ;;
    --screenshot) SCREENSHOT=true ;;
    --1bit) ONEBIT=true ;;
    --output) OUTPUT_DIR="$2"; shift ;;
    *) PLUGIN_DIR="$1" ;;
  esac
  shift
done

if [[ -z "$PLUGIN_DIR" ]]; then
  echo "Usage: build-mashup-preview.sh <plugin-dir> [--device <name>] [--cell <CxR>[:layout]] [--screenshot]" >&2
  exit 1
fi

if [[ ${#CELLS[@]} -eq 0 ]]; then
  CELLS=(3x1 1x1 1x3)
fi

if [[ "$PLUGIN_DIR" != /* ]]; then
  PLUGIN_DIR="$REPO_ROOT/$PLUGIN_DIR"
fi

BUILD_DIR="$PLUGIN_DIR/_build"

# Rebuild the per-device variants; the mashup pages are derived from them so the
# screen classes stay defined in one place.
bash "$SCRIPT_DIR/build-preview.sh" "$PLUGIN_DIR" --device "$DEVICE" --orientation "$ORIENTATION"

# Which view core would place in a cell of this shape.
default_layout_for_cell() {
  local cols="$1" rows="$2"
  if [[ "$cols" -eq "$rows" && "$cols" -eq 1 ]]; then echo quadrant
  elif [[ "$cols" -gt "$rows" ]]; then echo half_horizontal
  elif [[ "$rows" -gt "$cols" ]]; then echo half_vertical
  else echo full
  fi
}

placeholder_cells() {
  local count="$1" i
  for ((i = 0; i < count; i++)); do
    printf '      <div class="mashup-cell"><div class="view view--quadrant"><div class="layout flex flex--center"><span class="label">Plugin %s</span></div></div></div>\n' "$((i + 2))"
  done
}

build_cell_page() {
  local variant_dir="$1" cols="$2" rows="$3" layout="$4"
  local src="$variant_dir/${layout}.html"
  local out="$variant_dir/mashup-${cols}x${rows}.html"

  if [[ ! -f "$src" ]]; then
    echo "Skipping $(basename "$variant_dir")/${cols}x${rows} (${layout}.html not found)" >&2
    return
  fi

  local cell_open extra_closes fillers
  cell_open="      <div class=\"mashup-cell mashup-cell--col-1 mashup-cell--col-span-${cols} mashup-cell--row-1 mashup-cell--row-span-${rows}\">"
  fillers="$(placeholder_cells "$((9 - cols * rows))")"

  if grep -q '<div class="mashup mashup--' "$src"; then
    # The view already sits in a fixed mashup: swap that wrapper for the 3x3 grid.
    # One extra </div> is needed because the cell adds a nesting level.
    extra_closes=1
    awk -v open="$cell_open" -v fillers="$fillers" '
      /<div class="mashup mashup--/ {
        print "      <div class=\"mashup mashup--3x3\">"
        if (fillers != "") print fillers
        print open
        next
      }
      { print }
    ' "$src" > "$out"
  else
    # full.html has no mashup wrapper: add both the grid and the cell after .screen.
    extra_closes=2
    awk -v open="$cell_open" -v fillers="$fillers" '
      { print }
      /<div class="screen/ && !done {
        print "      <div class=\"mashup mashup--3x3\">"
        if (fillers != "") print fillers
        print open
        done = 1
      }
    ' "$src" > "$out"
  fi

  local closes=""
  local i
  for ((i = 0; i < extra_closes; i++)); do closes+="      </div>"$'\n'; done
  awk -v closes="$closes" '
    /<\/body>/ && !done { printf "%s", closes; done = 1 }
    { print }
  ' "$out" > "$out.tmp" && mv "$out.tmp" "$out"

  echo "Built: $(basename "$variant_dir")/mashup-${cols}x${rows}.html (${layout})"
}

# Same variant list build-preview.sh just produced — globbing _build/ instead would
# pick up stale directories from an earlier run with a different --device.
case "$DEVICE" in
  og)  DEVICES=(og) ;;
  x)   DEVICES=(x) ;;
  all) DEVICES=(og x) ;;
  *)   echo "Unknown device: $DEVICE (expected: og, x, all)" >&2; exit 1 ;;
esac
case "$ORIENTATION" in
  landscape) ORIENTATIONS=(landscape) ;;
  portrait)  ORIENTATIONS=(portrait) ;;
  all)       ORIENTATIONS=(landscape portrait) ;;
  *)         echo "Unknown orientation: $ORIENTATION (expected: landscape, portrait, all)" >&2; exit 1 ;;
esac

VARIANT_NAMES=()
for dev in "${DEVICES[@]}"; do
  for orient in "${ORIENTATIONS[@]}"; do
    if [[ "$dev" == "og" && "$orient" == "portrait" ]]; then continue; fi
    if [[ "$dev" == "og" ]]; then VARIANT_NAMES+=(og)
    elif [[ "$orient" == "portrait" ]]; then VARIANT_NAMES+=(x-portrait)
    else VARIANT_NAMES+=(x)
    fi
  done
done

for name in "${VARIANT_NAMES[@]}"; do
  variant_dir="$BUILD_DIR/$name"
  for spec in "${CELLS[@]}"; do
    size="${spec%%:*}"
    layout_override=""
    [[ "$spec" == *:* ]] && layout_override="${spec#*:}"
    cols="${size%%x*}"
    rows="${size##*x}"
    if [[ ! "$cols" =~ ^[1-3]$ || ! "$rows" =~ ^[1-3]$ ]]; then
      echo "Bad --cell value: $spec (expected <1-3>x<1-3>[:layout])" >&2
      exit 1
    fi
    layout="${layout_override:-$(default_layout_for_cell "$cols" "$rows")}"
    build_cell_page "$variant_dir" "$cols" "$rows" "$layout"
  done
done

if $SCREENSHOT; then
  if [[ -z "$OUTPUT_DIR" ]]; then
    SCREENSHOT_DIR="$PLUGIN_DIR"
  elif [[ "$OUTPUT_DIR" == /* || "$OUTPUT_DIR" == [A-Za-z]:* ]]; then
    SCREENSHOT_DIR="$OUTPUT_DIR"
  else
    SCREENSHOT_DIR="$PLUGIN_DIR/$OUTPUT_DIR"
  fi
  mkdir -p "$SCREENSHOT_DIR"

  # The pages pull the framework CSS and JS over https, so they have to be served
  # rather than opened from disk: playwright-cli refuses the file: protocol.
  port_listening() {
    python -c "import socket,sys; s=socket.socket(); s.settimeout(1); sys.exit(0 if s.connect_ex(('127.0.0.1',8765))==0 else 1)" 2>/dev/null
  }

  SERVER_PID=""
  if port_listening; then
    echo "Using the HTTP server already on port 8765 — it must be serving $BUILD_DIR."
  else
    python -m http.server 8765 --bind 127.0.0.1 --directory "$BUILD_DIR" > /dev/null 2>&1 &
    SERVER_PID=$!
    trap '[[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null' EXIT
    for _ in 1 2 3 4 5 6 7 8 9 10; do
      if port_listening; then break; fi
      sleep 1
    done
    if ! port_listening; then
      echo "Could not start an HTTP server on port 8765." >&2
      exit 1
    fi
    echo "Serving $BUILD_DIR on port 8765."
  fi

  # A mashup always occupies the whole screen, whatever the cell size.
  screen_size_for_variant() {
    case "$1" in
      og)         echo "800 480" ;;
      x)          echo "1040 780" ;;
      x-portrait) echo "780 1040" ;;
      *) echo "Unknown variant: $1" >&2; exit 1 ;;
    esac
  }

  for name in "${VARIANT_NAMES[@]}"; do
    dims=$(screen_size_for_variant "$name")
    viewport_w="${dims%% *}"
    viewport_h="${dims##* }"
    for spec in "${CELLS[@]}"; do
      size="${spec%%:*}"
      page="$BUILD_DIR/$name/mashup-${size}.html"
      [[ -f "$page" ]] || continue
      render_png="$SCREENSHOT_DIR/render-${name}-mashup-${size}.png"
      echo "Taking screenshot of ${name}/mashup-${size} (${viewport_w}x${viewport_h}) → $render_png"
      # A fresh browser per shot. Reusing one and navigating with `goto` looks
      # faster and silently is not: a failed navigation leaves the previous page
      # up and the screenshot is of the wrong view, with nothing in the output to
      # say so. Needs a threaded HTTP server, or the open stalls - see CLAUDE.md.
      # The retry is not belt and braces: screenshot writes no file every few
      # calls and still exits 0, so the file is the only honest check.
      rm -f "$render_png"
      for attempt in 1 2 3; do
        playwright-cli close > /dev/null 2>&1 || true
        playwright-cli open --browser=msedge "http://localhost:8765/${name}/mashup-${size}.html" > /dev/null 2>&1 || true
        playwright-cli resize "$viewport_w" "$viewport_h" > /dev/null 2>&1 || true
        sleep 3
        playwright-cli screenshot --filename="$render_png" > /dev/null 2>&1 || true
        playwright-cli close > /dev/null 2>&1 || true
        if [[ -f "$render_png" ]]; then break; fi
      done
      if [[ ! -f "$render_png" ]]; then
        echo "Failed to capture ${name}/mashup-${size} after 3 attempts." >&2
        exit 1
      fi
      if $ONEBIT; then
        if command -v magick &>/dev/null; then
          magick "$render_png" -colorspace Gray -threshold 60% -type Bilevel "$render_png"
          echo "Converted to 1-bit (no dithering): $render_png"
        else
          echo "ImageMagick not found — skipping 1-bit conversion"
        fi
      fi
    done
  done
fi

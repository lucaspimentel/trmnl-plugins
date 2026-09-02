#!/usr/bin/env bash
# sync-framework-docs.sh — vendor the TRMNL framework's own Markdown docs into the skill
# Usage: ./tools/sync-framework-docs.sh [--version <x.y>] [--clone <path>] [--all] [--no-generate]
#
# --version <x.y>:  docs track to copy, default 3.3. Must be one of the framework's
#                     SUPPORTED_DOCS_VERSIONS (1.2, 2.3, 3.0, 3.1, 3.3). Note this is the
#                     docs *track*, not the release: 3.3.0 and 3.3.1 both publish as 3.3.
# --clone <path>:   the usetrmnl/trmnl-framework checkout. Defaults to $TRMNL_FRAMEWORK_DIR,
#                     then to the sibling layout used on this machine.
# --all:            copy every generated page instead of the plugin-author subset in PAGES.
# --no-generate:    copy whatever the clone already generated; skip the rake task.
#
# Why vendor at all: the framework docs are the one TRMNL source with no working
# LLM-ready endpoint. trmnl.com/llms.txt advertises .md twins that 404, and the path
# that does resolve serves the full HTML page. The framework repo generates those .md
# files itself, so we render them locally and check in a copy stamped with the commit
# it came from. The copy is pinned to the docs track the plugin renders against, which
# is the point: hand-written notes drift silently, a stamped generated copy does not.
#
# The framework repo is MIT licensed and the grant covers its documentation; SOURCE.md
# records the copyright and the originating commit alongside each sync.
#
# Windows note: the rake task boots Rails, and Windows ships no zoneinfo database, so
# the framework's Gemfile needs `gem "tzinfo-data", platforms: %i[windows jruby]` or the
# task aborts with TZInfo::DataSources::ZoneinfoDirectoryNotFound before writing anything.
# That line is missing upstream. Add it locally (and revert it afterwards), or pass
# --no-generate and generate from a machine that has zoneinfo.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
SKILL_REFS="$REPO_ROOT/.claude/skills/trmnl-dev/references/framework"

VERSION=3.3
CLONE="${TRMNL_FRAMEWORK_DIR:-/d/source/usetrmnl/trmnl-framework}"
COPY_ALL=false
GENERATE=true

# The pages a *plugin* author needs. The framework also documents its own internals -
# paint, sass, themes, variables, maps, tiles, device rendering - which belong to
# framework and theme authors, not to this repo. Add a name here to pull one in.
PAGES=(
  # guides — read these when moving to a new framework version
  v3_overview v3_upgrade_guide v3_enhancement_guide trmnl_x_guide
  # foundation
  structure screen view layout title_bar columns mashup devices
  # arrangement
  size spacing gap flex grid aspect_ratio position
  # responsive
  responsive visibility
  # styling
  background border rounded outline image image_stroke text_stroke scale inverse
  colors color_palettes tokens
  # typography
  text_color text_size text_scale text_alignment font_family font_weight
  # elements
  title value label description divider
  # components
  rich_text item table chart progress
  # modulations
  overflow table_overflow clamp format_value fit_value content_limiter pixel_perfect
  framework_runtime
)

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)     VERSION="$2"; shift 2 ;;
    --clone)       CLONE="$2"; shift 2 ;;
    --all)         COPY_ALL=true; shift ;;
    --no-generate) GENERATE=false; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

[[ -d "$CLONE" ]] || {
  echo "framework clone not found: $CLONE" >&2
  echo "Clone https://github.com/usetrmnl/trmnl-framework and pass --clone, or set TRMNL_FRAMEWORK_DIR." >&2
  exit 1
}

if $GENERATE; then
  echo "Generating $VERSION docs in $CLONE ..."
  # The task regenerates every supported track, not just ours; there is no per-version flag.
  (cd "$CLONE" && bundle exec rake framework:generate_markdown >/dev/null)
fi

SRC="$CLONE/public/framework/docs/$VERSION"
[[ -d "$SRC" ]] || { echo "no generated docs at $SRC (run without --no-generate?)" >&2; exit 1; }

DEST="$SKILL_REFS/$VERSION"
rm -rf "$DEST"
mkdir -p "$DEST"

copied=0
missing=()
if $COPY_ALL; then
  cp "$SRC"/*.md "$DEST"/
  copied=$(find "$DEST" -maxdepth 1 -name '*.md' | wc -l)
else
  for page in "${PAGES[@]}"; do
    if [[ -f "$SRC/$page.md" ]]; then
      cp "$SRC/$page.md" "$DEST/$page.md"
      copied=$((copied + 1))
    else
      missing+=("$page")
    fi
  done
fi

SHA="$(cd "$CLONE" && git rev-parse HEAD)"
SUBJECT="$(cd "$CLONE" && git log -1 --format=%s)"
DATE="$(cd "$CLONE" && git log -1 --format=%cs)"

cat > "$DEST/SOURCE.md" <<EOF
# Generated framework docs — $VERSION

Do not edit these files. They are generated, and the next sync overwrites them.
Findings of our own that contradict or extend them belong in \`../updates.md\`.

| | |
|---|---|
| Docs track | $VERSION |
| Source | [usetrmnl/trmnl-framework](https://github.com/usetrmnl/trmnl-framework) |
| Commit | \`$SHA\` |
| Commit subject | $SUBJECT |
| Commit date | $DATE |
| Generated by | \`tools/sync-framework-docs.sh\` (framework's own \`rake framework:generate_markdown\`) |
| Pages | $copied |

Copyright (c) 2026 TRMNL, MIT licensed. The MIT grant covers the associated
documentation files, which is what these are.

Re-sync when \`framework_version\` in a plugin's \`src/settings.yml\` moves to a new
docs track, or when the framework ships a release on this track worth picking up:

\`\`\`bash
bash tools/sync-framework-docs.sh --version $VERSION
\`\`\`
EOF

echo "Copied $copied pages to ${DEST#"$REPO_ROOT"/}"
[[ ${#missing[@]} -eq 0 ]] || echo "Not present on this track (skipped): ${missing[*]}"
echo "Stamped $SHA ($DATE)"

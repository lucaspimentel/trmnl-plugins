#!/usr/bin/env bash
# push-plugin.sh — push a plugin to TRMNL, applying the staging overrides when needed
# Usage: ./tools/push-plugin.sh plugins/weather [--env <name>] [--dry-run] [--no-lint]
#
# --env <name>:  staging (default) or prod.
#                  prod    pushes src/settings.yml exactly as checked in.
#                  staging applies the overrides below in place, pushes, then restores the file.
# --dry-run:     apply the overrides and lint, print what would be pushed, restore, push nothing.
# --no-lint:     skip trmnlp lint. It runs by default and a failure aborts before pushing.
#
# The staging overrides, applied to a copy of src/settings.yml and reverted afterwards:
#   1. id:          -> the plugin's staging id (STAGING_IDS below)
#   2. polling_url: -> PROD_HOST replaced with STAGING_HOST, when the URL mentions it at all
#   3. name:        -> " (staging)" appended, so the two are distinguishable in the TRMNL UI
#
# src/settings.yml must have no uncommitted changes. The file is restored with
# `git checkout --`, which would discard them. The restore also runs for prod, because
# `trmnlp push` rewrites the file with the server's copy of the settings (adding the
# oauth_* keys and a description) whichever environment it is pushing to.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

# Staging plugin ids. The prod id is the one checked in at plugins/<name>/src/settings.yml;
# these are its staging counterpart, also recorded in each plugins/<name>/CLAUDE.md.
declare -A STAGING_IDS=(
  [weather]=316595
  [mbta-alerts]=316556
)

PROD_HOST="trmnl-plugins-prod.lucasp.net"
STAGING_HOST="trmnl-plugins-staging.lucasp.net"

PLUGIN_DIR=""
ENV_NAME="staging"
DRY_RUN=false
LINT=true

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env) ENV_NAME="$2"; shift ;;
    --dry-run) DRY_RUN=true ;;
    --no-lint) LINT=false ;;
    *) PLUGIN_DIR="$1" ;;
  esac
  shift
done

if [[ -z "$PLUGIN_DIR" ]]; then
  echo "Usage: push-plugin.sh <plugin-dir> [--env <name>] [--dry-run] [--no-lint]" >&2
  exit 1
fi

if [[ "$ENV_NAME" != "staging" && "$ENV_NAME" != "prod" ]]; then
  echo "Unknown env: $ENV_NAME (expected: staging, prod)" >&2
  exit 1
fi

# Accept either "weather" or "plugins/weather", absolute or relative to the repo root.
if [[ "$PLUGIN_DIR" != /* ]]; then
  if [[ -d "$REPO_ROOT/$PLUGIN_DIR" ]]; then
    PLUGIN_DIR="$REPO_ROOT/$PLUGIN_DIR"
  elif [[ -d "$REPO_ROOT/plugins/$PLUGIN_DIR" ]]; then
    PLUGIN_DIR="$REPO_ROOT/plugins/$PLUGIN_DIR"
  fi
fi

PLUGIN_NAME="$(basename "$PLUGIN_DIR")"
SETTINGS="$PLUGIN_DIR/src/settings.yml"

if [[ ! -f "$SETTINGS" ]]; then
  echo "No settings.yml at $SETTINGS" >&2
  echo "(trmnlp reads src/settings.yml exclusively; one at the plugin root is ignored)" >&2
  exit 1
fi

# The restore below is destructive, so refuse to start on top of uncommitted work.
if ! git -C "$PLUGIN_DIR" diff --quiet -- src/settings.yml ||
   ! git -C "$PLUGIN_DIR" diff --cached --quiet -- src/settings.yml; then
  echo "src/settings.yml has uncommitted changes." >&2
  echo "This script restores the file with 'git checkout --' after pushing, which would" >&2
  echo "discard them. Commit or stash them first." >&2
  exit 1
fi

restore_settings() {
  git -C "$PLUGIN_DIR" checkout -- src/settings.yml 2>/dev/null || true
}
trap restore_settings EXIT

if [[ "$ENV_NAME" == "staging" ]]; then
  STAGING_ID="${STAGING_IDS[$PLUGIN_NAME]:-}"
  if [[ -z "$STAGING_ID" ]]; then
    echo "No staging id known for plugin '$PLUGIN_NAME'." >&2
    echo "Add it to STAGING_IDS in $(basename "${BASH_SOURCE[0]}"), and to $PLUGIN_NAME/CLAUDE.md." >&2
    exit 1
  fi

  if ! grep -qE '^id: ' "$SETTINGS"; then
    echo "No top-level 'id:' key in $SETTINGS" >&2
    exit 1
  fi
  if ! grep -qE '^name: ' "$SETTINGS"; then
    echo "No top-level 'name:' key in $SETTINGS" >&2
    exit 1
  fi

  # Anchored at column 0 on purpose: custom_fields entries carry their own indented
  # 'name:' keys, and only the top-level one names the plugin.
  sed -i -E "s|^id: .*|id: $STAGING_ID|" "$SETTINGS"
  sed -i "s|$PROD_HOST|$STAGING_HOST|g" "$SETTINGS"
  sed -i -E "s|^name: (.*)$|name: \1 (staging)|" "$SETTINGS"

  echo "Staging overrides applied to $PLUGIN_NAME/src/settings.yml:"
  git -C "$PLUGIN_DIR" diff -U0 -- src/settings.yml 2>/dev/null |
    grep -E '^[+-]' | grep -vE '^(\+\+\+|---)' | sed 's/^/  /'
  echo
fi

cd "$PLUGIN_DIR"

if [[ "$LINT" == true ]]; then
  trmnlp lint
fi

if [[ "$DRY_RUN" == true ]]; then
  echo "Dry run: nothing pushed, restoring src/settings.yml."
  exit 0
fi

echo "Pushing $PLUGIN_NAME to $ENV_NAME..."
trmnlp push --force

# trap restores src/settings.yml, discarding both the overrides and whatever the
# server round-tripped back into the file during the push.

#!/usr/bin/env bash
# setup-env.sh — install the toolchain this repo needs (Ruby + trmnlp, .NET SDK).
# Usage: bash tools/setup-env.sh [--skip-ruby] [--skip-dotnet] [--skip-node]
#
# Idempotent: every step is skipped when a good-enough version is already present.
# Intended for a fresh Linux container (Claude Code on the web, CI, a new dev box).
# Ruby is built from source by rbenv when the system Ruby is too old, which takes
# several minutes; that is the slow step.

set -euo pipefail

# Keep these in sync with .github/workflows/plugins.yml and tests.yml.
RUBY_VERSION="4.0.2"        # workflow pins ruby-version 4.0
RUBY_MIN="3.4"              # trmnl_preview 0.11.0 requires >= 3.4
TRMNLP_VERSION="0.11.0"     # pinned in the plugins workflow
DOTNET_CHANNEL="10.0"

SKIP_RUBY=false
SKIP_DOTNET=false
SKIP_NODE=false
for arg in "$@"; do
  case "$arg" in
    --skip-ruby) SKIP_RUBY=true ;;
    --skip-dotnet) SKIP_DOTNET=true ;;
    --skip-node) SKIP_NODE=true ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 1 ;;
  esac
done

log() { printf '\n==> %s\n' "$*"; }

# Environment this repo needs in every future shell. Kept in its own file, sourced
# from the first line of ~/.bashrc for interactive shells. That is not enough on its
# own: `bash -c`, which is how Claude Code and CI run commands, reads no startup file
# at all, so see setup_path_wrappers below for how the tools reach those shells.
ENV_FILE="$HOME/.trmnl-plugins-env.sh"

persist() {
  grep -qF "$1" "$ENV_FILE" 2>/dev/null || printf '%s\n' "$1" >> "$ENV_FILE"
}

setup_shell_file() {
  [ -f "$ENV_FILE" ] || printf '# Written by trmnl-plugins tools/setup-env.sh\n' > "$ENV_FILE"
  local hook=". \"\$HOME/.trmnl-plugins-env.sh\"  # trmnl-plugins"
  if ! grep -qF 'trmnl-plugins-env.sh' "$HOME/.bashrc" 2>/dev/null; then
    printf '%s\n' "$hook" | cat - "$HOME/.bashrc" > "$HOME/.bashrc.tmp" 2>/dev/null \
      && mv "$HOME/.bashrc.tmp" "$HOME/.bashrc"
    log "Sourcing $ENV_FILE from the top of ~/.bashrc"
  fi
}

# trmnlp reads UTF-8 templates. Under the C/POSIX locale Ruby defaults its
# external encoding to US-ASCII and `trmnlp lint` dies with
# "invalid byte sequence in US-ASCII" on the first non-ASCII character.
setup_locale() {
  case "${LANG:-}" in
    *UTF-8|*utf8) log "Locale $LANG is already UTF-8" ;;
    *)
      log "Setting LANG=C.UTF-8 (trmnlp fails on UTF-8 templates under the C locale)"
      export LANG=C.UTF-8 LC_ALL=C.UTF-8
      ;;
  esac
  # Persisted even when this shell is already UTF-8: the wrappers below source this
  # file from shells that may not be.
  persist "export LANG=${LANG:-C.UTF-8}"
  persist "export LC_ALL=${LC_ALL:-${LANG:-C.UTF-8}}"
}

# Compares dotted versions: version_ge 3.4.9 3.4 -> true
version_ge() { [ "$(printf '%s\n%s\n' "$2" "$1" | sort -V | head -1)" = "$2" ]; }

# --- Ruby + trmnlp -----------------------------------------------------------

setup_ruby() {
  local current=""
  command -v ruby >/dev/null && current="$(ruby -e 'print RUBY_VERSION')"

  # rbenv's shims directory is not on PATH in every image. Add it whenever rbenv is
  # present, not just on the branch that installs: a later run finds a good-enough
  # Ruby, skips the install, and would otherwise never see the gems it put there.
  if command -v rbenv >/dev/null; then
    export PATH="$(rbenv root)/shims:$PATH"
    persist 'command -v rbenv >/dev/null && eval "$(rbenv init - bash)"'
    hash -r
    current=""
    command -v ruby >/dev/null && current="$(ruby -e 'print RUBY_VERSION')"
  fi

  if [ -n "$current" ] && version_ge "$current" "$RUBY_MIN"; then
    log "Ruby $current already satisfies >= $RUBY_MIN"
  elif command -v rbenv >/dev/null; then
    log "Ruby ${current:-none} is below $RUBY_MIN; installing $RUBY_VERSION with rbenv (slow, builds from source)"
    rbenv install -s "$RUBY_VERSION"
    rbenv global "$RUBY_VERSION"
    rbenv rehash
    hash -r
    log "Ruby is now $(ruby -e 'print RUBY_VERSION')"
  else
    echo "Ruby ${current:-none} is below $RUBY_MIN and rbenv is not installed." >&2
    echo "Install rbenv (https://github.com/rbenv/rbenv) or a Ruby >= $RUBY_MIN, then re-run." >&2
    return 1
  fi

  if [ "$(trmnlp version 2>/dev/null)" = "$TRMNLP_VERSION" ]; then
    log "trmnl_preview $TRMNLP_VERSION already installed"
  else
    log "Installing trmnl_preview $TRMNLP_VERSION"
    gem install trmnl_preview -v "$TRMNLP_VERSION" --no-document
    command -v rbenv >/dev/null && rbenv rehash || true
  fi
}

# --- .NET SDK ----------------------------------------------------------------

setup_dotnet() {
  # A previous run installs to $HOME/.dotnet, which is only on PATH in later shells.
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
    persist 'export DOTNET_ROOT="$HOME/.dotnet"'
    persist 'export PATH="$HOME/.dotnet:$PATH"'
  fi
  if command -v dotnet >/dev/null && dotnet --list-sdks | grep -q "^${DOTNET_CHANNEL%.*}\."; then
    log ".NET SDK $(dotnet --version) already installed"
    return 0
  fi

  log "Installing .NET SDK $DOTNET_CHANNEL to \$HOME/.dotnet"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$HOME/.dotnet"
  rm -f /tmp/dotnet-install.sh
  export PATH="$HOME/.dotnet:$PATH"
  # dotnet is not on PATH for later shells unless this is persisted.
  persist 'export DOTNET_ROOT="$HOME/.dotnet"'
  persist 'export PATH="$HOME/.dotnet:$PATH"' 
}

# --- Node / Playwright -------------------------------------------------------

setup_node() {
  # Only tools/build-preview.sh --screenshot needs a browser driver; skip silently
  # when Node is absent, since linting and pushing plugins do not use it.
  if ! command -v node >/dev/null; then
    log "Node not found; skipping Playwright (only needed for build-preview.sh --screenshot)"
    return 0
  fi
  if command -v playwright-cli >/dev/null; then
    log "playwright-cli already installed"
  else
    log "Node $(node -v) present. Install the screenshot driver yourself if you need it:"
    echo "    npm i -g @playwright/test   # browsers: PLAYWRIGHT_BROWSERS_PATH is preconfigured in the web container"
  fi
}

# --- Reachability from non-interactive shells --------------------------------

# ~/.bashrc is only read by interactive shells. Claude Code and CI invoke tools
# through `bash -c`, which reads no startup file, so rbenv shims and $HOME/.dotnet
# would be invisible there no matter where the hook is placed in ~/.bashrc. Put a
# wrapper for each tool in a directory that is already on the default PATH; the
# wrapper sources $ENV_FILE itself, which also fixes the locale trmnlp needs.
#
# ~/.local/bin, not /usr/local/bin: a container image may link `ruby` and `bundle`
# there for its own system Ruby, and this repo needs a newer one -- overriding them
# would change the Ruby every other tool in the container sees.
LINK_DIR=""

pick_link_dir() {
  local d
  mkdir -p "$HOME/.local/bin" 2>/dev/null || true
  for d in "$HOME/.local/bin" /usr/local/bin; do
    if [ -d "$d" ] && [ -w "$d" ] && case ":$PATH:" in *":$d:"*) true ;; *) false ;; esac; then
      LINK_DIR="$d"; return 0
    fi
  done
  log "No writable directory on PATH; tools will only be on PATH in login shells"
}

setup_path_wrappers() {
  pick_link_dir
  [ -n "$LINK_DIR" ] || return 0
  # `command -v` answers from bash's command hash, which still holds the pre-rbenv
  # /usr/local/bin/ruby this script probed before it put the shims on PATH. Without
  # this the wrappers would point back at the system Ruby they exist to bypass.
  hash -r
  local cmd target
  for cmd in ruby gem bundle bundler trmnlp dotnet; do
    target="$(command -v "$cmd" 2>/dev/null)" || continue
    [ -n "$target" ] || continue
    # Skip a wrapper this script wrote on an earlier run, or we would exec ourselves.
    case "$target" in "$LINK_DIR/$cmd") continue ;; esac
    # Unlink first: a redirection follows a symlink, and these names are often links
    # into a system toolchain -- writing through one destroys the binary it points at.
    rm -f "$LINK_DIR/$cmd"
    cat > "$LINK_DIR/$cmd" <<EOF
#!/usr/bin/env bash
# Written by trmnl-plugins tools/setup-env.sh -- makes $cmd reachable from \`bash -c\`.
. "$ENV_FILE"
exec "$target" "\$@"
EOF
    chmod +x "$LINK_DIR/$cmd"
  done
  log "Wrapped ruby, gem, bundle, trmnlp and dotnet into $LINK_DIR for non-interactive shells"
}

# --- .env --------------------------------------------------------------------

setup_env_file() {
  local root
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  if [ -f "$root/.env" ]; then
    log ".env already exists; leaving it alone"
  else
    log "Creating .env from .env.example (fill in the blanks; values are in 1Password item \"trmnl\")"
    cp "$root/.env.example" "$root/.env"
  fi
}

setup_shell_file
setup_locale
$SKIP_RUBY   || setup_ruby
$SKIP_DOTNET || setup_dotnet
$SKIP_NODE   || setup_node
setup_path_wrappers
setup_env_file

log "Done. Versions:"
command -v ruby    >/dev/null && ruby -v
command -v trmnlp  >/dev/null && echo "trmnlp $(trmnlp version)"
command -v dotnet  >/dev/null && dotnet --version
echo
echo "These tools are on PATH for new shells, interactive or not. Load repo secrets with:"
echo "    set -a && source .env && set +a"

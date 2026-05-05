#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: refresh-host-local-intent-cli.sh [HOST_ROOT]

Refresh HOST_ROOT/.intent-cli/bin/intent-cli from the current intent-system
checkout. HOST_ROOT defaults to $HOST_ROOT when set, otherwise the current
directory. CHILD_INTENT_SYSTEM may override the child checkout path.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

HOST_ROOT_INPUT="${1:-${HOST_ROOT:-$(pwd)}}"
HOST_ROOT="$(cd "$HOST_ROOT_INPUT" && pwd)"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHILD_INTENT_SYSTEM="${CHILD_INTENT_SYSTEM:-$(cd "$SCRIPT_DIR/.." && pwd)}"
CHILD_INTENT_SYSTEM="$(cd "$CHILD_INTENT_SYSTEM" && pwd)"

PROJECT_PATH="$CHILD_INTENT_SYSTEM/src/IntentSystem.Cli/IntentSystem.Cli.csproj"
PACKAGES_DIR="$HOST_ROOT/.intent-cli/packages"
BIN_DIR="$HOST_ROOT/.intent-cli/bin"
WRAPPER_PATH="$BIN_DIR/intent-cli"

if [[ ! -f "$PROJECT_PATH" ]]; then
  echo "intent-cli project not found: $PROJECT_PATH" >&2
  exit 1
fi

CHILD_SHA="$(git -C "$CHILD_INTENT_SYSTEM" rev-parse --short=12 HEAD)"
LOCAL_STAMP="$(date -u +%Y%m%d%H%M%S)"
INTENT_CLI_LOCAL_VERSION="${INTENT_CLI_LOCAL_VERSION:-0.1.0-local.$LOCAL_STAMP.$$.g$CHILD_SHA}"

if [[ "$INTENT_CLI_LOCAL_VERSION" == "0.1.0" ]]; then
  echo "INTENT_CLI_LOCAL_VERSION must not be the fixed package version 0.1.0." >&2
  exit 1
fi

mkdir -p "$PACKAGES_DIR" "$BIN_DIR"
find "$PACKAGES_DIR" -maxdepth 1 -type f -name 'intent-cli.*.nupkg' -delete

dotnet pack "$PROJECT_PATH" \
  -p:Version="$INTENT_CLI_LOCAL_VERSION" \
  -o "$PACKAGES_DIR"

cat > "$WRAPPER_PATH.tmp" <<EOF
#!/usr/bin/env bash
set -euo pipefail
HOST_ROOT="\$(cd "\$(dirname "\${BASH_SOURCE[0]}")/../.." && pwd)"
INTENT_CLI_LOCAL_VERSION="$INTENT_CLI_LOCAL_VERSION"
exec dotnet tool exec \\
  --yes \\
  --source "\$HOST_ROOT/.intent-cli/packages" \\
  --version "\$INTENT_CLI_LOCAL_VERSION" \\
  intent-cli -- \\
  "\$@"
EOF

chmod +x "$WRAPPER_PATH.tmp"
mv "$WRAPPER_PATH.tmp" "$WRAPPER_PATH"

SUMMARY_OUTPUT="$("$WRAPPER_PATH" automation summary --format json)"
if [[ "$SUMMARY_OUTPUT" != *'"automationCommandSurfaceVersion"'* ]]; then
  echo "refreshed wrapper did not emit automationCommandSurfaceVersion." >&2
  exit 1
fi

REQUIRED_CAPABILITIES=(
  "issue-publish"
  "pr-transition.review-start"
  "pr-transition.request-update"
  "pr-transition.approved"
)
for capability in "${REQUIRED_CAPABILITIES[@]}"; do
  if [[ "$SUMMARY_OUTPUT" != *"\"$capability\""* ]]; then
    echo "refreshed wrapper missing required automation capability: $capability" >&2
    exit 1
  fi
done

echo "Refreshed $WRAPPER_PATH"
echo "Package version: $INTENT_CLI_LOCAL_VERSION"
echo "Child checkout: $CHILD_INTENT_SYSTEM@$CHILD_SHA"

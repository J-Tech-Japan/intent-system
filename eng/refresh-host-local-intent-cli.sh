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
VERSION_POLICY_PATH="$CHILD_INTENT_SYSTEM/eng/version.json"
PACKAGES_DIR="$HOST_ROOT/.intent-cli/packages"
BIN_DIR="$HOST_ROOT/.intent-cli/bin"
WRAPPER_PATH="$BIN_DIR/intent-cli"
TEMP_WRAPPER_PATH="$WRAPPER_PATH.tmp"
CANDIDATE_PACKAGE_PATH=""
CANDIDATE_PACKAGES_DIR=""

cleanup_candidate() {
  rm -f "$TEMP_WRAPPER_PATH" || true
  if [[ -n "$CANDIDATE_PACKAGE_PATH" ]]; then
    rm -f "$CANDIDATE_PACKAGE_PATH" || true
  fi
  if [[ -n "$CANDIDATE_PACKAGES_DIR" && -d "$CANDIDATE_PACKAGES_DIR" ]]; then
    find "$CANDIDATE_PACKAGES_DIR" -maxdepth 1 -type f -delete || true
    rmdir "$CANDIDATE_PACKAGES_DIR" || true
  fi
}

refresh_failed() {
  local check="$1"
  local remedy="$2"
  local details="${3:-}"

  echo "host-local intent-cli refresh failed during $check." >&2
  if [[ -n "$details" ]]; then
    echo "$details" >&2
  fi
  echo "The previously installed wrapper was not changed." >&2
  echo "Remedy: $remedy" >&2
  exit 1
}

trap cleanup_candidate EXIT

if [[ ! -f "$PROJECT_PATH" ]]; then
  echo "intent-cli project not found: $PROJECT_PATH" >&2
  exit 1
fi

if [[ ! -f "$VERSION_POLICY_PATH" ]]; then
  echo "intent-cli version policy not found: $VERSION_POLICY_PATH" >&2
  exit 1
fi

PACKAGE_ID="$(sed -n 's:.*<PackageId>\([^<]*\)</PackageId>.*:\1:p' "$PROJECT_PATH")"
if [[ -z "$PACKAGE_ID" ]]; then
  echo "PackageId not found in intent-cli project: $PROJECT_PATH" >&2
  exit 1
fi

INTENT_CLI_BASE_VERSION="$(sed -n 's/^[[:space:]]*"nextVersion"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$VERSION_POLICY_PATH")"
if [[ -z "$INTENT_CLI_BASE_VERSION" ]]; then
  echo "nextVersion not found in intent-cli version policy: $VERSION_POLICY_PATH" >&2
  exit 1
fi

CHILD_SHA="$(git -C "$CHILD_INTENT_SYSTEM" rev-parse --short=12 HEAD)"
LOCAL_STAMP="$(date -u +%Y%m%d%H%M%S)"
INTENT_CLI_LOCAL_VERSION="${INTENT_CLI_LOCAL_VERSION:-$INTENT_CLI_BASE_VERSION-local.$LOCAL_STAMP.$$.g$CHILD_SHA}"

if [[ "$INTENT_CLI_LOCAL_VERSION" == "$INTENT_CLI_BASE_VERSION" ]]; then
  echo "INTENT_CLI_LOCAL_VERSION must not reuse the derived fixed package version $INTENT_CLI_BASE_VERSION." >&2
  exit 1
fi

mkdir -p "$PACKAGES_DIR" "$BIN_DIR"
CANDIDATE_PACKAGES_DIR="$PACKAGES_DIR/.refresh-$LOCAL_STAMP-$$"
mkdir "$CANDIDATE_PACKAGES_DIR"

if ! dotnet pack "$PROJECT_PATH" \
    -p:Version="$INTENT_CLI_LOCAL_VERSION" \
    -o "$CANDIDATE_PACKAGES_DIR"; then
  refresh_failed \
    "package build" \
    "Fix the reported dotnet pack error, then retry the refresh."
fi

shopt -s nullglob
candidate_packages=("$CANDIDATE_PACKAGES_DIR"/"$PACKAGE_ID".*.nupkg)
shopt -u nullglob
if [[ "${#candidate_packages[@]}" -ne 1 ]]; then
  refresh_failed \
    "package resolution" \
    "Confirm PackageId and nextVersion in the child checkout, then retry." \
    "Expected exactly one $PACKAGE_ID nupkg, found ${#candidate_packages[@]}."
fi

PACKAGE_FILENAME="$(basename "${candidate_packages[0]}")"
CANDIDATE_PACKAGE_PATH="$PACKAGES_DIR/$PACKAGE_FILENAME"
if [[ -e "$CANDIDATE_PACKAGE_PATH" ]]; then
  CANDIDATE_PACKAGE_PATH=""
  refresh_failed \
    "candidate package preparation" \
    "Choose a new INTENT_CLI_LOCAL_VERSION and retry; the requested package version already exists." \
    "Refusing to overwrite existing package: $PACKAGES_DIR/$PACKAGE_FILENAME"
fi

mv "${candidate_packages[0]}" "$CANDIDATE_PACKAGE_PATH"
rmdir "$CANDIDATE_PACKAGES_DIR"
CANDIDATE_PACKAGES_DIR=""

cat > "$TEMP_WRAPPER_PATH" <<EOF
#!/usr/bin/env bash
set -euo pipefail
HOST_ROOT="\$(cd "\$(dirname "\${BASH_SOURCE[0]}")/../.." && pwd)"
INTENT_CLI_LOCAL_VERSION="$INTENT_CLI_LOCAL_VERSION"
exec dotnet tool exec \\
  --yes \\
  --source "\$HOST_ROOT/.intent-cli/packages" \\
  --version "\$INTENT_CLI_LOCAL_VERSION" \\
  $PACKAGE_ID -- \\
  "\$@"
EOF

chmod +x "$TEMP_WRAPPER_PATH"

if ! VERSION_OUTPUT="$("$TEMP_WRAPPER_PATH" --version 2>&1)"; then
  refresh_failed \
    "version invocation" \
    "Inspect the package source and dotnet tool exec error above, then retry." \
    "$VERSION_OUTPUT"
fi

VERSION_IDENTITY_MATCHED=false
while IFS= read -r version_line; do
  if [[ "$version_line" == "intent-cli $INTENT_CLI_LOCAL_VERSION" \
      || "$version_line" == "intent-cli $INTENT_CLI_LOCAL_VERSION-"* ]]; then
    VERSION_IDENTITY_MATCHED=true
    break
  fi
done <<< "$VERSION_OUTPUT"

if [[ "$VERSION_IDENTITY_MATCHED" != "true" ]]; then
  refresh_failed \
    "version identity" \
    "Confirm eng/version.json and the local package version, then retry." \
    "Candidate reported an unexpected version: $VERSION_OUTPUT"
fi

if ! SUMMARY_OUTPUT="$("$TEMP_WRAPPER_PATH" automation summary --format json 2>&1)"; then
  refresh_failed \
    "automation summary" \
    "Inspect the candidate CLI error above, repair it, and retry the refresh." \
    "$SUMMARY_OUTPUT"
fi

if [[ "$SUMMARY_OUTPUT" != *'"automationCommandSurfaceVersion"'* ]]; then
  refresh_failed \
    "automation summary schema" \
    "Restore automationCommandSurfaceVersion in the candidate summary, then retry."
fi

REQUIRED_CAPABILITIES=(
  "issue-publish"
  "pr-transition.review-start"
  "pr-transition.request-update"
  "pr-transition.approved"
)
for capability in "${REQUIRED_CAPABILITIES[@]}"; do
  if [[ "$SUMMARY_OUTPUT" != *"\"$capability\""* ]]; then
    refresh_failed \
      "required capability check ($capability)" \
      "Restore the missing automation capability in the candidate CLI, then retry."
  fi
done

# Promotion is deliberately the final state-changing step. The candidate has
# passed every check above, and this same-filesystem rename replaces the old
# wrapper atomically. Until this line, the installed wrapper is untouched.
mv "$TEMP_WRAPPER_PATH" "$WRAPPER_PATH"
CANDIDATE_PACKAGE_PATH=""
trap - EXIT

echo "Refreshed $WRAPPER_PATH"
echo "Package version: $INTENT_CLI_LOCAL_VERSION"
echo "Child checkout: $CHILD_INTENT_SYSTEM@$CHILD_SHA"

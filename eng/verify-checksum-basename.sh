#!/usr/bin/env bash
# G409: Static verification that .github/workflows/release.yml generates
# checksum sidecars using the archive basename, not the dist/-prefixed path.
#
# Usage (from repo root):
#   bash eng/verify-checksum-basename.sh
#
# Exit 0 = PASS, exit 1 = FAIL, exit 2 = workflow file not found.
set -euo pipefail

WORKFLOW="${1:-.github/workflows/release.yml}"

if [ ! -f "$WORKFLOW" ]; then
  echo "ERROR: $WORKFLOW not found (run from repo root)" >&2
  exit 2
fi

fail=0

check_present() {
  local pattern="$1"
  local desc="$2"
  if ! grep -qF "$pattern" "$WORKFLOW"; then
    echo "FAIL: $desc"
    echo "      pattern '$pattern' not found in $WORKFLOW"
    fail=1
  fi
}

check_absent() {
  local pattern="$1"
  local desc="$2"
  if grep -qF "$pattern" "$WORKFLOW"; then
    echo "FAIL: $desc"
    echo "      pattern '$pattern' found in $WORKFLOW (must be absent)"
    fail=1
  fi
}

check_present 'BASENAME=' \
  'BASENAME variable must be set before hashing'
check_present 'cd dist' \
  'hasher must run inside dist/ so the sidecar records only the basename'

# Regression guards: the old path-based form must not be present.
check_absent 'sha256sum "${ASSET}"' \
  'dist/-prefixed ASSET form must not be passed to sha256sum'
check_absent 'shasum -a 256 "${ASSET}"' \
  'dist/-prefixed ASSET form must not be passed to shasum'

if [ "$fail" -eq 0 ]; then
  echo "PASS: $WORKFLOW generates checksum sidecars with basename (no dist/ prefix)"
fi
exit "$fail"

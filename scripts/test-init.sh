#!/usr/bin/env bash
# Validates that the Sonarr UI override init script (50-sonarr-override-ui.sh)
# keeps working against BOTH the classic Sonarr v4 frontend layout and the
# Vite-based Sonarr v5 layout. Runs anywhere bash exists (CI, the NAS, or a
# local git-bash):
#
#   bash scripts/test-init.sh
#
# For each layout it feeds the real init script a synthetic index.html, lets the
# script rebuild it, then asserts the rebuilt page carries the correct bundle
# tag, our picker script, and the Sonarr placeholders for that generation.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
INIT="$ROOT/init/50-sonarr-override-ui.sh"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PASS=0
FAIL=0

report() {
  local ok="$1" label="$2"
  if [ "$ok" -eq 0 ]; then
    PASS=$((PASS + 1))
    echo "  [PASS] $label"
  else
    FAIL=$((FAIL + 1))
    echo "  [FAIL] $label"
  fi
}

# Prepare a runnable copy: point its UI lookup at the sandbox and drop the
# background "re-protect" watchdog loop (120s sleep) so this test stays fast.
prepare() {
  local copy="$1"
  mkdir -p "$copy/ui"
  cp "$INIT" "$copy/50-sonarr-override-ui.sh"
  sed -i "s|/app/sonarr/bin/UI|$copy/ui|g; s|/opt/sonarr/UI|$copy/ui|g" "$copy/50-sonarr-override-ui.sh"
  awk 'BEGIN{keep=1} /^# Sonarr \(or its image\) may rewrite index\.html/{keep=0} keep{print}' \
    "$copy/50-sonarr-override-ui.sh" > "$copy/50-sonarr-override-ui.sh.tmp"
  mv "$copy/50-sonarr-override-ui.sh.tmp" "$copy/50-sonarr-override-ui.sh"
  printf 'var x = 1;\n' > "$copy/metadata-proxy-override.js"
}

run_rebuild() {
  local copy="$1"
  (cd "$copy" && bash ./50-sonarr-override-ui.sh >/dev/null)
}

# ---------------------------------------------------------------------------
echo "==> Sonarr v4 fixture"
V4="$(mktemp -d)"
prepare "$V4"
cat > "$V4/ui/index.html" <<'EOF4'
<!DOCTYPE html><html lang="en"><head>
<link rel="stylesheet" href="/Content/styles.css">
<script src="/index-abc123.js" data-no-hash></script>
</head><body><div id="root"></div></body></html>
EOF4
run_rebuild "$V4"
OUT="$(cat "$V4/ui/index.html")"

grep -qF '<script src="/index-abc123.js" data-no-hash></script>' <<<"$OUT"; report $? "v4 keeps classic bundle (data-no-hash)"
grep -qF "__URL_BASE__" <<<"$OUT"; report $? "v4 keeps __URL_BASE__ placeholder"
grep -qF '<div id="root"></div>' <<<"$OUT"; report $? "v4 keeps #root mount point"
grep -qF 'metadata-proxy-override.js?v=' <<<"$OUT"; report $? "v4 loads our picker script"
if grep -qF 'type="module"' <<<"$OUT"; then
  report 1 "v4 does not get an ESM tag"
else
  report 0 "v4 does not get an ESM tag"
fi
rm -rf "$V4"

# ---------------------------------------------------------------------------
echo "==> Sonarr v5 fixture"
V5="$(mktemp -d)"
prepare "$V5"
cat > "$V5/ui/index.html" <<'EOF5'
<!DOCTYPE html><html lang="en"><head>
<link rel="stylesheet" type="text/css" href="/Content/Fonts/fonts.css">
<link rel="stylesheet" crossorigin href="/assets/index-cf02e6f1e5a4c0f40ef2.css">
<script type="module" crossorigin src="/assets/index-cf02e6f1e5a4c0f40ef2.js"></script>
</head><body><div id="portal-root"></div><div id="root" class="root"></div></body></html>
EOF5
run_rebuild "$V5"
OUT="$(cat "$V5/ui/index.html")"

grep -qF '<script type="module" crossorigin src="/assets/index-cf02e6f1e5a4c0f40ef2.js"></script>' <<<"$OUT"; report $? "v5 loads Vite ESM bundle"
grep -qF 'href="/assets/index-cf02e6f1e5a4c0f40ef2.css"' <<<"$OUT"; report $? "v5 links the Vite stylesheet"
grep -qF '_THEME_' <<<"$OUT"; report $? "v5 keeps _THEME_ placeholder (theme injection)"
grep -qF '__MINI_PROFILER__' <<<"$OUT"; report $? "v5 keeps __MINI_PROFILER__ placeholder"
grep -qF '__URL_BASE__' <<<"$OUT"; report $? "v5 keeps __URL_BASE__ placeholder"
grep -qF '<div id="root" class="root"></div>' <<<"$OUT"; report $? "v5 keeps #root (root class) mount point"
grep -qF 'metadata-proxy-override.js?v=' <<<"$OUT"; report $? "v5 loads our picker script"
if grep -q 'data-no-hash' <<<"$OUT"; then
  report 1 "v5 output has no data-no-hash tags"
else
  report 0 "v5 output has no data-no-hash tags"
fi
rm -rf "$V5"

# ---------------------------------------------------------------------------
echo "==> Syntax"
bash -n "$INIT"; report $? "init script parses (bash -n)"

echo ""
echo "init compat: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
#!/usr/bin/with-contenv bash
# Injects the "metadata source" picker into the Sonarr web UI.
#
# This copies metadata-proxy-override.js (mounted next to this script) into the
# Sonarr UI directory and patches index.html so the script is loaded. The script
# adds a small per-series overlay: Automatisch / TMDB / TVDB, backed by the
# proxy's /api/overrides management API.
#
# Runs once at container start (LinuxServer /custom-cont-init.d hook). Recreate
# the container (or touch this script) to re-apply after a Sonarr update.

set -e

UI_DIR=""
for candidate in /app/sonarr/bin/UI /opt/sonarr/UI; do
  if [ -f "${candidate}/index.html" ]; then
    UI_DIR="${candidate}"
    break
  fi
done

if [ -z "${UI_DIR}" ]; then
  echo "[sonarr-metadata-proxy] Sonarr UI directory not found; skipping override UI patch."
  exit 0
fi

INDEX="${UI_DIR}/index.html"
SRC="/shared/init/metadata-proxy-override.js"
if [ ! -f "${SRC}" ]; then
  # fallback: old layout where the JS was mounted next to this script
  SRC="$(dirname "$0")/metadata-proxy-override.js"
fi
MARKER="metadata-proxy-override"

if [ -f "${SRC}" ]; then
  cp -f "${SRC}" "${UI_DIR}/metadata-proxy-override.js"
  chmod 644 "${UI_DIR}/metadata-proxy-override.js"
  echo "[sonarr-metadata-proxy] Copied override UI script to ${UI_DIR}/metadata-proxy-override.js"

  SONARR_CONFIG="/config/config.xml"
  if [ -f "${SONARR_CONFIG}" ]; then
    API_KEY="$(sed -n 's:.*<ApiKey>\([^<]*\)</ApiKey>.*:\1:p' "${SONARR_CONFIG}" | head -1)"
  else
    API_KEY=""
  fi

  if [ -n "${API_KEY}" ]; then
    sed -i "s/__SONARR_API_KEY__/${API_KEY}/g" "${UI_DIR}/metadata-proxy-override.js"
    echo "[sonarr-metadata-proxy] Embedded Sonarr API key into override UI script."
  else
    echo "[sonarr-metadata-proxy] No Sonarr API key found in ${SONARR_CONFIG}; key panel stays in UI."
  fi
else
  echo "[sonarr-metadata-proxy] ${SRC} not found; skipping script copy."
fi

if grep -q "${MARKER}" "${INDEX}" 2>/dev/null; then
  echo "[sonarr-metadata-proxy] index.html already patched; skip."
  exit 0
fi

if grep -q '</head>' "${INDEX}" 2>/dev/null; then
  sed -i '/metadata-proxy-override/d' "${INDEX}"
  sed -i 's#</head>#<script src="/metadata-proxy-override.js"></script></head>#' "${INDEX}"
  echo "[sonarr-metadata-proxy] index.html patched with override UI script."
else
  echo "[sonarr-metadata-proxy] No </head> found in ${INDEX}; skipping patch."
fi
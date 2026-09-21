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
SRC=""
for candidate in \
  "/shared/metadata-proxy-override.js" \
  "/shared/init/metadata-proxy-override.js" \
  "$(dirname "$0")/metadata-proxy-override.js"
do
  if [ -f "${candidate}" ]; then
    SRC="${candidate}"
    break
  fi
done
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
    # Replace only the first occurrence (the var assignment); the placeholder in
    # the runtime checks stays intact so getApiKey() can detect "not embedded".
    sed -i "0,/__SONARR_API_KEY__/s/__SONARR_API_KEY__/${API_KEY}/" "${UI_DIR}/metadata-proxy-override.js"
    if grep -q '__SONARR_API_KEY__' "${UI_DIR}/metadata-proxy-override.js"; then
      echo "[sonarr-metadata-proxy] WARNING: __SONARR_API_KEY__ placeholder still present; API key was not embedded."
    else
      echo "[sonarr-metadata-proxy] Embedded Sonarr API key into override UI script."
    fi
  else
    echo "[sonarr-metadata-proxy] No Sonarr API key found in ${SONARR_CONFIG}; key panel stays in UI."
  fi

  # Optional: reverse-proxy setup. When Sonarr is reached from a browser through
  # an HTTPS reverse proxy (e.g. Synology), the legacy fallback "http://<host>:9697"
  # is blocked as mixed content and never reaches the proxy. OVERRIDES_API_URL makes
  # the picker call the reverse-proxied management API instead; combine it with
  # CORS_ALLOWED_ORIGINS=<Sonarr browser origin> on the proxy container.
  if [ -n "${OVERRIDES_API_URL:-}" ]; then
    sed -i "0,/__OVERRIDES_API_URL__/s|__OVERRIDES_API_URL__|${OVERRIDES_API_URL}|" "${UI_DIR}/metadata-proxy-override.js"
    if grep -q '__OVERRIDES_API_URL__' "${UI_DIR}/metadata-proxy-override.js"; then
      echo "[sonarr-metadata-proxy] WARNING: __OVERRIDES_API_URL__ placeholder still present; OVERRIDES_API_URL is not reaching this script (set it on the Sonarr container, not the proxy)."
    else
      echo "[sonarr-metadata-proxy] Override UI points at ${OVERRIDES_API_URL} for the overrides API."
    fi
  else
    echo "[sonarr-metadata-proxy] OVERRIDES_API_URL unset; override UI falls back to http://<host>:9697 (LAN/port-forward only)."
  fi
else
  echo "[sonarr-metadata-proxy] ${SRC} not found; skipping script copy."
fi

if grep -q '</head>' "${INDEX}" 2>/dev/null; then
  # Idempotent patch: drop any previous include, then add a fresh one with a
  # cache-busting query. Sonarr serves the UI JS with cache headers, so without
  # ?v= the browser keeps running an outdated picker after an update (shows the
  # "overrides API unreachable" panel). Bumping the version on every start forces
  # a re-fetch.
  sed -i '/metadata-proxy-override/d' "${INDEX}"
  VSTAMP="$(date +%s)"
  sed -i "s#</head>#<script src=\"/metadata-proxy-override.js?v=${VSTAMP}\"></script></head>#" "${INDEX}"
  echo "[sonarr-metadata-proxy] index.html patched with override UI script (cache-bust v=${VSTAMP})."
else
  echo "[sonarr-metadata-proxy] No </head> found in ${INDEX}; skipping patch."
fi
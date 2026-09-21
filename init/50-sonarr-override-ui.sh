#!/usr/bin/with-contenv bash
# Injects the "metadata source" picker into the Sonarr web UI.
#
# This copies metadata-proxy-override.js (mounted next to this script) into the
# Sonarr UI directory and patches index.html so the script is loaded. The script
# adds a small per-series overlay: Automatisch / TMDB / TVDB, backed by the
# proxy's /api/overrides management API, and a "Search via" provider picker on
# the Add New search box (tmdb:/tvdb: prefixes).
#
# Runs at every container start (LinuxServer runs /custom-cont-init.d on each
# boot, not only at create), so an update or restart re-applies everything.

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
  "/shared/init/metadata-proxy-override.js" \
  "/shared/metadata-proxy-override.js" \
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
    # Busybox-safe replace of the FIRST occurrence only (no g flag, no GNU 0,
    # addressing). The placeholder lives on exactly one line of the JS; the
    # runtime check uses the indexOf('__SONARR_') prefix test which the pattern
    # below does not touch, so the check stays intact no matter what.
    sed -i "s/__SONARR_API_KEY__/${API_KEY}/" "${UI_DIR}/metadata-proxy-override.js"
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
    # Busybox-safe; the URL check uses origin.indexOf('http'), so a plain first-
    # occurrence replace can never corrupt the runtime logic.
    sed -i "s|__OVERRIDES_API_URL__|${OVERRIDES_API_URL}|" "${UI_DIR}/metadata-proxy-override.js"
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

# Some Sonarr images rewrite or truncate index.html at startup (the file can end
# mid-markup without <div id="root">, which blanks the whole UI with a React
# "Target container is not a DOM element" error). We stop patching via a naive
# </head> substitution and instead rebuild a complete, minimal index.html that
# guarantees the mount point AND our picker script. A short background loop then
# re-protects it in case Sonarr rewrites the file again after its app starts.
rebuild_index() {
  local js
  js="$(grep -o '/index-[a-f0-9]*\.js' "${INDEX}" 2>/dev/null | head -1)"
  [ -n "${js}" ] || js="/index-cf02e6f1e5a4c0f40ef2.js"
  local v
  v="$(date +%s)"
  local tmp="${INDEX}.mpo.tmp"
  {
    printf '<!doctype html><html lang="en"><head><meta charset="utf-8"/>\n'
    printf '<meta name="viewport" content="width=device-width,initial-scale=1"/>\n'
    printf '<link rel="stylesheet" href="/Content/Fonts/fonts.css">\n'
    printf '<link rel="stylesheet" href="/Content/styles.css">\n'
    printf '<style>html,body,#root{height:100%%;margin:0;}</style>\n'
    printf "<script>window.Sonarr = { urlBase: '__URL_BASE__' };</script>\n"
    printf '<script src="%s" data-no-hash></script>\n' "${js}"
    printf '<title>Sonarr</title>\n</head>\n<body><div id="root"></div>\n'
    printf '<script src="/metadata-proxy-override.js?v=%s"></script>\n' "${v}"
    printf '</body></html>\n'
  } > "${tmp}"
  mv -f "${tmp}" "${INDEX}"
  chmod 644 "${INDEX}"
}

index_ok() {
  [ -f "${INDEX}" ] \
    && grep -q 'id="root"' "${INDEX}" \
    && grep -q 'metadata-proxy-override' "${INDEX}"
}

if index_ok; then
  echo "[sonarr-metadata-proxy] index.html already complete (root mount point + picker script)."
else
  rebuild_index
  echo "[sonarr-metadata-proxy] Rebuilt index.html with root mount point + override UI script."
fi

# Sonarr (or its image) may rewrite index.html after the app starts; keep it intact.
(
  n=0
  while [ "${n}" -lt 24 ]; do
    sleep 5
    n=$((n + 1))
    if ! index_ok; then
      rebuild_index
      echo "[sonarr-metadata-proxy] Repaired index.html after it was rewritten (attempt ${n})."
    fi
  done
) &
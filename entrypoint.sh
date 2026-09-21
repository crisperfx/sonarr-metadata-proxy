#!/bin/sh
# Container entrypoint. Ships the Sonarr-side injection files (CA hook, override-UI
# hook and JS) into DATA_DIR/init so a brand new user needs no local files at all:
# a Sonarr container mounts the same volume as /shared and /custom-cont-init.d.
#
# Layout inside DATA_DIR:
#   init/       seed files below (kept in sync with the image)
#   certs/      generated CA + certificates (user data)
#   mappings/   mappings.json - the persisted override/TMDB store (user data)
#
# The seed files stay in sync with the image. Whenever an updated image ships a
# different file, it is refreshed automatically on the next start of the proxy,
# so pulling a new version is all it takes. Do NOT hand-edit these files in
# DATA_DIR - the image version always wins. User data (mappings/, certs/) is
# never touched.

set -e

DATA="${DATA_DIR:-/app/data}"
INIT_DIR="${DATA}/init"

mkdir -p "${DATA}" "${INIT_DIR}"

for f in 01-install-ca.sh 50-sonarr-override-ui.sh metadata-proxy-override.js; do
  if [ ! -f "${INIT_DIR}/$f" ]; then
    cp -f "/app/init/$f" "${INIT_DIR}/$f"
    echo "[sonarr-metadata-proxy] Seeded ${INIT_DIR}/$f from image."
  elif ! cmp -s "/app/init/$f" "${INIT_DIR}/$f"; then
    cp -f "/app/init/$f" "${INIT_DIR}/$f"
    echo "[sonarr-metadata-proxy] Refreshed ${INIT_DIR}/$f from image (content changed)."
  else
    echo "[sonarr-metadata-proxy] ${INIT_DIR}/$f already up to date."
  fi
done

exec dotnet Sonarr.MetadataProxy.dll
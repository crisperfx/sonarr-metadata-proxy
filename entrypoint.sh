#!/bin/sh
# Container entrypoint. Ships the Sonarr-side injection files (CA hook, override-UI
# hook and JS) into DATA_DIR so a brand new user needs no local files at all:
# a Sonarr container mounts the same volume as /shared and /custom-cont-init.d.
#
# The seed files stay in sync with the image. Whenever an updated image ships a
# different file, it is refreshed automatically on the next start of the proxy,
# so pulling a new version is all it takes. Do NOT hand-edit these files in
# DATA_DIR - the image version always wins. User data (mappings.json, certs/)
# is never touched.

set -e

DATA="${DATA_DIR:-/app/data}"

mkdir -p "${DATA}"

for f in 01-install-ca.sh 50-sonarr-override-ui.sh metadata-proxy-override.js; do
  if [ ! -f "${DATA}/$f" ]; then
    cp -f "/app/init/$f" "${DATA}/$f"
    echo "[sonarr-metadata-proxy] Seeded ${DATA}/$f from image."
  elif ! cmp -s "/app/init/$f" "${DATA}/$f"; then
    cp -f "/app/init/$f" "${DATA}/$f"
    echo "[sonarr-metadata-proxy] Refreshed ${DATA}/$f from image (content changed)."
  else
    echo "[sonarr-metadata-proxy] ${DATA}/$f already up to date."
  fi
done

exec dotnet Sonarr.MetadataProxy.dll
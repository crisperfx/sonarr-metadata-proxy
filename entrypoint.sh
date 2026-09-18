#!/bin/sh
# Container entrypoint. Makes the Sonarr-side injection files available to a brand
# new user who only pulled the image: the proxy ships init/ in the image and copies
# the files into its data volume (DATA_DIR) so a Sonarr container can mount the same
# volume as /shared and /custom-cont-init.d -- no host bind mounts required.

set -e

DATA="${DATA_DIR:-/app/data}"

for f in 01-install-ca.sh 50-sonarr-override-ui.sh metadata-proxy-override.js; do
  if [ ! -f "${DATA}/$f" ]; then
    cp -f "/app/init/$f" "${DATA}/$f"
  fi
done

exec dotnet Sonarr.MetadataProxy.dll
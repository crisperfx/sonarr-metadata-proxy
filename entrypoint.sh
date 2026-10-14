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
#
# Because Sonarr only runs /custom-cont-init.d at container start, a refreshed
# seed takes effect only after Sonarr restarts. Set RESTART_CONTAINER_NAME (and
# optionally DOCKER_PROXY_URL) to have the proxy restart that container itself
# whenever a seed actually changed on this start.

set -e

DATA="${DATA_DIR:-/app/data}"
INIT_DIR="${DATA}/init"

mkdir -p "${DATA}" "${INIT_DIR}"

changed=0
for f in 01-install-ca.sh 50-sonarr-override-ui.sh metadata-proxy-override.js; do
  if [ ! -f "${INIT_DIR}/$f" ]; then
    cp -f "/app/init/$f" "${INIT_DIR}/$f"
    echo "[sonarr-metadata-proxy] Seeded ${INIT_DIR}/$f from image."
    changed=1
  elif ! cmp -s "/app/init/$f" "${INIT_DIR}/$f"; then
    cp -f "/app/init/$f" "${INIT_DIR}/$f"
    echo "[sonarr-metadata-proxy] Refreshed ${INIT_DIR}/$f from image (content changed)."
    changed=1
  else
    echo "[sonarr-metadata-proxy] ${INIT_DIR}/$f already up to date."
  fi
done

# Sonarr runs /custom-cont-init.d only at container start, so a refreshed seed
# only takes effect after Sonarr's container restarts. When RESTART_CONTAINER_NAME
# is set and at least one seed actually changed (e.g. after pulling a new proxy
# image), ask the Docker API to restart that container. DOCKER_PROXY_URL (usually
# a locked-down socket proxy, see docker-compose.yml) keeps the raw Docker socket
# out of this container; without it we fall back to a mounted /var/run/docker.sock.
restart_target() {
  echo "[sonarr-metadata-proxy] Init files changed; restarting container '${RESTART_CONTAINER_NAME}' to apply them."
  if [ -n "${DOCKER_PROXY_URL:-}" ]; then
    curl -fsS -X POST "${DOCKER_PROXY_URL%/}/containers/${RESTART_CONTAINER_NAME}/restart?t=10" >/dev/null 2>&1 \
      && echo "[sonarr-metadata-proxy] Restart signal sent via ${DOCKER_PROXY_URL}." \
      || echo "[sonarr-metadata-proxy] WARNING: could not restart '${RESTART_CONTAINER_NAME}' via ${DOCKER_PROXY_URL}."
  elif [ -S /var/run/docker.sock ]; then
    curl -fsS --unix-socket /var/run/docker.sock -X POST "http://localhost/containers/${RESTART_CONTAINER_NAME}/restart?t=10" >/dev/null 2>&1 \
      && echo "[sonarr-metadata-proxy] Restart signal sent via /var/run/docker.sock." \
      || echo "[sonarr-metadata-proxy] WARNING: could not restart '${RESTART_CONTAINER_NAME}' via /var/run/docker.sock."
  else
    echo "[sonarr-metadata-proxy] RESTART_CONTAINER_NAME set but no Docker endpoint (DOCKER_PROXY_URL unset and /var/run/docker.sock missing); skipping restart."
  fi
}

if [ "${changed}" -eq 1 ] && [ -n "${RESTART_CONTAINER_NAME:-}" ]; then
  restart_target
fi

# Run the proxy as the unprivileged 'app' user (fixes ownership of the data
# volume from older root-run containers on each start).
if [ "$(id -u)" = "0" ]; then
  chown -R app:app "${DATA}" 2>/dev/null || true
  exec runuser -u app -- /usr/share/dotnet/dotnet Sonarr.MetadataProxy.dll
fi

exec dotnet Sonarr.MetadataProxy.dll
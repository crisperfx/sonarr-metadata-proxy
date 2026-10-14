#!/usr/bin/env bash
# Tests entrypoint.sh auto-restart behaviour without Docker.
# A stub `curl` records Docker API calls; temp dirs stand in for the image's
# /app/init (INIT_SRC) and the shared DATA_DIR. The trailing `dotnet` exec is
# stubbed too, so only the seed-sync + restart logic under test actually runs.
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SCRIPT="${ROOT}/entrypoint.sh"

pass=0
fail=0
report() {
  if [ "$1" -eq 0 ]; then
    echo "  [PASS] $2"
    pass=$((pass + 1))
  else
    echo "  [FAIL] $2"
    fail=$((fail + 1))
  fi
}

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

src="${work}/appinit"
data="${work}/data"
bin="${work}/bin"
mkdir -p "${src}" "${data}" "${bin}"

printf 'ca-v1\n' > "${src}/01-install-ca.sh"
printf 'ui-v1\n' > "${src}/50-sonarr-override-ui.sh"
printf 'js-v1\n' > "${src}/metadata-proxy-override.js"

cat > "${bin}/curl" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "${CURL_LOG}"
exit 0
STUB
printf '#!/usr/bin/env bash\nexit 0\n' > "${bin}/dotnet"
printf '#!/usr/bin/env bash\nexit 0\n' > "${bin}/runuser"
chmod +x "${bin}/curl" "${bin}/dotnet" "${bin}/runuser"

CURL_LOG="${work}/curl.log"

run() {
  CURL_LOG="${CURL_LOG}" PATH="${bin}:${PATH}" \
    DATA_DIR="${data}" INIT_SRC="${src}" \
    RESTART_CONTAINER_NAME="${RESTART_CONTAINER_NAME:-}" DOCKER_PROXY_URL="${DOCKER_PROXY_URL:-}" \
    bash "${SCRIPT}" > "${work}/out.log" 2>&1
}

count_restarts() { grep -c 'containers/.*/restart' "${CURL_LOG}" 2>/dev/null || true; }

echo "==> Auto-restart logic"

export RESTART_CONTAINER_NAME="Sonarr"
export DOCKER_PROXY_URL="http://docker-socket-proxy:2375"

: > "${CURL_LOG}"
run
report $? "entrypoint exits 0 on first run"
[ "$(count_restarts)" -eq 1 ]
report $? "first run (seed refresh) restarts the target container once"
grep -q 'POST http://docker-socket-proxy:2375/containers/Sonarr/restart' "${CURL_LOG}"
report $? "restart targets the configured container via DOCKER_PROXY_URL"

: > "${CURL_LOG}"
run
report $? "entrypoint exits 0 on an unchanged second run"
[ "$(count_restarts)" -eq 0 ]
report $? "no restart when nothing changed"

: > "${CURL_LOG}"
printf 'ui-v2\n' > "${src}/50-sonarr-override-ui.sh"
run
report $? "entrypoint exits 0 after a seed change"
[ "$(count_restarts)" -eq 1 ]
report $? "a changed seed triggers exactly one restart"

unset RESTART_CONTAINER_NAME
: > "${CURL_LOG}"
run
report $? "entrypoint exits 0 with no RESTART_CONTAINER_NAME"
[ "$(count_restarts)" -eq 0 ]
report $? "no restart when RESTART_CONTAINER_NAME is unset"

export RESTART_CONTAINER_NAME="Sonarr"
DOCKER_PROXY_URL=""
printf 'js-v2\n' > "${src}/metadata-proxy-override.js"
: > "${CURL_LOG}"
run
if [ -S /var/run/docker.sock ]; then
  echo "  [SKIP] no-Docker-endpoint warning (a real /var/run/docker.sock exists here)"
else
  grep -qi 'no docker endpoint' "${work}/out.log"
  report $? "warns and skips when no Docker endpoint is available"
fi
unset DOCKER_PROXY_URL

echo "entrypoint: ${pass} passed, ${fail} failed"
[ "${fail}" -eq 0 ]

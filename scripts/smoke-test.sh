#!/usr/bin/env bash
set -euo pipefail

IMAGE_TAG="${IMAGE_TAG:-v2-clam-av:smoke-test}"
CONTAINER_NAME="${CONTAINER_NAME:-v2-clam-av-smoke-$$}"
HOST_PORT="${HOST_PORT:-18080}"
BASE_URL="http://127.0.0.1:${HOST_PORT}"

cleanup() {
  docker rm -f "${CONTAINER_NAME}" >/dev/null 2>&1 || true
}

show_logs() {
  docker logs --tail 200 "${CONTAINER_NAME}" 2>&1 || true
}

trap cleanup EXIT

docker build --build-arg CI=true -t "${IMAGE_TAG}" .
docker run -d --name "${CONTAINER_NAME}" -p "127.0.0.1:${HOST_PORT}:8080" "${IMAGE_TAG}" >/dev/null

for attempt in {1..60}; do
  if curl --fail --silent --show-error "${BASE_URL}/healthz" | grep -q '"status":"ok"'; then
    break
  fi

  if [[ "$(docker inspect --format '{{.State.Running}}' "${CONTAINER_NAME}")" != "true" ]]; then
    echo "Container stopped before becoming ready." >&2
    show_logs
    exit 1
  fi

  if [[ "${attempt}" -eq 60 ]]; then
    echo "Container did not become ready within 120 seconds." >&2
    show_logs
    exit 1
  fi

  sleep 2
done

curl --fail --silent --show-error "${BASE_URL}/version" | grep -q '"clamAvVersion":"ClamAV '

clean_response="$({ printf '%s\n' 'hello from the automated smoke test'; } |
  curl --fail --silent --show-error -F 'file=@-;filename=clean.txt' "${BASE_URL}/scan")"
grep -q '"status":"clean"' <<<"${clean_response}"

# Split the standard EICAR string so endpoint protection does not quarantine this script.
# The literal dollar signs are part of the EICAR test string.
# shellcheck disable=SC2016
eicar_prefix='X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-'
# shellcheck disable=SC2016
eicar_suffix='STANDARD-ANTIVIRUS-TEST-FILE!$H+H*'
infected_response="$({ printf '%s%s' "${eicar_prefix}" "${eicar_suffix}"; } |
  curl --fail --silent --show-error -F 'file=@-;filename=eicar.txt' "${BASE_URL}/scan")"
grep -q '"status":"infected"' <<<"${infected_response}"

echo "Container smoke tests passed."

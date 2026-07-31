#!/usr/bin/env bash
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPOSITORY_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
readonly VERSION="${VERSION:-$(git -C "${REPOSITORY_ROOT}" describe --tags --always --dirty)}"
readonly OUTPUT_DIR="${OUTPUT_DIR:-${REPOSITORY_ROOT}/artifacts/iso}"
readonly CACHE_VOLUME="${CACHE_VOLUME:-fortos-iso-live-cache}"
readonly BUILDER_IMAGE="${BUILDER_IMAGE:-debian:12.11-slim}"

if [[ "$(uname -s)" != "Linux" ]]; then
    echo "error: ISO images must be built on a Linux host." >&2
    exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
    echo "error: Docker is required to build the Debian installation image." >&2
    exit 1
fi

if ! docker info >/dev/null 2>&1; then
    echo "error: the Docker daemon is not available to the current user." >&2
    exit 1
fi

mkdir -p "${OUTPUT_DIR}"

# live-build mounts pseudo filesystems and creates loop devices while assembling
# the hybrid image, therefore the disposable builder requires --privileged.
docker run --rm --privileged \
    --env "FortOS_VERSION=${VERSION}" \
    --env "DOTNET_SDK_VERSION=${DOTNET_SDK_VERSION:-10.0.302}" \
    --env "OUTPUT_UID=$(id -u)" \
    --env "OUTPUT_GID=$(id -g)" \
    --volume "${REPOSITORY_ROOT}:/workspace:ro" \
    --volume "${OUTPUT_DIR}:/output" \
    --mount "type=volume,source=${CACHE_VOLUME},target=/build/fortos-iso/live/cache" \
    --workdir /workspace \
    "${BUILDER_IMAGE}" \
    bash /workspace/eng/iso/build-in-container.sh

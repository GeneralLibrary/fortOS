#!/usr/bin/env bash
# -------------------------------------------------------------------------
# FortOS Debian ISO — 本地构建脚本(在 WSL / 任意 Debian 系主机直接运行,
# 无需 Docker;等价于 eng/iso/build-in-container.sh 的本地执行路径)。
#
# 依赖(dotnet SDK 10 优先取 /opt/dotnet,或 PATH 中的 dotnet):
#   live-build xorriso squashfs-tools curl apt-get git mtools dosfstools
#
# 用法:
#   bash eng/iso/build-local-wsl.sh
#   VERSION=v1.2.3 OUTPUT_DIR=/path bash eng/iso/build-local-wsl.sh
#
# 说明:
#   - 从 git index 恢复 Windows checkout 丢失的 includes.chroot symlink
#     (core.symlinks=false 会把 symlink 物化为普通文件,systemd 会因
#     "not a symlink" 拒绝 wants drop-in)。
#   - Docker 包用 apt pin 强制取 bookworm 版本(WSL 等 host 的发行版
#     版本号更高,apt 默认会解析到不存在的版本)。
#   - 输出 ISO 与 .sha256 到 ${OUTPUT_DIR}(默认 artifacts/iso)。
# -------------------------------------------------------------------------
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPOSITORY_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
readonly VERSION="${VERSION:-dev}"
readonly SAFE_VERSION="${VERSION//[^a-zA-Z0-9._-]/-}"
readonly BUILD_ROOT="${BUILD_ROOT:-${REPOSITORY_ROOT}/artifacts/iso-build}"
readonly PUBLISH_ROOT="${BUILD_ROOT}/publish"
readonly LIVE_ROOT="${BUILD_ROOT}/live"
readonly OUTPUT_DIR="${OUTPUT_DIR:-${REPOSITORY_ROOT}/artifacts/iso}"
readonly IMAGE_BASENAME="fortos-debian12-${SAFE_VERSION}-amd64"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DEBIAN_FRONTEND=noninteractive
export NUGET_PACKAGES="${BUILD_ROOT}/nuget"

# --- dotnet SDK(优先 /opt/dotnet,与 build-in-container.sh 一致)---
if ! command -v dotnet >/dev/null 2>&1 && [[ -x /opt/dotnet/dotnet ]]; then
    export PATH="/opt/dotnet:${PATH}"
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: dotnet SDK 10 not found — install it or place it at /opt/dotnet." >&2
    exit 1
fi

# --- 工具检查 ---
for cmd in lb xorriso mksquashfs curl apt-get git; do
    if ! command -v "${cmd}" >/dev/null 2>&1; then
        echo "error: ${cmd} is required (apt-get install live-build xorriso squashfs-tools ...)." >&2
        exit 1
    fi
done

# --- 清理上次被中断构建残留的 chroot 挂载(/proc /sys /dev 等)---
mount 2>/dev/null | grep "${BUILD_ROOT}" | awk '{print $3}' | sort -r \
    | while read -r m; do umount "${m}" 2>/dev/null || umount -l "${m}" 2>/dev/null || true; done || true
sleep 1

echo "=== 1/6 dotnet publish(4 个项目,已有产物则跳过)==="
mkdir -p "${PUBLISH_ROOT}/api" "${PUBLISH_ROOT}/cli" \
    "${PUBLISH_ROOT}/installer/gui" "${PUBLISH_ROOT}/installer/cli"

publish_if_missing() {
    local output="$1" bin="$2" extra="${3:-}"
    shift 3
    if [[ -x "${output}/${bin}" ]]; then
        echo "  ${bin} 已存在,跳过"
        return
    fi
    dotnet publish "${REPOSITORY_ROOT}/$1" \
        --configuration Release --runtime linux-x64 --self-contained true \
        ${extra} --output "${output}" >/dev/null
    echo "  ${bin} OK"
}
publish_if_missing "${PUBLISH_ROOT}/api" FortOS.Api "" "src/FortOS.Api/FortOS.Api.csproj"
publish_if_missing "${PUBLISH_ROOT}/cli" FortOS.Cli "" "src/FortOS.Cli/FortOS.Cli.csproj"
publish_if_missing "${PUBLISH_ROOT}/installer/gui" fortos-installer-gui \
    "-p:PublishTrimmed=true -p:TrimMode=partial" "src/FortOS.Installer.Gui/FortOS.Installer.Gui.csproj"
publish_if_missing "${PUBLISH_ROOT}/installer/cli" fortos-installer \
    "-p:PublishTrimmed=true -p:TrimMode=partial" "src/FortOS.Installer.Cli/FortOS.Installer.Cli.csproj"

echo "=== 2/6 lb config ==="
rm -rf "${LIVE_ROOT}"
mkdir -p "${LIVE_ROOT}"
cd "${LIVE_ROOT}"
lb config \
    --mode debian \
    --distribution bookworm \
    --architecture amd64 \
    --archive-areas "main contrib non-free-firmware" \
    --binary-image iso-hybrid \
    --checksums sha256 \
    --debian-installer live \
    --debian-installer-distribution bookworm \
    --debian-installer-gui true \
    --bootappend-live "boot=live components hostname=fortos locales=en_US.UTF-8,zh_CN.UTF-8 keyboard-layouts=us console=tty0 console=ttyS0,115200n8" \
    --iso-application "FortOS Debian 12 Installer" \
    --iso-publisher "FortOS Project" \
    --iso-volume "FortOS_${SAFE_VERSION:0:20}" \
    --uefi-secure-boot auto

echo "=== 3/6 复制 config + CRLF 归一化 + symlink 恢复 ==="
cp -a "${REPOSITORY_ROOT}/eng/iso/config/." "${LIVE_ROOT}/config/"
find "${LIVE_ROOT}/config" -type f -exec sed -i 's/\r$//' {} +
find "${LIVE_ROOT}/config/hooks" -type f -name '*.hook.chroot' -exec chmod 0755 {} +
# 恢复 Windows checkout 物化为普通文件的 git symlink(includes.chroot)
while read -r mode hash stage rel; do
    if [[ "${mode}" == "120000" && -n "${rel}" ]]; then
        src="${LIVE_ROOT}/config/${rel#eng/iso/config/}"
        if [[ -e "${src}" && ! -L "${src}" ]]; then
            target="$(cat "${src}" 2>/dev/null || true)"
            if [[ -n "${target}" && "${target}" != *$'\r'* ]]; then
                rm -f "${src}"
                ln -s "${target}" "${src}"
            fi
        fi
    fi
done < <(git -C "${REPOSITORY_ROOT}" ls-files -s eng/iso/config/includes.chroot 2>/dev/null || true)

echo "=== 4/6 复制 FortOS 产物到 includes.chroot ==="
mkdir -p "${LIVE_ROOT}/config/packages.chroot"
mkdir -p "${LIVE_ROOT}/config/includes.chroot/opt/fortos"
cp -a "${PUBLISH_ROOT}/api" "${LIVE_ROOT}/config/includes.chroot/opt/fortos/"
cp -a "${PUBLISH_ROOT}/cli" "${LIVE_ROOT}/config/includes.chroot/opt/fortos/"
cp -a "${PUBLISH_ROOT}/installer" "${LIVE_ROOT}/config/includes.chroot/opt/fortos/"
printf '%s\n' "${VERSION}" > "${LIVE_ROOT}/config/includes.chroot/etc/fortos/version"

echo "=== 5/6 Docker 包(apt pin 强制 bookworm 版本)==="
install -d -m 0755 /etc/apt/keyrings
curl --fail --show-error --silent --location --retry 3 \
    https://download.docker.com/linux/debian/gpg \
    --output /etc/apt/keyrings/docker.asc
printf '%s\n' \
    "deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian bookworm stable" \
    > /etc/apt/sources.list.d/docker.list
cat > /etc/apt/preferences.d/fortos-docker-pin <<'PINEOF'
Package: containerd.io docker-ce docker-ce-cli docker-buildx-plugin docker-compose-plugin
Pin: origin download.docker.com
Pin-Priority: 1001
PINEOF
apt-get -o Acquire::Retries=3 update >/dev/null 2>&1 || true
(
    cd "${LIVE_ROOT}/config/packages.chroot"
    apt-get download containerd.io docker-buildx-plugin docker-ce docker-ce-cli docker-compose-plugin >/dev/null 2>&1
)
mkdir -p "${LIVE_ROOT}/config/includes.chroot/etc/apt/keyrings"
cp /etc/apt/keyrings/docker.asc "${LIVE_ROOT}/config/includes.chroot/etc/apt/keyrings/docker.asc"
echo "  Docker 包: $(ls "${LIVE_ROOT}"/config/packages.chroot/*.deb 2>/dev/null | wc -l) debs"

echo "=== 6/6 lb build(完整 FortOS ISO)==="
cd "${LIVE_ROOT}"
lb build

echo "=== 输出 ISO ==="
mkdir -p "${OUTPUT_DIR}"
cp "${LIVE_ROOT}/live-image-amd64.hybrid.iso" "${OUTPUT_DIR}/${IMAGE_BASENAME}.iso"
(cd "${OUTPUT_DIR}" && sha256sum "${IMAGE_BASENAME}.iso" > "${IMAGE_BASENAME}.iso.sha256")
ls -la "${OUTPUT_DIR}/${IMAGE_BASENAME}.iso"
echo "=== 完成:${OUTPUT_DIR}/${IMAGE_BASENAME}.iso ==="

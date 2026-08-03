#!/usr/bin/env bash
set -Eeuo pipefail

readonly ISO_PATH="${1:?usage: test-image.sh <iso-path> [work-directory]}"
readonly WORK_DIR="${2:-$(mktemp -d)}"
readonly SQUASHFS_PATH="${WORK_DIR}/filesystem.squashfs"
readonly EXTRACTED_ROOT="${WORK_DIR}/root"

for command in xorriso unsquashfs sha256sum file; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "error: ${command} is required to inspect the ISO." >&2
        exit 1
    fi
done

if [[ ! -s "${ISO_PATH}" ]]; then
    echo "error: ISO does not exist or is empty: ${ISO_PATH}" >&2
    exit 1
fi

mkdir -p "${WORK_DIR}" "${EXTRACTED_ROOT}"

checksum_file="${ISO_PATH}.sha256"
if [[ -f "${checksum_file}" ]]; then
    (
        cd "$(dirname -- "${ISO_PATH}")"
        sha256sum --check "$(basename -- "${checksum_file}")"
    )
fi

boot_report="$(xorriso -indev "${ISO_PATH}" -report_el_torito plain 2>&1)"
printf '%s\n' "${boot_report}" > "${WORK_DIR}/el-torito.txt"
grep -Eq 'BIOS|El Torito boot img.*BIOS' "${WORK_DIR}/el-torito.txt"
grep -Eq 'UEFI|El Torito boot img.*UEFI' "${WORK_DIR}/el-torito.txt"

xorriso -osirrox on -indev "${ISO_PATH}" \
    -extract /live/filesystem.squashfs "${SQUASHFS_PATH}" \
    >/dev/null 2>&1

required_paths=(
    "opt/fortos/api/FortOS.Api"
    "opt/fortos/cli/FortOS.Cli"
    "opt/fortos/installer/gui/fortos-installer-gui"
    "opt/fortos/installer/gui/fortos-installer-kiosk.sh"
    "etc/fortos/fortos.env"
    "etc/fortos/version"
    "etc/systemd/system/fortos.service"
    "etc/systemd/system/fortos-installer.service"
    "etc/systemd/system/multi-user.target.wants/fortos.service"
    "etc/systemd/system/multi-user.target.wants/fortos-installer.service"
    "etc/systemd/system/multi-user.target.wants/docker.service"
    "etc/apt/keyrings/docker.asc"
    "etc/apt/sources.list.d/docker.list"
    "usr/bin/docker"
    "usr/lib/systemd/system/docker.service"
    "usr/local/bin/fortos"
)

unsquashfs -f -d "${EXTRACTED_ROOT}" "${SQUASHFS_PATH}" "${required_paths[@]}" \
    >/dev/null

for path in "${required_paths[@]}"; do
    target="${EXTRACTED_ROOT}/${path}"
    if [[ -L "${target}" ]]; then
        # Symlink — 在镜像根内解析目标,而非宿主机文件系统。
        # 绝对路径链接(如 /usr/local/bin/fortos -> /opt/fortos/cli/FortOS.Cli)
        # 在 CI runner 上宿主机不存在 /opt/fortos,裸 -e 会跟随并误报缺失;
        # 相对链接则基于链接所在目录解析。越界/悬空仍按错误处理。
        link_target="$(readlink "${target}")"
        case "${link_target}" in
            /*) candidate="${EXTRACTED_ROOT}/${link_target#/}" ;;
            *)  candidate="$(dirname -- "${target}")/${link_target}" ;;
        esac
        resolved="$(realpath -m "${candidate}")"
        case "${resolved}" in
            "${EXTRACTED_ROOT}"|"${EXTRACTED_ROOT}"/*) ;;
            *)
                echo "error: required installed path resolves outside the image root: /${path} -> ${link_target}" >&2
                exit 1
                ;;
        esac
        if [[ ! -e "${resolved}" ]]; then
            echo "error: required installed path is a broken symlink: /${path} -> ${link_target}" >&2
            exit 1
        fi
    elif [[ ! -e "${target}" ]]; then
        echo "error: required installed path is missing: /${path}" >&2
        exit 1
    fi
done

file "${EXTRACTED_ROOT}/opt/fortos/api/FortOS.Api" | grep -q 'ELF 64-bit.*x86-64'
file "${EXTRACTED_ROOT}/opt/fortos/cli/FortOS.Cli" | grep -q 'ELF 64-bit.*x86-64'
file "${EXTRACTED_ROOT}/opt/fortos/installer/gui/fortos-installer-gui" | grep -q 'ELF 64-bit.*x86-64'
grep -q '^ExecStart=/opt/fortos/api/FortOS.Api$' \
    "${EXTRACTED_ROOT}/etc/systemd/system/fortos.service"
grep -q '^ExecStart=/opt/fortos/installer/gui/fortos-installer-kiosk.sh$' \
    "${EXTRACTED_ROOT}/etc/systemd/system/fortos-installer.service"
grep -q '^FortOS_DATA_ROOT=/srv/nas$' \
    "${EXTRACTED_ROOT}/etc/fortos/fortos.env"

echo "ISO boot catalog and installed FortOS payload are valid."

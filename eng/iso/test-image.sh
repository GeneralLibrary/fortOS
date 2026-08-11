#!/usr/bin/env bash
set -Eeuo pipefail

readonly ISO_PATH="${1:?usage: test-image.sh <iso-path> [work-directory]}"
readonly WORK_DIR="${2:-$(mktemp -d)}"
# 目标架构:优先 ARCH 环境变量,否则从 ISO 文件名(fortos-debian12-*-<arch>.iso)推断。
readonly ISO_ARCH="${ARCH:-$(basename "${ISO_PATH}" | sed -nE 's/.*-(amd64|arm64)\.iso$/\1/p')}"
if [[ -z "${ISO_ARCH}" ]]; then
    echo "error: cannot determine target architecture from '${ISO_PATH}' — set ARCH=amd64|arm64." >&2
    exit 1
fi
case "${ISO_ARCH}" in
    amd64|arm64) ;;
    *) echo "error: unsupported ARCH '${ISO_ARCH}' (expected amd64 or arm64)." >&2; exit 1 ;;
esac
readonly SQUASHFS_PATH="${WORK_DIR}/filesystem.squashfs"
readonly EXTRACTED_ROOT="${WORK_DIR}/root"

for command in xorriso unsquashfs sha256sum file isoinfo; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "error: ${command} is required to inspect the ISO (apt-get install genisoimage)." >&2
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
# amd64 的 live-build ISO 是 isohybrid:BIOS(isolinux)+ UEFI(grub-efi)双 El Torito 条目;
# arm64 的 live-build ISO 是纯 GRUB 引导(无 El Torito 记录,arm64 固件直接从
# ISO 9660 加载 /BOOT/GRUB),因此 El Torito 检查只在 amd64 上执行,
# arm64 改为验证 GRUB 引导文件存在。
if [[ "${ISO_ARCH}" == "amd64" ]]; then
    grep -Eq 'BIOS|El Torito boot img.*BIOS' "${WORK_DIR}/el-torito.txt"
    grep -Eq 'UEFI|El Torito boot img.*UEFI' "${WORK_DIR}/el-torito.txt"
else
    # 校验 arm64 引导文件在 ISO 中真实存在(grub.cfg + live 内核/initrd)。
    # 必须用 -R(RockRidge)读取,否则会看到 Joliet 的 8.3 截断名(下划线),
    # 而 grub.cfg 引用的是 RockRidge 长名(连字符)。
    iso_listing="$(isoinfo -i "${ISO_PATH}" -R -f 2>/dev/null | tr '[:upper:]' '[:lower:]' | sed 's/;[0-9]*$//')"
    for p in '/boot/grub/grub.cfg' '/live/vmlinuz-' '/live/initrd.img-'; do
        if ! grep -q "^${p}" <<<"${iso_listing}"; then
            echo "error: required ARM64 boot path not found in ISO: ${p}*" >&2
            exit 1
        fi
    done
fi

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
        # Symlink — resolve the target inside the image root, not on the host filesystem.
        # For absolute-path links (e.g. /usr/local/bin/fortos -> /opt/fortos/cli/FortOS.Cli), the host
        # on a CI runner has no /opt/fortos, so a bare -e would follow the link and wrongly report it missing;
        # relative links are resolved from the directory containing the link. Out-of-root/broken links are still errors.
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

elf_pattern='ELF 64-bit.*x86-64'
if [[ "${ISO_ARCH}" == "arm64" ]]; then
    elf_pattern='ELF 64-bit.*aarch64'
fi
file "${EXTRACTED_ROOT}/opt/fortos/api/FortOS.Api" | grep -q "${elf_pattern}"
file "${EXTRACTED_ROOT}/opt/fortos/cli/FortOS.Cli" | grep -q "${elf_pattern}"
file "${EXTRACTED_ROOT}/opt/fortos/installer/gui/fortos-installer-gui" | grep -q "${elf_pattern}"
grep -q '^ExecStart=/opt/fortos/api/FortOS.Api$' \
    "${EXTRACTED_ROOT}/etc/systemd/system/fortos.service"
grep -q '^ExecStart=/opt/fortos/installer/gui/fortos-installer-kiosk.sh$' \
    "${EXTRACTED_ROOT}/etc/systemd/system/fortos-installer.service"
grep -q '^FortOS_DATA_ROOT=/srv/nas$' \
    "${EXTRACTED_ROOT}/etc/fortos/fortos.env"

echo "ISO boot catalog and installed FortOS payload are valid."

#!/usr/bin/env bash
# -------------------------------------------------------------------------
# installer-e2e.sh — FortOS.Installer scenario-matrix integration test (design doc §9 / M4)
#
# Boots the live ISO (which embeds FortOS.Installer.Cli) in QEMU, drives
# `fortos-installer --config install.yaml --yes` through the serial port to complete a full installation,
# then asserts the target disk offline: partition table / fstab / grub / install-summary.json / crypttab / mdadm.
#
# Scenario matrix (scenario parameter):
#   single-btrfs   single system disk btrfs (default)
#   single-ext4    single system disk ext4
#   raid1          system disk + 2x whole-disk mdadm RAID1 (btrfs)
#   luks           system disk + 1x whole-disk LUKS2 (btrfs)
#
# Dependencies: xorriso qemu-system-x86_64 qemu-img expect mkfs.vfat mcopy
#       qemu-nbd (offline assertions; the CI container runs as root)
#
# Usage: installer-e2e.sh <iso-path> <result-directory> [scenario]
# Note: designed to run inside a Linux CI container (same environment as test-install.sh).
# -------------------------------------------------------------------------
set -Eeuo pipefail

readonly ISO_PATH="${1:?usage: installer-e2e.sh <iso-path> <result-directory> [scenario]}"
readonly RESULT_DIR="${2:?result directory is required}"
readonly SCENARIO="${3:-single-btrfs}"
readonly BOOT_DIR="${RESULT_DIR}/live"
readonly CONFIG_IMG="${RESULT_DIR}/install-config.img"
readonly CONFIG_MNT="${RESULT_DIR}/config-mnt"
readonly SERIAL_LOG="${RESULT_DIR}/installer-serial.log"
readonly INSTALL_YAML="install.yaml"

for command in xorriso qemu-img qemu-system-x86_64 expect mkfs.vfat mcopy qemu-nbd; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "error: ${command} is required for the installer E2E test." >&2
        exit 1
    fi
done

rm -rf "${RESULT_DIR}"
mkdir -p "${BOOT_DIR}" "${CONFIG_MNT}"

# --- Scenario definition: root filesystem + data disk layout + scenario assertions --------------------------
ROOT_FS=btrfs
DATA_DISK_SIZES=()          # data disk sizes (QEMU disks appended after the system disk)
data_yaml() { :; }          # emits the data: section of install.yaml
extra_assert() { :; }       # scenario-specific assertions (rootfs mount point path is $1)

case "${SCENARIO}" in
    single-btrfs) ;;

    single-ext4) ROOT_FS=ext4 ;;

    raid1)
        DATA_DISK_SIZES=("5G" "5G")
        data_yaml() {
            cat <<'YAML'
data:
  mode: raid
  raidLevel: 1
  raidDisks: [/dev/vdb, /dev/vdc]
  fs: btrfs
  label: FORTOS_DATA
YAML
        }
        extra_assert() {
            python3 - "$1" <<'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
mdadm = (root / "etc/mdadm/mdadm.conf").read_text()
# With --name=fortos-data, mdadm --detail --scan outputs a homehost-style
# "ARRAY /dev/md/fortos-data ... name=fortos-data"; do not assert a specific device name.
assert "ARRAY" in mdadm and "name=fortos-data" in mdadm, f"mdadm.conf missing array: {mdadm}"
assert not (root / "etc/crypttab").exists(), "crypttab should be absent for RAID"
print("RAID assertions passed")
PY
        }
        ;;

    luks)
        DATA_DISK_SIZES=("5G")
        data_yaml() {
            cat <<'YAML'
data:
  mode: luks
  disk: /dev/vdb
  luksPassphrase: fortos-e2e-luks
  fs: btrfs
  label: FORTOS_DATA
YAML
        }
        extra_assert() {
            python3 - "$1" <<'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
crypttab = (root / "etc/crypttab").read_text()
assert "fortos-data UUID=" in crypttab and "none luks" in crypttab, f"crypttab invalid: {crypttab}"
fstab = (root / "etc/fstab").read_text()
assert "/dev/mapper/fortos-data /srv/nas" in fstab, f"fstab must mount mapper device: {fstab}"
print("LUKS assertions passed")
PY
        }
        ;;

    *)
        echo "error: unknown scenario '${SCENARIO}'. Allowed: single-btrfs single-ext4 raid1 luks" >&2
        exit 1
        ;;
esac

# --- 1. Extract live kernel and initrd --------------------------------------------
extract_live() {
    local path="$1" dest="$2"
    xorriso -osirrox on -indev "${ISO_PATH}" -extract "${path}" "${dest}" >/dev/null 2>&1
}
extract_live /live/vmlinuz "${BOOT_DIR}/vmlinuz" || extract_live /isolinux/live/vmlinuz "${BOOT_DIR}/vmlinuz"
extract_live /live/initrd.img "${BOOT_DIR}/initrd.img" || extract_live /isolinux/live/initrd.img "${BOOT_DIR}/initrd.img"
[[ -s "${BOOT_DIR}/vmlinuz" && -s "${BOOT_DIR}/initrd.img" ]] || {
    echo "error: live kernel/initrd not found in ISO." >&2
    exit 1
}

# --- 2. Generate install.yaml and pack it as a vfat config disk -----------------------------
{
    cat <<YAML
# FortOS E2E scenario: ${SCENARIO}
system:
  disk: /dev/vda
  rootFs: ${ROOT_FS}
  swap: off
YAML
    data_yaml
    cat <<'YAML'
network:
  mode: dhcp
  hostname: fortos-e2e
account:
  username: admin
  password: fortos-e2e-pass
  timezone: UTC
locale:
  lang: en_US.UTF-8
  keyboard: us
YAML
} > "${CONFIG_MNT}/${INSTALL_YAML}"

mkfs.vfat -C "${CONFIG_IMG}" 64 >/dev/null 2>&1
for f in "${CONFIG_MNT}"/*; do
    mcopy -i "${CONFIG_IMG}" "${f}" "::/$(basename "${f}")"
done

# --- 3. Target virtual disks: system disk disk-0 + data disks disk-1..N ------------------------
declare -a DISK_PATHS=()
i=0
for size in "20G" "${DATA_DISK_SIZES[@]}"; do
    disk_path="${RESULT_DIR}/disk-${i}.qcow2"
    rm -f "${disk_path}"
    qemu-img create -q -f qcow2 "${disk_path}" "${size}"
    DISK_PATHS+=("${disk_path}")
    i=$((i + 1))
done
SYSTEM_DISK="${DISK_PATHS[0]}"
DATA_DISK_PATHS_STR=""
for ((i = 1; i < ${#DISK_PATHS[@]}; i++)); do
    DATA_DISK_PATHS_STR="${DATA_DISK_PATHS_STR} ${DISK_PATHS[i]}"
done

# --- 4. QEMU + expect driven install ----------------------------------------------
cat > "${RESULT_DIR}/drive-install.exp" <<'EXP'
#!/usr/bin/expect -f
set timeout 1800
log_user 1
set serial_log [lindex $argv 0]
set iso         [lindex $argv 1]
set vmlinuz     [lindex $argv 2]
set initrd      [lindex $argv 3]
set cfgimg      [lindex $argv 4]
set system_disk [lindex $argv 5]
set data_disks  [split [lindex $argv 6] " "]

set cmd [list qemu-system-x86_64 \
    -machine q35,accel=kvm \
    -cpu host \
    -m 3072 -smp 2 \
    -drive "file=$system_disk,if=virtio,format=qcow2"]
foreach d $data_disks {
    if {$d ne ""} { lappend cmd -drive "file=$d,if=virtio,format=qcow2" }
}
lappend cmd \
    -drive "file=$cfgimg,format=raw,if=virtio" \
    -cdrom "$iso" \
    -kernel "$vmlinuz" -initrd "$initrd" \
    -append "boot=live components hostname=fortos console=tty0 console=ttyS0,115200n8" \
    -nic user,model=virtio-net-pci \
    -nographic -no-reboot

# The guest serial (ttyS0) goes to QEMU stdio via -nographic; expect interacts there and matches the
# login prompt. -serial file: cannot be used: guest output would all go to the file, leaving only the
# "(qemu)" monitor prompt on stdio, so expect would never match the login prompt.
log_file "$serial_log"

spawn {*}$cmd

# The live system serial console has no auto-login; log in manually. The user is "user" (password "live",
# live-build default --password live); root login requires a password and is not applicable in this non-interactive scenario.
expect {
    -re "fortos login:" { send "user\r"; exp_continue }
    -re "Password:" { send "live\r"; exp_continue }
    -re {[#$] $} { }
    timeout { puts "TIMEOUT waiting for live shell"; exit 2 }
}

# Mount the config disk (the last attached virtio disk, located by content; mounting needs root).
# Note: \$d is escaped at the Tcl layer as the literal $d, substituted by the guest's sh loop variable;
# use test -f rather than [ -f ] to keep Tcl from parsing [ as command substitution.
send "sudo sh -c 'mkdir -p /mnt/cfg; for d in /dev/vd?; do mount \$d /mnt/cfg 2>/dev/null && test -f /mnt/cfg/install.yaml && exit 0; umount /mnt/cfg 2>/dev/null; done; exit 1'\r"
expect {
    -re {password for user:} { send "live\r"; exp_continue }
    -re {[#$] $} { }
    timeout { puts "TIMEOUT mounting config"; exit 2 }
}
send "sudo fortos-installer --config /mnt/cfg/install.yaml --yes\r"
expect {
    "FortOS installation completed successfully." {
        send "sudo poweroff -f\r"
        expect eof
        exit 0
    }
    -re {Installation failed} {
        puts "INSTALL FAILED (see serial log)"
        send "sudo poweroff -f\r"
        expect eof
        exit 3
    }
    timeout { puts "TIMEOUT during installation"; exit 2 }
    eof { puts "QEMU exited unexpectedly"; exit 4 }
}
EXP

set +e
timeout 45m expect "${RESULT_DIR}/drive-install.exp" \
    "${SERIAL_LOG}" "${ISO_PATH}" \
    "${BOOT_DIR}/vmlinuz" "${BOOT_DIR}/initrd.img" "${CONFIG_IMG}" \
    "${SYSTEM_DISK}" "${DATA_DISK_PATHS_STR}"
expect_exit=$?
set -e

if [[ ${expect_exit} -ne 0 ]]; then
    echo "error: installer E2E expect driver exited ${expect_exit}." >&2
    exit 1
fi
[[ -s "${SYSTEM_DISK}" ]] || { echo "error: installation did not produce a system disk." >&2; exit 1; }

# --- 5. Offline assertions (mount system disk p3 with qemu-nbd) -----------------------------------
[[ -e /dev/nbd0 ]] || { echo "error: /dev/nbd0 unavailable (need nbd module + root)." >&2; exit 1; }
qemu-nbd --connect=/dev/nbd0 "${SYSTEM_DISK}"
trap 'umount "${RESULT_DIR}/rootfs" 2>/dev/null || true; qemu-nbd --disconnect /dev/nbd0 >/dev/null 2>&1 || true' EXIT
mkdir -p "${RESULT_DIR}/rootfs"
# After the nbd connection, the partition node (nbd0p3) may not exist yet: trigger a kernel re-scan of the
# partition table and wait for the node to appear before mounting.
partprobe /dev/nbd0 2>/dev/null || partx -a /dev/nbd0 2>/dev/null || true
for _ in $(seq 1 20); do
    [[ -b /dev/nbd0p3 ]] && break
    sleep 1
done
mount /dev/nbd0p3 "${RESULT_DIR}/rootfs"

python3 - "${RESULT_DIR}/rootfs" <<'PY'
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
summary = json.loads((root / "etc/fortos/install-summary.json").read_text())
assert summary.get("Success") is True, f"install-summary.json Success != true: {summary}"
assert (root / "boot/grub/grub.cfg").exists(), "grub.cfg missing"
fstab = (root / "etc/fstab").read_text()
assert "UUID=" in fstab, "fstab has no UUID entries"
assert (root / "etc/hostname").read_text().strip() == "fortos-e2e", "hostname mismatch"
print("E2E base assertions passed")
PY

extra_assert "${RESULT_DIR}/rootfs"

umount "${RESULT_DIR}/rootfs"
qemu-nbd --disconnect /dev/nbd0 >/dev/null 2>&1 || true
trap - EXIT

echo "FortOS installer E2E [${SCENARIO}] completed successfully."

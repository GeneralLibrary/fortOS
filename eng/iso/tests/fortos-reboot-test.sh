#!/usr/bin/env bash
set -Eeuo pipefail

# -------------------------------------------------------------------------
# fortos-reboot-test.sh — FortOS post-install reboot boot test (QEMU).
#
# The existing installer-e2e.sh only does "install + offline assertions" and never reboots from the target disk,
# so regressions like "install completes but reboot gives a VFS panic (e.g. corrupt ext4 extent / initramfs not
# effective)" slip through completely. This script adds that last step:
#   1. QEMU boots the live ISO and drives fortos-installer to complete the installation;
#   2. Shuts down the live environment and boots directly from the target system disk (BIOS/GRUB);
#   3. Asserts that "VFS: Mounted root" appears (success) or captures a panic / extent error (failure).
#
# Usage: fortos-reboot-test.sh <iso-path> <result-directory> [if=scsi|virtio]
#   if=scsi   use the LSI Logic SCSI controller (lsi53c895a, VMware default, closer to user environments)
#   if=virtio use virtio-blk (same as installer-e2e.sh, control group)
# The config disk is always virtio (device name is always /dev/vdb, avoiding a loop scan inside the guest).
# -------------------------------------------------------------------------
readonly ISO_PATH="${1:?usage: fortos-reboot-test.sh <iso> <result-dir> [if=scsi|virtio] [rootfs=ext4|btrfs]}"
readonly RESULT_DIR="${2:?result directory is required}"
readonly IF="${3:-scsi}"
readonly ROOT_FS="${4:-ext4}"
case "${IF}" in
    scsi|virtio) ;;
    *) echo "error: controller must be scsi or virtio." >&2; exit 1 ;;
esac
case "${ROOT_FS}" in
    ext4|btrfs) ;;
    *) echo "error: rootfs must be ext4 or btrfs." >&2; exit 1 ;;
esac

# System disk device name: scsi -> /dev/sda; virtio -> /dev/vda.
readonly SYSTEM_DEV="/dev/$([ "${IF}" = scsi ] && echo sda || echo vda)"
# Target root partition: with swap=off the layout is p1 BIOS boot / p2 EFI / p3 root.
readonly TARGET_ROOT="/dev/$([ "${IF}" = scsi ] && echo sda3 || echo vda3)"
# Config disk is always virtio: in the scsi scenario only the config disk is on the virtio bus -> vda;
# in the virtio scenario the system disk registers first (vda) and the config disk second (vdb).
readonly CFG_DEV="/dev/$([ "${IF}" = scsi ] && echo vda || echo vdb)"
readonly BOOT_DIR="${RESULT_DIR}/boot"
readonly CONFIG_MNT="${RESULT_DIR}/cfg"
readonly CONFIG_IMG="${RESULT_DIR}/install-config.img"
readonly SERIAL_LOG="${RESULT_DIR}/install-serial.log"
readonly REBOOT_SERIAL="${RESULT_DIR}/reboot-serial.log"
readonly INSTALL_YAML="install.yaml"
readonly SYSTEM_DISK="${RESULT_DIR}/system-disk.qcow2"

for command in xorriso qemu-img qemu-system-x86_64 expect mkfs.vfat mcopy; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "error: ${command} is required." >&2
        exit 1
    fi
done

rm -rf "${RESULT_DIR}"
mkdir -p "${BOOT_DIR}" "${CONFIG_MNT}"

# System disk QEMU arguments: scsi uses -device lsi53c895a (q35 does not support -drive if=scsi).
sys_drive_args() { # $1 = disk file
    if [[ "${IF}" = scsi ]]; then
        echo "-drive file=$1,if=none,id=sys0,format=qcow2 -device lsi53c895a,id=scsi0 -device scsi-hd,drive=sys0,bus=scsi0.0,scsi-id=0"
    else
        echo "-drive file=$1,if=virtio,format=qcow2"
    fi
}

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

# --- 2. install.yaml + vfat config disk ------------------------------------------
{
    cat <<YAML
# FortOS reboot test: controller=${IF} rootfs=${ROOT_FS}
system:
  disk: ${SYSTEM_DEV}
  rootFs: ${ROOT_FS}
  swap: off
network:
  mode: dhcp
  hostname: fortos-reboot
account:
  username: admin
  password: fortos-reboot-pass
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

# --- 3. Target virtual disk ----------------------------------------------------------
rm -f "${SYSTEM_DISK}"
qemu-img create -q -f qcow2 "${SYSTEM_DISK}" "20G"

# --- 4. QEMU + expect driven install ----------------------------------------------
SYS_DRIVE_ARGS="$(sys_drive_args "${SYSTEM_DISK}")"
cat > "${RESULT_DIR}/drive-install.exp" <<EXP
#!/usr/bin/expect -f
set timeout 1800
log_user 1
set serial_log [lindex \$argv 0]
set iso         [lindex \$argv 1]
set vmlinuz     [lindex \$argv 2]
set initrd      [lindex \$argv 3]
set cfgimg      [lindex \$argv 4]
set sys_drive    [lindex \$argv 5]
set target_root  [lindex \$argv 6]
set cfg_dev      [lindex \$argv 7]

set cmd [list qemu-system-x86_64 \
    -machine q35,accel=kvm \
    -cpu host \
    -m 3072 -smp 2]
eval lappend cmd \$sys_drive
lappend cmd \
    -drive "file=\$cfgimg,format=raw,if=virtio" \
    -cdrom "\$iso" \
    -kernel "\$vmlinuz" -initrd "\$initrd" \
    -append "boot=live components hostname=fortos console=tty0 console=ttyS0,115200n8" \
    -nic user,model=virtio-net-pci \
    -nographic -no-reboot

log_file "\$serial_log"
spawn {*}\$cmd

expect {
    -re "fortos login:" { send "user\r"; exp_continue }
    -re "Password:" { send "live\r"; exp_continue }
    -re {[#$] $} { }
    timeout { puts "TIMEOUT waiting for live shell"; exit 2 }
}

# The config disk is always virtio; the device name is passed via CFG_DEV (avoiding a Tcl conflict with the guest loop variable).
send "sudo sh -c 'mkdir -p /mnt/cfg; mount \${cfg_dev} /mnt/cfg 2>/dev/null; test -f /mnt/cfg/install.yaml && exit 0; exit 1'\r"
expect {
    -re {password for user:} { send "live\r"; exp_continue }
    -re {[#$] $} { }
    timeout { puts "TIMEOUT mounting config"; exit 2 }
}
send "sudo fortos-installer --config /mnt/cfg/install.yaml --yes\r"
expect {
    "FortOS installation completed successfully." {
        # Do not rely on guest shutdown (in practice poweroff -f is occasionally ineffective, leaving expect eof
        # blocked forever); exit directly and let the bash side clean up the leftover QEMU process.
        exit 0
    }
    -re {safe to re-run} {
        # Last line of the failure path: wait for the full error message (including "at step ...") to hit the log before exiting.
        puts "INSTALL FAILED (see serial log for step details)"
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
    "${SYS_DRIVE_ARGS}" "${TARGET_ROOT}" "${CFG_DEV}"
expect_exit=$?
set -e
# Clean up the QEMU left over from the install phase (the guest does not shut down when expect exits).
# First SIGTERM lets QEMU exit gracefully and flush qcow2 caches; SIGKILL after a timeout.
pkill -TERM -f "qemu-system-x86_64.*${SYSTEM_DISK}" 2>/dev/null || true
for _ in $(seq 1 10); do
    pgrep -f "qemu-system-x86_64.*${SYSTEM_DISK}" >/dev/null 2>&1 || break
    sleep 1
done
pkill -9 -f "qemu-system-x86_64.*${SYSTEM_DISK}" 2>/dev/null || true
sleep 1
if [[ ${expect_exit} -ne 0 ]]; then
    echo "error: install phase failed (expect exit ${expect_exit})." >&2
    echo "--- install serial tail ---" >&2
    tail -40 "${SERIAL_LOG}" >&2 || true
    exit 1
fi
echo "=== install phase OK ==="

# --- 5. Host-side injection of console=ttyS0 (mount the target disk with qemu-nbd to edit grub.cfg) ---------
# Rebooting observation needs kernel logs over the serial port; after install, the linux lines in grub.cfg
# have no console=ttyS0 by default (VGA output cannot be captured with -display none). On the host,
# edit the target system's grub.cfg directly via qemu-nbd (test-only, bypassing guest interaction).
inject_console() {
    modprobe nbd max_part=8 2>/dev/null || true
    qemu-nbd --connect=/dev/nbd0 "${SYSTEM_DISK}"
    sleep 1
    partprobe /dev/nbd0 2>/dev/null || true
    for _ in $(seq 1 20); do
        [[ -b /dev/nbd0p3 ]] && break
        sleep 1
    done
    mkdir -p "${RESULT_DIR}/inject-mnt"
    mount /dev/nbd0p3 "${RESULT_DIR}/inject-mnt"
    # Inject console=ttyS0 at the end of each kernel command line (only the normal/recovery kernel entries,
    # grub script command lines (if/set/export etc.) contain no vmlinuz and are unaffected).
    sed -i 's#^\(\s*linux[0-9a-z]*\s*/boot/vmlinuz[^ ]*\)#\1 console=ttyS0,115200n8#' \
        "${RESULT_DIR}/inject-mnt/boot/grub/grub.cfg"
    grep -n "vmlinuz" "${RESULT_DIR}/inject-mnt/boot/grub/grub.cfg" | head -5
    umount "${RESULT_DIR}/inject-mnt"
    qemu-nbd --disconnect /dev/nbd0 >/dev/null 2>&1 || true
}
inject_console

# --- 6. Reboot boot from the target disk and observe ---------------------------------------------
rm -f "${REBOOT_SERIAL}"
# shellcheck disable=SC2046
timeout 180 qemu-system-x86_64 \
    -machine q35,accel=kvm -cpu host \
    -m 3072 -smp 2 \
    $(sys_drive_args "${SYSTEM_DISK}") \
    -nic user,model=virtio-net-pci \
    -serial "file:${REBOOT_SERIAL}" \
    -display none -no-reboot -boot order=c \
    >"${RESULT_DIR}/reboot-qemu.log" 2>&1 &
qemu_pid=$!
cleanup() {
    kill "${qemu_pid}" 2>/dev/null || true
    wait "${qemu_pid}" 2>/dev/null || true
}
trap cleanup EXIT

verdict="TIMEOUT"
for i in $(seq 1 150); do
    # Success: the ext4 path prints "VFS: Mounted root"; the btrfs path prints
    # "BTRFS info (device ...): first mount" (no "VFS: Mounted root" text);
    # both eventually land on the login prompt.
    if grep -qE "VFS: Mounted root|BTRFS info \(device .*\): first mount|login:" "${REBOOT_SERIAL}" 2>/dev/null; then
        verdict="BOOT_OK"
        break
    fi
    if grep -qE "Kernel panic - not syncing|extent not found|Unable to mount root fs" "${REBOOT_SERIAL}" 2>/dev/null; then
        verdict="BOOT_FAILED"
        break
    fi
    sleep 1
done

sleep 3  # allow the tail of a panic output to be flushed to disk
if [[ "${verdict}" == "BOOT_OK" ]]; then
    echo "=== REBOOT TEST PASSED: kernel mounted the root filesystem (if=${IF}) ==="
    grep -m1 "VFS: Mounted root" "${REBOOT_SERIAL}"
else
    echo "=== REBOOT TEST ${verdict} (if=${IF}) ===" >&2
    echo "--- reboot serial log (tail) ---" >&2
    tail -60 "${REBOOT_SERIAL}" >&2 || true
    exit 2
fi

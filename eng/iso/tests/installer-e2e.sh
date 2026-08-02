#!/usr/bin/env bash
# -------------------------------------------------------------------------
# installer-e2e.sh — FortOS.Installer 场景矩阵集成测试(设计稿 §9 / M4)
#
# 在 QEMU 中启动 live ISO(内嵌 FortOS.Installer.Cli),通过串口驱动
# `fortos-installer --config install.yaml --yes` 完成全流程安装,随后离线
# 断言目标盘:分区表 / fstab / grub / install-summary.json / crypttab / mdadm。
#
# 场景矩阵(scenario 参数):
#   single-btrfs   单系统盘 btrfs(默认)
#   single-ext4    单系统盘 ext4
#   raid1          系统盘 + 2×整盘 mdadm RAID1(btrfs)
#   luks           系统盘 + 1×整盘 LUKS2(btrfs)
#
# 依赖: xorriso qemu-system-x86_64 qemu-img expect mkfs.vfat mcopy
#       qemu-nbd(离线断言;CI 容器以 root 运行)
#
# 用法: installer-e2e.sh <iso-path> <result-directory> [scenario]
# 说明: 设计在 Linux CI 容器内运行(与 test-install.sh 相同的环境)。
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

# --- 场景定义:根文件系统 + 数据盘布局 + 专属断言 --------------------------
ROOT_FS=btrfs
DATA_DISK_SIZES=()          # 数据盘大小(系统盘之后追加的 QEMU 盘)
data_yaml() { :; }          # 输出 install.yaml 的 data: 段
extra_assert() { :; }       # 场景专属断言(rootfs 挂载点路径为 $1)

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
# --name=fortos-data 时 mdadm --detail --scan 输出 homehost 风格的
# "ARRAY /dev/md/fortos-data ... name=fortos-data",不断言具体设备名。
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

# --- 1. 提取 live 内核与 initrd --------------------------------------------
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

# --- 2. 生成 install.yaml 并打包为 vfat 配置盘 -----------------------------
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

# --- 3. 目标虚拟盘:系统盘 disk-0 + 数据盘 disk-1..N ------------------------
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

# --- 4. QEMU + expect 驱动安装 ----------------------------------------------
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
    -append "boot=live components hostname=fortos console=ttyS0,115200n8 user-autologin" \
    -nic user,model=virtio-net-pci \
    -serial "file:$serial_log" \
    -nographic -no-reboot

spawn {*}$cmd

expect {
    -re "fortos login:" { send "root\r"; exp_continue }
    -re {[#$] $} { }
    timeout { puts "TIMEOUT waiting for live shell"; exit 2 }
}

# 挂载配置盘(最后附加的 virtio 盘,按内容定位;mount 需 root)。
# 注意:\$d 在 Tcl 层转义为字面 $d,由 guest 的 sh 循环变量替换。
send "sudo sh -c 'mkdir -p /mnt/cfg; for d in /dev/vd?; do mount \$d /mnt/cfg 2>/dev/null && [ -f /mnt/cfg/install.yaml ] && exit 0; umount /mnt/cfg 2>/dev/null; done; exit 1'\r"
expect {
    -re {password for user:} { send "user\r"; exp_continue }
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

# --- 5. 离线断言(qemu-nbd 挂载系统盘 p3) -----------------------------------
[[ -e /dev/nbd0 ]] || { echo "error: /dev/nbd0 unavailable (need nbd module + root)." >&2; exit 1; }
qemu-nbd --connect=/dev/nbd0 "${SYSTEM_DISK}"
trap 'umount "${RESULT_DIR}/rootfs" 2>/dev/null || true; qemu-nbd --disconnect /dev/nbd0 >/dev/null 2>&1 || true' EXIT
mkdir -p "${RESULT_DIR}/rootfs"
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

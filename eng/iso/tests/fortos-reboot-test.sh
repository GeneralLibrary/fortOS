#!/usr/bin/env bash
set -Eeuo pipefail

# -------------------------------------------------------------------------
# fortos-reboot-test.sh — FortOS 安装后重启引导测试(QEMU)。
#
# 项目既有 installer-e2e.sh 只做「安装 + 离线断言」,从不从目标盘重新引导,
# 因此「安装完成但重启即 VFS panic(如 ext4 extent 损坏 / initramfs 未生效)」
# 这类回归完全漏网。本脚本补上最后一步:
#   1. QEMU 启动 live ISO,驱动 fortos-installer 完成安装;
#   2. 关闭 live 环境,直接从目标系统盘引导(BIOS/GRUB);
#   3. 断言出现 "VFS: Mounted root"(成功)或捕获 panic / extent 错误(失败)。
#
# 用法: fortos-reboot-test.sh <iso-path> <result-directory> [if=scsi|virtio]
#   if=scsi   用 LSI Logic SCSI 控制器(lsi53c895a,VMware 默认,贴近用户环境)
#   if=virtio 用 virtio-blk(与 installer-e2e.sh 一致,对照组)
# 配置盘固定用 virtio(设备名恒为 /dev/vdb,避免 guest 内循环遍历)。
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

# 系统盘设备名:scsi → /dev/sda;virtio → /dev/vda。
readonly SYSTEM_DEV="/dev/$([ "${IF}" = scsi ] && echo sda || echo vda)"
# 目标 root 分区:swap=off 时布局为 p1 BIOS boot / p2 EFI / p3 root。
readonly TARGET_ROOT="/dev/$([ "${IF}" = scsi ] && echo sda3 || echo vda3)"
# 配置盘固定 virtio:scsi 场景下 virtio 总线只有配置盘 → vda;virtio 场景下
# 系统盘先注册(vda)、配置盘其次(vdb)。
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

# 系统盘 QEMU 参数:scsi 用 -device lsi53c895a(q35 不支持 -drive if=scsi)。
sys_drive_args() { # $1 = 盘文件
    if [[ "${IF}" = scsi ]]; then
        echo "-drive file=$1,if=none,id=sys0,format=qcow2 -device lsi53c895a,id=scsi0 -device scsi-hd,drive=sys0,bus=scsi0.0,scsi-id=0"
    else
        echo "-drive file=$1,if=virtio,format=qcow2"
    fi
}

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

# --- 2. install.yaml + vfat 配置盘 ------------------------------------------
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

# --- 3. 目标虚拟盘 ----------------------------------------------------------
rm -f "${SYSTEM_DISK}"
qemu-img create -q -f qcow2 "${SYSTEM_DISK}" "20G"

# --- 4. QEMU + expect 驱动安装 ----------------------------------------------
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

# 配置盘恒为 virtio;设备名由 CFG_DEV 传入(避免 guest 循环变量的 Tcl 冲突)。
send "sudo sh -c 'mkdir -p /mnt/cfg; mount \${cfg_dev} /mnt/cfg 2>/dev/null; test -f /mnt/cfg/install.yaml && exit 0; exit 1'\r"
expect {
    -re {password for user:} { send "live\r"; exp_continue }
    -re {[#$] $} { }
    timeout { puts "TIMEOUT mounting config"; exit 2 }
}
send "sudo fortos-installer --config /mnt/cfg/install.yaml --yes\r"
expect {
    "FortOS installation completed successfully." {
        # 不依赖 guest 关机(实测 poweroff -f 偶发不生效导致 expect eof
        # 永久阻塞);直接退出,由 bash 侧清理残留 QEMU 进程。
        exit 0
    }
    -re {safe to re-run} {
        # 失败路径最后一行:等完整错误消息(含 "at step ...")落盘后再退出。
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
# 清理安装阶段残留的 QEMU(guest 未随 expect 退出而关闭)。
# 先 SIGTERM 让 QEMU 优雅退出并 flush qcow2 缓存,超时再 SIGKILL。
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

# --- 5. Host 侧注入 console=ttyS0(qemu-nbd 挂载目标盘改 grub.cfg) ---------
# 重启观测需要内核日志经串口输出;安装后 grub.cfg 的 linux 行默认无
# console=ttyS0(QEMU -display none 下 VGA 输出不可捕获)。在 host 侧用
# qemu-nbd 直接改目标系统 grub.cfg(仅测试用途,绕开 guest 交互)。
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
    # 在每条内核命令行的行尾注入 console=ttyS0(仅普通/recovery 内核条目,
    # grub 脚本指令行(if/set/export 等)不含 vmlinuz,不受影响)。
    sed -i 's#^\(\s*linux[0-9a-z]*\s*/boot/vmlinuz[^ ]*\)#\1 console=ttyS0,115200n8#' \
        "${RESULT_DIR}/inject-mnt/boot/grub/grub.cfg"
    grep -n "vmlinuz" "${RESULT_DIR}/inject-mnt/boot/grub/grub.cfg" | head -5
    umount "${RESULT_DIR}/inject-mnt"
    qemu-nbd --disconnect /dev/nbd0 >/dev/null 2>&1 || true
}
inject_console

# --- 6. 从目标盘重启引导,观测 ---------------------------------------------
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
    # 成功:ext4 路径打印 "VFS: Mounted root";btrfs 路径打印
    # "BTRFS info (device ...): first mount"(无 VFS: Mounted root 字样);
    # 两者最终都落到 login 提示。
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

sleep 3  # 留出 panic 尾部输出落盘
if [[ "${verdict}" == "BOOT_OK" ]]; then
    echo "=== REBOOT TEST PASSED: kernel mounted the root filesystem (if=${IF}) ==="
    grep -m1 "VFS: Mounted root" "${REBOOT_SERIAL}"
else
    echo "=== REBOOT TEST ${verdict} (if=${IF}) ===" >&2
    echo "--- reboot serial log (tail) ---" >&2
    tail -60 "${REBOOT_SERIAL}" >&2 || true
    exit 2
fi

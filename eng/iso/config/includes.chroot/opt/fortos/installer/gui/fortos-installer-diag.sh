#!/bin/sh
# -------------------------------------------------------------------------
# FortOS installer GUI diagnostics — CI 冒烟测试用。
# fortos-installer-diag.service 在安装器服务启动后运行,把 Avalonia 向导
# 的进程状态写到 ttyS1。QEMU 引导测试(test-boot.sh)通过第二个 -serial
# file: 收集并断言 gui=alive;真实硬件(无 ttyS1)时静默退出。
# -------------------------------------------------------------------------
set -eu

xorg_alive() {
    pgrep -x Xorg >/dev/null 2>&1 || pgrep -x Xorg.wrap >/dev/null 2>&1
}

# 立即标记:即使后续失败,test-boot.sh 也能确认本服务已执行。
{
    echo "=== FORTOS_INSTALLER_DIAG_START ==="
} > /dev/ttyS1 2>/dev/null || true

# 等待 Xorg 和 Avalonia 安装器启动。
# TCG 软件仿真模式下 JIT 和图形初始化远慢于实机;默认轮询上限 240 s,
# 这样即便 GUI 迟迟未起,CI 也能在外层 420 s 总超时前拿到最终诊断。
# 内核命令行可通过 FORTOS_DIAG_WAIT_S=<秒数> 覆盖该值。
WAIT_LIMIT="${FORTOS_DIAG_WAIT_S:-240}"
elapsed=0
while [ "${elapsed}" -lt "${WAIT_LIMIT}" ]; do
    if xorg_alive && pgrep -f fortos-installer-gui >/dev/null 2>&1; then
        break
    fi
    sleep 5
    elapsed=$((elapsed + 5))
done

{
    echo "=== FORTOS_INSTALLER_DIAG ==="
    echo "service=$(systemctl is-active fortos-installer.service 2>/dev/null)"
    echo "xorg=$(xorg_alive && echo alive || echo dead)"
    echo "xorg_pid=$(pgrep -x Xorg -o 2>/dev/null || pgrep -x Xorg.wrap -o 2>/dev/null || echo none)"
    echo "gui=$(pgrep -f fortos-installer-gui >/dev/null 2>&1 && echo alive || echo dead)"
    echo "gui_pid=$(pgrep -f fortos-installer-gui -o 2>/dev/null || echo none)"
    echo "=== FORTOS_INSTALLER_DIAG_END ==="
} > /dev/ttyS1 2>/dev/null || true

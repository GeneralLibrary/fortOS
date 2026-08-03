#!/bin/sh
# -------------------------------------------------------------------------
# FortOS installer GUI diagnostics — CI 冒烟测试用。
# fortos-installer-diag.service 在安装器服务启动后运行,把 Avalonia 向导
# 的进程状态写到 ttyS1。QEMU 引导测试(test-boot.sh)通过第二个 -serial
# file: 收集并断言 gui=alive;真实硬件(无 ttyS1)时静默退出。
# -------------------------------------------------------------------------
set -eu

# 立即标记:即使后续失败,test-boot.sh 也能确认本服务已执行。
{
    echo "=== FORTOS_INSTALLER_DIAG_START ==="
} > /dev/ttyS1 2>/dev/null || true

# 等待 Xorg 和 Avalonia 安装器启动。
# TCG 软件仿真模式下 JIT 和图形初始化远慢于实机;默认轮询上限 300 s。
# 内核命令行可通过 FORTOS_DIAG_WAIT_S=<秒数> 覆盖该值。
WAIT_LIMIT="${FORTOS_DIAG_WAIT_S:-300}"
elapsed=0
while [ "${elapsed}" -lt "${WAIT_LIMIT}" ]; do
    if pgrep -x Xorg >/dev/null 2>&1 && pgrep -f fortos-installer-gui >/dev/null 2>&1; then
        break
    fi
    sleep 5
    elapsed=$((elapsed + 5))
done

{
    echo "=== FORTOS_INSTALLER_DIAG ==="
    echo "service=$(systemctl is-active fortos-installer.service 2>/dev/null)"
    echo "xorg=$(pgrep -x Xorg >/dev/null 2>&1 && echo alive || echo dead)"
    echo "gui=$(pgrep -f fortos-installer-gui >/dev/null 2>&1 && echo alive || echo dead)"
    echo "=== FORTOS_INSTALLER_DIAG_END ==="
} > /dev/ttyS1 2>/dev/null || true

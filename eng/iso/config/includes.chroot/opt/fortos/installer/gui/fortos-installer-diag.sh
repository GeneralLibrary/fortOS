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

# 给 Xorg + Avalonia 留出启动时间(服务已启动,此处仅等渲染就绪)。
sleep 20

{
    echo "=== FORTOS_INSTALLER_DIAG ==="
    echo "service=$(systemctl is-active fortos-installer.service 2>/dev/null)"
    echo "xorg=$(pgrep -x Xorg >/dev/null 2>&1 && echo alive || echo dead)"
    echo "gui=$(pgrep -f fortos-installer-gui >/dev/null 2>&1 && echo alive || echo dead)"
    echo "=== FORTOS_INSTALLER_DIAG_END ==="
} > /dev/ttyS1 2>/dev/null || true

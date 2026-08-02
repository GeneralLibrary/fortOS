#!/bin/sh
# -------------------------------------------------------------------------
# FortOS installer kiosk launcher (design: docs/installer-design.md §3.2).
# 在 tty7 拉起 Xorg,然后执行 X 会话脚本(openbox + Avalonia 安装器)。
# 由 fortos-installer.service 以 root 调用。
#
# -allow-root:live 环境以 root 运行整个 kiosk 链(xorg-server ≥1.21 默认
#   拒绝 root,这是安装器场景的常规做法);-nolisten tcp 阻止远程 X 连接。
# -------------------------------------------------------------------------
set -eu

exec xinit /opt/fortos/installer/gui/installer-session.sh \
    -- :0 vt7 -allow-root -keeptty -nolisten tcp

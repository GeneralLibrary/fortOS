#!/bin/sh
# -------------------------------------------------------------------------
# FortOS installer kiosk launcher (design: docs/installer-design.md §3.2).
# 在 tty7 拉起 Xorg,然后执行 X 会话脚本(openbox + Avalonia 安装器)。
# 由 fortos-installer.service 以 root 调用。
#
# -nolisten tcp 阻止远程 X 连接。显示号用 :1:live 环境默认会启动一个
# 用户 X 会话占用 :0(如 Debian live 的 xinit :0 vt1),若 kiosk 也用 :0
# 会因显示号冲突导致 Xorg 启动失败。Xorg ≥1.21(Debian bookworm)不识别
# -allow-root 选项(会打印 usage 后退出),且 Debian 构建未启用 root 检查,
# root 运行无需该选项。
# -------------------------------------------------------------------------
set -eu

exec xinit /opt/fortos/installer/gui/installer-session.sh \
    -- :1 vt7 -keeptty -nolisten tcp

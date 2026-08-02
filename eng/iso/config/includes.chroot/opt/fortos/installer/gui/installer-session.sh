#!/bin/sh
# -------------------------------------------------------------------------
# FortOS installer X session:openbox 作为窗口管理器,安装器在前台全屏运行。
# xinit 以本脚本为 X 客户端(须可执行)。
# -------------------------------------------------------------------------
set -eu

# 键盘布局默认 us;安装向导内可再改(写入目标系统时生效)。
setxkbmap us 2>/dev/null || true

openbox &

# Avalonia 安装器(主进程;退出即会话结束)。
exec /opt/fortos/installer/gui/fortos-installer-gui

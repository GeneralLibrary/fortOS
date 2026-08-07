#!/bin/sh
# -------------------------------------------------------------------------
# FortOS installer kiosk launcher (design: docs/installer-design.md §3.2).
# Starts Xorg on tty7, then runs the X session script (openbox + the Avalonia installer).
# Invoked as root by fortos-installer.service.
#
# -nolisten tcp blocks remote X connections. Display number :1 is used: the live environment starts
# a user X session on :0 by default (e.g. Debian live's xinit :0 vt1); if the kiosk also used :0,
# Xorg would fail to start because of the display number conflict. Xorg >= 1.21 (Debian bookworm) does not
# recognize the -allow-root option (it prints usage and exits), and the Debian build does not enable the root
# check, so running as root does not need that option.
# -------------------------------------------------------------------------
set -eu

exec xinit /opt/fortos/installer/gui/installer-session.sh \
    -- :1 vt7 -keeptty -nolisten tcp

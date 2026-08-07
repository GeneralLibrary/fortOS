#!/bin/sh
# -------------------------------------------------------------------------
# FortOS installer X session: openbox as the window manager, with the installer running fullscreen in the foreground.
# xinit uses this script as its X client (must be executable).
# -------------------------------------------------------------------------
set -eu

# Keyboard layout defaults to us; it can be changed inside the install wizard (takes effect when written to the target system).
setxkbmap us 2>/dev/null || true

openbox &

# The Avalonia installer (main process; when it exits, the session ends).
exec /opt/fortos/installer/gui/fortos-installer-gui

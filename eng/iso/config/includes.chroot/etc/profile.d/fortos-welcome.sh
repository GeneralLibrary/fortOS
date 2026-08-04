#!/bin/sh
# FortOS login banner: prints the welcome panel and quick-start tips after an
# interactive console/SSH login. Quietly skipped for non-interactive sessions
# and when the fortos CLI is not installed yet.
if [ -t 0 ] && command -v fortos >/dev/null 2>&1; then
    fortos welcome 2>/dev/null || true
fi

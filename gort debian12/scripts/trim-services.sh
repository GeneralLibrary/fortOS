#!/usr/bin/env bash
# =============================================================================
# GORT Debian 12 — Service Trimming Script
# =============================================================================
# Runs INSIDE the chroot during debootstrap.
# Disables/masks ALL services except those GORT requires.
# =============================================================================
set -euo pipefail

echo "=== GORT Service Trimming ==="

# -----------------------------------------------------------------------------
# 1. SERVICES TO KEEP ENABLED (whitelist)
# -----------------------------------------------------------------------------
# Format: exact systemd unit name
# These are the ONLY services allowed to start at boot.

KEEP_ENABLED=(
    # --- GORT Core ---
    "gort.service"

    # --- Docker / Container Runtime ---
    "docker.service"
    "containerd.service"

    # --- File Sharing (Samba) ---
    "smbd.service"
    "nmbd.service"

    # --- File Sharing (NFS) ---
    "nfs-server.service"
    "nfs-mountd.service"
    "nfs-idmapd.service"
    "rpcbind.service"
    "rpc-statd.service"
    "rpcbind.socket"

    # --- File Sharing (FTP) ---
    "vsftpd.service"

    # --- Core System (essential) ---
    "systemd-journald.service"
    "systemd-journald.socket"
    "systemd-udevd.service"
    "systemd-udevd-kernel.socket"
    "systemd-udevd-control.socket"
    "systemd-resolved.service"
    "systemd-networkd.service"
    "systemd-timesyncd.service"
    "systemd-tmpfiles-setup.service"
    "systemd-tmpfiles-setup-dev.service"
    "systemd-tmpfiles-clean.service"
    "systemd-logind.service"
    "systemd-user-sessions.service"
    "systemd-update-utmp.service"
    "systemd-random-seed.service"
    "systemd-sysctl.service"
    "systemd-modules-load.service"
    "systemd-remount-fs.service"
    "systemd-fsck-root.service"
    "systemd-journal-flush.service"
    "systemd-binfmt.service"
    "keyboard-setup.service"
    "kmod-static-nodes.service"
    "console-setup.service"

    # --- SSH ---
    "ssh.service"
    "sshd.service"

    # --- Networking ---
    "networking.service"
    "NetworkManager.service"
    "NetworkManager-wait-online.service"
    "NetworkManager-dispatcher.service"
    "wpa_supplicant.service"

    # --- UPS ---
    "nut-monitor.service"
    "nut-client.service"

    # --- System Utilities ---
    "cron.service"
    "logrotate.service"
    "rsyslog.service"

    # --- D-Bus (required by NetworkManager, systemd) ---
    "dbus.service"
    "dbus.socket"
)

# -----------------------------------------------------------------------------
# 2. AGGRESSIVE MASKING — Disable everything else
# -----------------------------------------------------------------------------
echo ""
echo "Scanning for services to disable..."

# Get ALL installed systemd service units
ALL_SERVICES=$(find /lib/systemd/system /etc/systemd/system -name "*.service" -type f 2>/dev/null | \
    xargs -r -n1 basename | sort -u)

DISABLED_COUNT=0
MASKED_COUNT=0

for service in $ALL_SERVICES; do
    # Skip if in the keep list
    keep=false
    for keep_svc in "${KEEP_ENABLED[@]}"; do
        if [ "$service" = "$keep_svc" ]; then
            keep=true
            break
        fi
    done

    if $keep; then
        # Ensure the service is enabled
        systemctl enable "$service" 2>/dev/null || true
        echo "  [KEEP]   $service"
        continue
    fi

    # Disable and mask the service
    systemctl disable "$service" 2>/dev/null || true
    systemctl mask "$service" 2>/dev/null || true
    DISABLED_COUNT=$((DISABLED_COUNT + 1))
done

echo ""
echo "Services disabled/masked: $DISABLED_COUNT"
echo "Services kept enabled:   ${#KEEP_ENABLED[@]}"

# -----------------------------------------------------------------------------
# 3. DISABLE NON-ESSENTIAL TIMERS
# -----------------------------------------------------------------------------
echo ""
echo "Disabling non-essential timers..."

# Keep only essential timers
KEEP_TIMERS=(
    "systemd-tmpfiles-clean.timer"
    "logrotate.timer"
    "apt-daily.timer"
    "apt-daily-upgrade.timer"
    "fstrim.timer"
)

ALL_TIMERS=$(find /lib/systemd/system /etc/systemd/system -name "*.timer" -type f 2>/dev/null | \
    xargs -r -n1 basename | sort -u)

for timer in $ALL_TIMERS; do
    keep=false
    for keep_timer in "${KEEP_TIMERS[@]}"; do
        if [ "$timer" = "$keep_timer" ]; then
            keep=true
            break
        fi
    done

    if ! $keep; then
        systemctl disable "$timer" 2>/dev/null || true
        systemctl mask "$timer" 2>/dev/null || true
    fi
done

# -----------------------------------------------------------------------------
# 4. REMOVE UNNECESSARY SOCKETS
# -----------------------------------------------------------------------------
echo ""
echo "Cleaning up unnecessary sockets..."

# Mask socket units for services we don't need
MASK_SOCKETS=(
    "cups.socket"
    "avahi-daemon.socket"
    "pcscd.socket"
    "rpcbind.socket"  # Keep — NFS needs it
)

# Actually rpcbind.socket is needed, remove it from this list
UNNEEDED_SOCKETS=(
    "cups.socket"
    "avahi-daemon.socket"
    "pcscd.socket"
)

for sock in "${UNNEEDED_SOCKETS[@]}"; do
    if [ -f "/lib/systemd/system/$sock" ]; then
        systemctl mask "$sock" 2>/dev/null || true
        echo "  [MASK]   $sock"
    fi
done

# -----------------------------------------------------------------------------
# 5. CLEAN UP DEFAULT TARGET — Ensure multi-user.target is lean
# -----------------------------------------------------------------------------
echo ""
echo "Cleaning up multi-user.target..."

# Remove graphical.target symlink to prevent GUI attempts
rm -f /etc/systemd/system/default.target
ln -sf /lib/systemd/system/multi-user.target /etc/systemd/system/default.target

# Ensure getty is on tty1 only (not tty2-tty6 to save resources)
for tty in tty2 tty3 tty4 tty5 tty6; do
    systemctl mask "getty@${tty}.service" 2>/dev/null || true
done

# -----------------------------------------------------------------------------
# 6. CREATE GORT BOOT MARKER
# -----------------------------------------------------------------------------
cat > /etc/gort/trimmed-release << 'EOF'
GORT Debian 12 — Trimmed Build
===============================
This Debian 12 rootfs has been trimmed for GORT.
Only services required by GORT are enabled.
Build date: BUILD_DATE_PLACEHOLDER
See /etc/gort/enabled-services.conf for the keep list.
EOF

# -----------------------------------------------------------------------------
# 7. SUMMARY
# -----------------------------------------------------------------------------
echo ""
echo "=== Trimming Summary ==="
echo "Services disabled: $DISABLED_COUNT"
echo "Services kept:     ${#KEEP_ENABLED[@]}"
echo ""
echo "Enabled units in multi-user.target:"
ls /etc/systemd/system/multi-user.target.wants/ 2>/dev/null | sed 's/^/  /' || echo "  (none)"
echo ""
echo "Trimming complete."

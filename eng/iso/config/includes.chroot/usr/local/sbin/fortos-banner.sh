#!/bin/sh
# FortOS boot banner: print the web management access address to the console so users can discover the management entry point.
# Skip silently when there is no network address (offline / no interface ready); ignore output errors when there is no console.
set -u

ip="$(hostname -I 2>/dev/null | awk '{print $1}')"
if [ -z "${ip}" ]; then
    ip="$(ip -4 addr show scope global 2>/dev/null | awk '/inet /{print $2}' | cut -d/ -f1 | head -n1)"
fi
[ -n "${ip}" ] || exit 0

# Port is parsed from ASPNETCORE_URLS in fortos.env, defaulting to 5000.
port="$(sed -n 's/^ASPNETCORE_URLS=[^:]*:\([0-9][0-9]*\).*/\1/p' /etc/fortos/fortos.env 2>/dev/null)"
[ -n "${port}" ] || port="5000"

# All non-virtual NIC IPs (hostname -I already excludes lo); list each one on its own line for multi-NIC setups.
ips="$(hostname -I 2>/dev/null | awk '{for (i=1;i<=NF;i++) print $i}')"
[ -n "${ips}" ] || ips="${ip}"

# Write directly to the kernel console (tty1/serial); systemd's StandardOutput=console fails to resolve
# on some builds, sending output to the journal instead of the screen, so redirect inside the script.
console=/dev/console
{
    echo ""
    echo "============================================================"
    echo "  FortOS Management Console"
    echo "${ips}" | while read -r a; do
        [ -n "${a}" ] && echo "  http://${a}:${port}/dashboard/"
    done
    echo "============================================================"
    echo ""
} > "${console}" 2>/dev/null || true

exit 0

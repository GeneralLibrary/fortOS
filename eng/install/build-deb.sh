#!/usr/bin/env bash
# ============================================================================
# FortOS — build a .deb package (P1-4 one-click install)
# ----------------------------------------------------------------------------
# Usage:
#   bash eng/install/build-deb.sh <api-publish-dir> [version]
#
# Produces:  dist/fortos_<version>_amd64.deb
#
# The package installs to /opt/fortos, ships the fortos.env template and the
# systemd unit, and triggers `systemctl enable --now fortos` on install.
# Companion one-click script: eng/install/install.sh (no package needed).
# ============================================================================
set -euo pipefail

API_DIR="${1:-}"
VERSION="${2:-1.0.0}"
[[ -n "$API_DIR" ]] || { echo "用法: $0 <api-publish-dir> [version]" >&2; exit 1; }
[[ -d "$API_DIR" ]] || { echo "发布目录不存在: $API_DIR" >&2; exit 1; }

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIST="$ROOT/dist"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

mkdir -p "$STAGE/DEBIAN" "$STAGE/opt/fortos/api" "$STAGE/etc/fortos" "$STAGE/etc/systemd/system" "$STAGE/usr/lib/systemd/system-preset"

# Binaries
cp -r "$API_DIR/." "$STAGE/opt/fortos/api/"

# Env template
cat > "$STAGE/etc/fortos/fortos.env" <<'EOF'
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000
FortOS_DATA_ROOT=/srv/nas
FortOS_CONFIG_PATH=/srv/nas/config/nas.yaml
EOF

# systemd unit + preset (start on install)
cat > "$STAGE/etc/systemd/system/fortos.service" <<'EOF'
[Unit]
Description=FortOS management service
Documentation=https://github.com/GeneralLibrary/fortos
After=network-online.target docker.service
Wants=network-online.target

[Service]
Type=simple
EnvironmentFile=/etc/fortos/fortos.env
WorkingDirectory=/opt/fortos/api
ExecStart=/opt/fortos/api/FortOS.Api
Restart=on-failure
RestartSec=5s
TimeoutStopSec=30s
UMask=0027

[Install]
WantedBy=multi-user.target
EOF
echo "fortos.service enable" > "$STAGE/usr/lib/systemd/system-preset/90-fortos.preset"

# Control file
cat > "$STAGE/DEBIAN/control" <<EOF
Package: fortos
Version: $VERSION
Section: admin
Priority: optional
Architecture: amd64
Depends: dotnet-runtime-8.0 | dotnet-runtime-9.0 | dotnet-runtime-10.0, docker.io | docker-ce, smbclient, nfs-common
Maintainer: FortOS Team <dev@fortos.example>
Description: FortOS — security-first Linux NAS management service
 Deploys the FortOS API (REST/gRPC) with container agent orchestration,
 file sharing, backup, AI assistant and Tailscale remote access.
EOF

cat > "$STAGE/DEBIAN/postinst" <<'EOF'
#!/usr/bin/env bash
set -e
systemctl daemon-reload
systemctl enable fortos.service 2>/dev/null || true
mkdir -p /srv/nas/config
systemctl restart fortos.service 2>/dev/null || true
echo "FortOS 已安装。管理地址: http://<本机IP>:5000"
EOF
chmod 755 "$STAGE/DEBIAN/postinst"

mkdir -p "$DIST"
dpkg-deb --build --root-owner-group "$STAGE" "$DIST/fortos_${VERSION}_amd64.deb"
echo "已生成: $DIST/fortos_${VERSION}_amd64.deb"

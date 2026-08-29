#!/usr/bin/env bash
# ============================================================================
# FortOS — Debian/Ubuntu one-click install (P1-4)
# ----------------------------------------------------------------------------
# Usage:
#   bash eng/install/install.sh
#
# What it does:
#   1. Detects the distro (Debian/Ubuntu) and installs runtime deps
#      (dotnet runtime, docker, smb/nfs client tools as needed).
#   2. Downloads the FortOS API publish output (local publish dir, a release
#      tarball URL, or an already-staged /opt/fortos) and installs it to
#      /opt/fortos.
#   3. Registers fortos.service (systemd) and starts it.
#   4. Prints the management URL and first-run notes.
#
# Requires: bash, curl (or wget), systemd. Run as root.
# ============================================================================
set -euo pipefail

# ---- Config ---------------------------------------------------------------
FORTOS_DEST="${FORTOS_DEST:-/opt/fortos}"
FORTOS_DATA_ROOT="${FortOS_DATA_ROOT:-/srv/nas}"
FORTOS_ENV_FILE="${FORTOS_ENV_FILE:-/etc/fortos/fortos.env}"
# Source of the API binaries: one of
#   local:<path>   — use a local publish output (e.g. local:./artifacts/fortos-api)
#   url:<tar.gz>   — download a published tarball
#   (unset)        — if /opt/fortos/api already exists, keep it; else error with guidance
FORTOS_SOURCE="${FORTOS_SOURCE:-}"

log()  { printf '\033[1;36m[fortos]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[fortos]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[fortos]\033[0m %s\n' "$*" >&2; exit 1; }

# ---- Prereqs --------------------------------------------------------------
[[ $EUID -eq 0 ]] || die "请以 root 运行:sudo bash eng/install/install.sh"
command -v systemctl >/dev/null 2>&1 || die "需要 systemd 系统。"

if ! command -v curl >/dev/null 2>&1 && ! command -v wget >/dev/null 2>&1; then
  die "需要 curl 或 wget。"
fi

# ---- Distro detection -----------------------------------------------------
if ! command -v apt-get >/dev/null 2>&1; then
  die "本脚本仅支持 Debian/Ubuntu(未检测到 apt-get)。"
fi
. /etc/os-release 2>/dev/null || true
log "检测到系统: ${PRETTY_NAME:-Debian/Ubuntu}"

# ---- Runtime dependencies -------------------------------------------------
log "安装运行时依赖(dotnet-runtime / docker / smb 客户端)…"
export DEBIAN_FRONTEND=noninteractive
apt-get update -y

install_pkg() {
  if ! dpkg -s "$1" >/dev/null 2>&1; then
    apt-get install -y "$1"
  fi
}

install_pkg ca-certificates
install_pkg curl

# Docker: use the distro package when present, else docker.io (best-effort).
if ! command -v docker >/dev/null 2>&1; then
  install_pkg docker.io || warn "Docker 安装失败,容器(Agent)功能将不可用;可稍后手动安装。"
fi
# SMB/NFS client tools for share access (optional but recommended).
install_pkg smbclient 2>/dev/null || true
install_pkg nfs-common 2>/dev/null || true

# dotnet runtime: needed to run FortOS.Api. Prefer the runtime from the
# Microsoft feed; fall back to a distro package if unavailable.
if ! command -v dotnet >/dev/null 2>&1; then
  log "安装 .NET Runtime…"
  install_pkg dotnet-runtime-8.0 2>/dev/null \
    || curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --runtime aspnetcore --install-dir /usr/share/dotnet \
    || warn ".NET Runtime 安装失败,请手动安装后重试。"
  export PATH="$PATH:/usr/share/dotnet"
fi

# ---- Data root ------------------------------------------------------------
mkdir -p "$FORTOS_DATA_ROOT"
log "数据目录: $FORTOS_DATA_ROOT"

# ---- Install binaries -----------------------------------------------------
install_from_local() {
  local src="${1#local:}"
  [[ -d "$src" ]] || die "本地发布目录不存在: $src"
  log "复制本地发布产物: $src → $FORTOS_DEST"
  mkdir -p "$FORTOS_DEST"
  cp -r "$src/." "$FORTOS_DEST/"
}

install_from_url() {
  local url="${1#url:}"
  local tmp
  tmp="$(mktemp -d)"
  log "下载发布包: $url"
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$url" -o "$tmp/fortos.tar.gz"
  else
    wget -qO "$tmp/fortos.tar.gz" "$url"
  fi
  mkdir -p "$FORTOS_DEST"
  tar -xzf "$tmp/fortos.tar.gz" -C "$FORTOS_DEST"
  rm -rf "$tmp"
}

case "$FORTOS_SOURCE" in
  local:*) install_from_local "$FORTOS_SOURCE" ;;
  url:*)   install_from_url "$FORTOS_SOURCE" ;;
  "")
    if [[ ! -x "$FORTOS_DEST/api/FortOS.Api" ]]; then
      die "未检测到 $FORTOS_DEST/api/FortOS.Api。请设置 FORTOS_SOURCE=local:<path> 或 FORTOS_SOURCE=url:<tar.gz> 提供发布产物。"
    fi
    log "复用已有安装: $FORTOS_DEST"
    ;;
  *) die "未知的 FORTOS_SOURCE 格式: $FORTOS_SOURCE" ;;
esac

[[ -x "$FORTOS_DEST/api/FortOS.Api" ]] || die "发布产物缺少可执行文件 $FORTOS_DEST/api/FortOS.Api"

# ---- Config file ----------------------------------------------------------
mkdir -p "$(dirname "$FORTOS_ENV_FILE")"
if [[ ! -f "$FORTOS_ENV_FILE" ]]; then
  cat > "$FORTOS_ENV_FILE" <<EOF
# FortOS environment(编辑后 systemctl restart fortos)
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000
FortOS_DATA_ROOT=$FORTOS_DATA_ROOT
FortOS_CONFIG_PATH=$FORTOS_DATA_ROOT/config/nas.yaml
EOF
  log "已生成环境文件: $FORTOS_ENV_FILE"
fi

# ---- systemd service ------------------------------------------------------
SERVICE_FILE="/etc/systemd/system/fortos.service"
if [[ ! -f "$SERVICE_FILE" ]]; then
  cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=FortOS management service
Documentation=https://github.com/GeneralLibrary/fortos
After=network-online.target docker.service
Wants=network-online.target

[Service]
Type=simple
EnvironmentFile=$FORTOS_ENV_FILE
WorkingDirectory=$FORTOS_DEST/api
ExecStart=$FORTOS_DEST/api/FortOS.Api
Restart=on-failure
RestartSec=5s
TimeoutStopSec=30s
UMask=0027

[Install]
WantedBy=multi-user.target
EOF
  systemctl daemon-reload
  systemctl enable fortos.service
  log "已注册并启用 fortos.service"
else
  log "fortos.service 已存在,跳过注册。"
fi

systemctl restart fortos.service
log "已启动 fortos.service"

# ---- Banner ---------------------------------------------------------------
sleep 2
IP=$(hostname -I 2>/dev/null | awk '{print $1}')
log "安装完成。"
log "  管理地址: http://${IP:-<本机IP>}:5000"
log "  首次使用: 打开上述地址,注册首个管理员账号。"
log "  容器/影音/AI: 在管理界面的「容器」页部署模板(先确保 Docker 可用)。"
log "  远程访问: 在「设置 → 系统配置」开启 remote:enabled(Tailscale)即可免公网 IP 访问。"

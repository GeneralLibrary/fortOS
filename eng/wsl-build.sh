#!/usr/bin/env bash
set -euo pipefail

export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

SRC_WIN="/mnt/c/github code/Juster/fortOS"
DST="$HOME/fortos"

echo "=== copying source via tar pipe to $DST ==="
mkdir -p "$DST"
cd "$SRC_WIN"
tar -cf - \
  --exclude='bin' --exclude='obj' --exclude='node_modules' \
  --exclude='.git' \
  --exclude='.reasonix' --exclude='mnt' \
  src protos eng Directory.Build.props Dockerfile docker-compose*.yml FortOS.slnx \
  | (cd "$DST" && tar -xf -)
echo "=== copied. src dir: ==="
ls "$DST/src" | head

echo "=== restore + build FortOS.Api ==="
cd "$DST/src/FortOS.Api"
dotnet build -c Debug 2>&1 | tail -25
echo "BUILD_DONE"

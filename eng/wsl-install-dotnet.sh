#!/usr/bin/env bash
set -euo pipefail

# Clean PATH to avoid Windows paths with parentheses breaking bash.
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

echo "=== install libicu76 ==="
apt-get install -y --no-install-recommends libicu76 2>&1 | tail -6

echo "=== verify dotnet ==="
dotnet --version
echo "=== SDKs ==="
dotnet --list-sdks
echo "DONE"

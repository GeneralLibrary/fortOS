#!/usr/bin/env bash
set -euo pipefail
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
TOKEN="$1"; SID="$2"
BASE="http://localhost:5173"
FILE=/tmp/bigtest.bin; TOTAL=6291456
echo "=== chunk 1 via proxy ==="
dd if="$FILE" bs=4M count=1 2>/dev/null | \
  curl -s -X PUT "$BASE/api/files/uploads/$SID" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Range: bytes 0-4194303/$TOTAL" \
    -H "Content-Type: application/octet-stream" \
    --data-binary @- --max-time 30
echo ""
echo "=== chunk 2 via proxy ==="
dd if="$FILE" bs=2M skip=2 count=1 2>/dev/null | \
  curl -s -X PUT "$BASE/api/files/uploads/$SID" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Range: bytes 4194304-6291455/$TOTAL" \
    -H "Content-Type: application/octet-stream" \
    --data-binary @- --max-time 30
echo ""
echo "=== finalize via proxy ==="
curl -s -X POST "$BASE/api/files/uploads/$SID/finalize" -H "Authorization: Bearer $TOKEN" --max-time 15
echo ""
echo "=== sha256 verify ==="
sha256sum /srv/nas/proxy-test.bin | cut -d' ' -f1
echo "expected: ab86755a0cbbdb9d800fd7278b08de756478143a64d09f7898001c96a9242f07"

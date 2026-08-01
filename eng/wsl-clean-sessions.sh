#!/usr/bin/env bash
set -euo pipefail
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
DB=/srv/nas/database/nas.db

echo "=== open sessions before ==="
sqlite3 "$DB" "SELECT session_id, target_path, received_bytes, state FROM upload_sessions WHERE state='open';"

echo "=== aborting all open sessions ==="
sqlite3 "$DB" "UPDATE upload_sessions SET state='aborted' WHERE state='open';"
echo "affected rows: $(sqlite3 "$DB" "SELECT changes();")"

echo "=== open sessions after ==="
sqlite3 "$DB" "SELECT COUNT(*) FROM upload_sessions WHERE state='open';"

echo "=== cleaning leftover .upload temp files ==="
find /srv/nas -name '*.upload' -type f 2>/dev/null | while read -r f; do
  rm -f "$f"
  echo "removed $f"
done
echo "DONE"

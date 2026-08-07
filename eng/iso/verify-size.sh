#!/usr/bin/env bash
# -------------------------------------------------------------------------
# verify-size.sh — FortOS ISO size guardrail (design doc §8.5)
#
# Acceptance baseline: the image delta of the graphical installer stack must be <= 300 MiB. The baseline comes from the
# "no graphics stack" FortOS ISO size. Without a baseline this script only prints the size as a reference (CI passes
# size_baseline explicitly via a workflow input with the previous build's size to enforce the delta budget).
#
# Usage:
#   verify-size.sh <iso-path>                    # print the size (as a reference baseline)
#   verify-size.sh <iso-path> <baseline-bytes>   # assert the delta is <= 300 MiB
#
# Exit codes: 0 pass; 1 missing / over budget.
# -------------------------------------------------------------------------
set -Eeuo pipefail

readonly ISO_PATH="${1:?usage: verify-size.sh <iso-path> [baseline-bytes]}"
readonly BASELINE="${2:-}"
readonly BUDGET_MIB=300

if [[ ! -f "${ISO_PATH}" ]]; then
    echo "error: ${ISO_PATH} not found." >&2
    exit 1
fi

size="$(stat --format='%s' "${ISO_PATH}")"
size_mib=$((size / 1048576))
echo "ISO size: ${size_mib} MiB (${size} bytes) — ${ISO_PATH}"

if [[ -n "${BASELINE}" ]]; then
    delta=$((size - BASELINE))
    delta_mib=$((delta / 1048576))
    echo "Delta vs baseline ${BASELINE} bytes: ${delta_mib} MiB (budget ${BUDGET_MIB} MiB)"
    if (( delta > BUDGET_MIB * 1048576 )); then
        echo "::error::ISO size delta ${delta_mib} MiB exceeds the ${BUDGET_MIB} MiB graphics-stack budget." >&2
        exit 1
    fi
else
    echo "No baseline given — recording this build as the reference. Pass <baseline-bytes> to enforce the ${BUDGET_MIB} MiB delta budget."
fi

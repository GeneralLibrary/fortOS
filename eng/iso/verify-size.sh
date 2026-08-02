#!/usr/bin/env bash
# -------------------------------------------------------------------------
# verify-size.sh — FortOS ISO 体积护栏(设计稿 §8.5)
#
# 验收基准:图形安装器栈的镜像增量 ≤ 300 MiB。基线取自「无图形栈」的
# FortOS ISO 大小。无基线时本脚本打印大小作为参考(CI 通过 workflow 输入
# size_baseline 显式传入上一次构建的大小以强制执行增量预算)。
#
# 用法:
#   verify-size.sh <iso-path>                    # 打印大小(作为参考基线)
#   verify-size.sh <iso-path> <baseline-bytes>   # 断言增量 ≤ 300 MiB
#
# 退出码:0 通过;1 缺失/超限。
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

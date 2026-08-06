#!/bin/sh
# FortOS 开机横幅:把 Web 管理访问地址打印到控制台,方便用户发现管理入口。
# 无网络地址(未联网/接口未就绪)时静默跳过;无 console 环境时忽略输出错误。
set -u

ip="$(hostname -I 2>/dev/null | awk '{print $1}')"
if [ -z "${ip}" ]; then
    ip="$(ip -4 addr show scope global 2>/dev/null | awk '/inet /{print $2}' | cut -d/ -f1 | head -n1)"
fi
[ -n "${ip}" ] || exit 0

# 端口从 fortos.env 的 ASPNETCORE_URLS 解析,缺省 5000。
port="$(sed -n 's/^ASPNETCORE_URLS=[^:]*:\([0-9][0-9]*\).*/\1/p' /etc/fortos/fortos.env 2>/dev/null)"
[ -n "${port}" ] || port="5000"

# 全部非虚拟网卡 IP(hostname -I 已排除 lo);多网卡时逐行列出。
ips="$(hostname -I 2>/dev/null | awk '{for (i=1;i<=NF;i++) print $i}')"
[ -n "${ips}" ] || ips="${ip}"

# 直接写内核 console(tty1/串口);systemd 的 StandardOutput=console 在部分
# 构建下解析失败,输出会落进 journal 而不到屏幕,故在脚本内重定向。
console=/dev/console
{
    echo ""
    echo "============================================================"
    echo "  FortOS 管理界面 / Management Console"
    echo "${ips}" | while read -r a; do
        [ -n "${a}" ] && echo "  http://${a}:${port}/dashboard/"
    done
    echo "============================================================"
    echo ""
} > "${console}" 2>/dev/null || true

exit 0

# GORT Debian 12 — 快速开始

## 最小系统描述

裁剪后的 Debian 12 系统特性：

| 属性 | 值 |
|------|-----|
| 基础系统 | Debian 12 (Bookworm) |
| 架构 | amd64 / arm64 |
| 安装包数量 | ~250 (vs 标准最小安装 ~800) |
| 磁盘占用 | ~1.2 GB (vs 标准最小安装 ~3.5 GB) |
| 默认启动服务 | ~15 个 |
| 内核 | linux-image-amd64 (包含 GORT 所需全部模块) |
| init 系统 | systemd |
| 默认 target | multi-user.target (无 GUI) |
| TTY 终端 | 仅 tty1 |

---

## 默认启动服务一览

```
multi-user.target.wants/
├── gort.service              # GORT 管理 API (端口 5000)
├── docker.service            # Docker 守护进程
├── containerd.service        # containerd 运行时
├── smbd.service              # Samba SMB 服务 (端口 445, 139)
├── nmbd.service              # NetBIOS 名称服务
├── nfs-server.service        # NFS 内核服务器 (端口 2049)
├── nfs-mountd.service        # NFS mount 守护进程
├── rpcbind.service           # RPC 端口映射 (端口 111)
├── vsftpd.service            # FTP 服务 (端口 21)
├── ssh.service               # SSH 远程管理 (端口 22)
├── NetworkManager.service    # 网络管理
├── networking.service        # 网络基础
├── nut-monitor.service       # UPS 监控
├── cron.service              # 定时任务
├── logrotate.service         # 日志轮转
├── rsyslog.service           # 系统日志
├── dbus.service              # D-Bus 系统消息总线
├── systemd-journald.service  # 日志
├── systemd-udevd.service     # 设备管理
├── systemd-resolved.service  # DNS 解析
├── systemd-timesyncd.service # 时间同步
├── systemd-logind.service    # 用户会话管理
└── getty@tty1.service        # 控制台登录
```

---

## 构建命令

```bash
# 进入裁剪目录
cd "gort debian12"

# 一键构建（需要 Docker）
bash scripts/bootstrap-debian12.sh all

# 验证裁剪结果
bash scripts/verify-trim.sh

# 查看 rootfs 大小
du -sh rootfs/
```

---

## 手动测试 rootfs

```bash
# 使用 systemd-nspawn 测试
sudo systemd-nspawn -D rootfs/ -b

# 或使用 chroot
sudo chroot rootfs/ /bin/bash

# 在 chroot 内检查服务状态
systemctl list-unit-files --type=service | grep enabled
```

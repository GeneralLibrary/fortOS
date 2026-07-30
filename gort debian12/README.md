# GORT Debian 12 — Trimmed Source Tree

此目录包含为 GORT 裁剪的 Debian 12 (Bookworm) 源码树。

**裁剪原则：** 仅保留 GORT 直接依赖、间接依赖及未来规划需要的包和服务，其余全部移除。

---

## 目录结构

```
gort debian12/
├── README.md                      # 本文件
├── config/
│   ├── gort-packages.list         # 包清单（REQUIRED / INDIRECT / FUTURE / BUILD）
│   └── enabled-services.conf      # 允许启动的 systemd 服务白名单
├── scripts/
│   ├── bootstrap-debian12.sh      # 主构建脚本（使用 Docker + debootstrap）
│   ├── trim-services.sh           # 服务裁剪脚本（在 chroot 内运行）
│   └── verify-trim.sh             # 验证裁剪结果
├── sources/                       # 下载的 Debian 源码包（离线构建用）
├── rootfs/                        # 裁剪后的最小化 Debian 12 根文件系统
└── docs/
    └── dependency-analysis.md     # GORT 依赖分析文档
```

---

## 快速开始

### 前提条件

- Docker（用于运行 debootstrap）
- Linux 主机（推荐）或 Windows Docker Desktop
- ~5GB 磁盘空间

### 一键构建

```bash
cd "gort debian12"
bash scripts/bootstrap-debian12.sh all
```

### 分步构建

```bash
# 步骤1：准备目录
bash scripts/bootstrap-debian12.sh prepare

# 步骤2：构建 bootstrap 容器
bash scripts/bootstrap-debian12.sh build-container

# 步骤3：运行 debootstrap（生成 rootfs）
bash scripts/bootstrap-debian12.sh debootstrap

# 步骤4：下载源码包（可选，离线构建用）
bash scripts/bootstrap-debian12.sh sources

# 步骤5：验证裁剪结果
bash scripts/bootstrap-debian12.sh trim
```

### 验证

```bash
bash scripts/verify-trim.sh
```

---

## 裁剪策略

### 默认启动的服务（白名单）

裁剪后的 Debian 12 **默认仅启动**以下服务：

| 分类 | 服务 | 用途 |
|------|------|------|
| **GORT** | `gort.service` | GORT 管理 API |
| **Docker** | `docker.service`, `containerd.service` | 容器运行时 |
| **Samba** | `smbd.service`, `nmbd.service` | SMB 文件共享 |
| **NFS** | `nfs-server.service`, `nfs-mountd.service`, `nfs-idmapd.service`, `rpcbind.service` | NFS 文件共享 |
| **FTP** | `vsftpd.service` | FTP 服务 |
| **SSH** | `ssh.service` | 远程管理 |
| **网络** | `NetworkManager.service`, `networking.service` | 网络管理 |
| **UPS** | `nut-monitor.service`, `nut-client.service` | UPS 监控 |
| **系统基础** | `systemd-journald`, `systemd-udevd`, `systemd-resolved`, `systemd-timesyncd`, `systemd-logind`, `dbus` 等 | 系统运行基础 |

### 已禁用的服务类别

以下类别的服务已全部禁用/mask：

- **桌面环境**：GNOME, KDE, Xfce, LXDE, Wayland, X11
- **打印**：CUPS
- **蓝牙**：Bluetooth
- **音频**：PulseAudio, ALSA, PipeWire
- **邮件**：Postfix, Exim
- **不必要的 TTY**：tty2-tty6（仅保留 tty1）
- **Avahi/mDNS**：Bonjour 兼容服务
- **不必要的定时器**：man-db, mlocate, fstrim 以外的所有 timer

---

## 包分类说明

### [REQUIRED] — 直接运行时依赖
GORT 运行时直接调用的命令和服务。例如：`samba`, `mdadm`, `nftables`

### [INDIRECT] — 间接/传递依赖
共享库、系统工具、运行时支持。例如：`libssl3`, `python3`, `dbus`

### [FUTURE] — 未来规划需要
GORT Roadmap 中 v1.1/v1.2/v2.0 规划的功能所需。例如：`zfsutils-linux`, `sssd`, `wireguard-tools`

### [BUILD] — 仅构建时需要
ISO 构建依赖，运行时不需要。例如：`live-build`, `xorriso`, `debootstrap`

---

## GORT 各模块依赖映射

| GORT 模块 | Debian 包依赖 |
|-----------|--------------|
| `GORT.Platform` (Linux) | `systemd`, `udev`, `util-linux`, `parted`, `smartmontools` |
| `GORT.Modules.Storage` | `mdadm`, `lvm2`, `e2fsprogs`, `xfsprogs`, `btrfs-progs` |
| `GORT.Modules.Share` | `samba`, `nfs-kernel-server`, `vsftpd` |
| `GORT.Modules.Network` | `nftables`, `iptables`, `iproute2`, `NetworkManager`, `netplan.io` |
| `GORT.Modules.Backup` | `rsync`, `rclone` |
| `GORT.Modules.Update` | `curl`, `ca-certificates` |
| `GORT.Agent` | `docker-ce`, `docker-compose-plugin`, `containerd.io` |
| `GORT.Observability` | `logrotate`, `loki` (Docker) |
| `GORT.Security` | `libssl3`, `libgssapi-krb5-2` |

---

## 更新裁剪配置

当 GORT 增加新功能依赖时：

1. 编辑 `config/gort-packages.list` 添加新包
2. 编辑 `config/enabled-services.conf` 添加新服务
3. 重新运行 `bash scripts/bootstrap-debian12.sh all`
4. 运行 `bash scripts/verify-trim.sh` 验证

---

## 与 GORT ISO 构建集成

裁剪后的 rootfs 可以直接用于 ISO 构建：

```bash
# 在 eng/iso/build.sh 中使用裁剪后的 rootfs
export GORT_TRIMMED_ROOTFS="$(pwd)/gort debian12/rootfs"
VERSION=1.0.0 bash eng/iso/build.sh
```

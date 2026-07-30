# GORT Debian 12 依赖分析

> 版本：v1.0 | 更新日期：2026-07-26

本文档详细记录了 GORT 对 Debian 12 系统和服务的依赖关系，作为裁剪依据。

---

## 1. 依赖分析方法

对 GORT 源码进行了以下维度的分析：

1. **Dockerfile** — 容器镜像中的 `apt-get install` 包列表
2. **eng/iso/config/package-lists/gort.list.chroot** — ISO 构建的包列表
3. **eng/iso/config/hooks/live/0100-gort-runtime.hook.chroot** — 启用的 systemd 服务
4. **eng/iso/build-in-container.sh** — ISO 构建流程
5. **src/GORT.Platform/Linux/**.cs — 平台层调用的所有 Linux 命令
6. **src/GORT.Modules.*/*.cs** — 各模块调用的外部命令
7. **src/GORT.Agent/**.cs — Docker/容器管理依赖
8. **docs/gort-architecture.md** — 架构文档中引用的服务和命令
9. **docker-compose.yml** — 容器编排依赖

---

## 2. 命令级依赖映射

### 2.1 GORT.Platform/Linux/LinuxDiskManager.cs

| 命令 | 提供包 | 用途 |
|------|--------|------|
| `lsblk` | `util-linux` | 列出块设备 |
| `parted` | `parted` | 磁盘分区 |
| `mdadm` | `mdadm` | RAID 管理 |
| `smartctl` | `smartmontools` | SMART 健康检查 |
| `wipefs` | `util-linux` | 清除磁盘签名 |

### 2.2 GORT.Platform/Linux/LinuxFileSystem.cs

| 命令 | 提供包 | 用途 |
|------|--------|------|
| `mount` / `umount` | `mount` (util-linux) | 挂载/卸载文件系统 |
| `mkfs.ext4` | `e2fsprogs` | 格式化 ext4 |
| `mkfs.xfs` | `xfsprogs` | 格式化 XFS |
| `mkfs.btrfs` | `btrfs-progs` | 格式化 Btrfs |
| `findmnt` | `util-linux` | 查询挂载信息 |
| `df` | `coreutils` | 磁盘空间查询 |

### 2.3 GORT.Platform/Linux/LinuxProcessManager.cs

| 命令 | 提供包 | 用途 |
|------|--------|------|
| `systemctl` | `systemd` | 服务管理 |
| `kill` | `procps` | 进程终止 |

### 2.4 GORT.Platform/Linux/LinuxNetworkManager.cs

| 命令 | 提供包 | 用途 |
|------|--------|------|
| `ip` | `iproute2` | 网络接口管理 |
| `netplan` | `netplan.io` | 网络配置 |
| `nft` | `nftables` | 防火墙规则 |
| `iptables` | `iptables` | 防火墙（兼容模式） |

### 2.5 GORT.Modules.Share (Samba/NFS/FTP)

| 命令 | 提供包 | 用途 |
|------|--------|------|
| `smbpasswd` | `samba-common-bin` | Samba 用户密码管理 |
| `smbd` / `nmbd` | `samba` | SMB 服务守护进程 |
| `exportfs` | `nfs-kernel-server` | NFS 导出管理 |
| `rpcbind` | `rpcbind` | RPC 端口映射 |
| `vsftpd` | `vsftpd` | FTP 守护进程 |

### 2.6 GORT.Modules.Backup

| 命令 | 提供包 | 用途 |
|------|--------|------|
| `rsync` | `rsync` | 增量备份 |
| `rclone` | `rclone` | 云备份 |
| `btrfs subvolume` | `btrfs-progs` | Btrfs 快照 |
| `zfs snapshot` | `zfsutils-linux` | ZFS 快照（FUTURE） |
| `xfs_quota` | `xfsprogs` | XFS 配额管理 |

### 2.7 GORT.Agent (Docker)

| 命令 | 提供包 | 用途 |
|------|--------|------|
| `docker` | `docker-ce-cli` | Docker CLI |
| `dockerd` | `docker-ce` | Docker 守护进程 |
| `containerd` | `containerd.io` | 容器运行时 |
| `docker compose` | `docker-compose-plugin` | Compose 编排 |

### 2.8 GORT.Observability

| 命令 | 提供包 | 用途 |
|------|--------|------|
| `logrotate` | `logrotate` | 日志轮转 |

---

## 3. 内核模块依赖

| 模块 | 用途 | 是否必需 |
|------|------|----------|
| `ext4` | ext4 文件系统 | **必需** |
| `xfs` | XFS 文件系统 | 按需 |
| `btrfs` | Btrfs 文件系统 | 按需 |
| `md_mod` / `raid*` | MD RAID | 按需 |
| `nfsd` / `nfs` | NFS 服务器 | 按需 |
| `cifs` | CIFS 客户端（可选） | 按需 |
| `nf_tables` / `nft_*` | nftables 防火墙 | **必需** |
| `overlay` | Docker overlay2 存储驱动 | **必需** |
| `bridge` | Docker bridge 网络 | **必需** |
| `veth` | Docker 虚拟网卡 | **必需** |
| `dm_mod` / `dm_*` | LVM / device-mapper | 按需 |
| `zfs` | ZFS 文件系统 | FUTURE |
| `usb_storage` / `uas` | USB 存储设备 | 按需 |
| `nvme` | NVMe 驱动 | 按需 |
| `ahci` / `ata_piix` | SATA 驱动 | **必需** |

---

## 4. 库依赖传递链

### .NET 10 运行时依赖

```
GORT.Api (self-contained)
├── libicu72
│   └── libicu72 (= 72.1-3)       # 直接
├── libssl3
│   └── libssl3 (= 3.0.11-1)      # 直接
│       └── libcrypto3             # 自动安装
├── libunwind8
│   └── libunwind8 (= 1.6.2-3)    # 直接
├── zlib1g
│   └── zlib1g (= 1:1.2.13)       # 直接（已包含在 base）
├── libstdc++6
│   └── libstdc++6 (≥ 12)          # 直接（已包含在 base）
├── libgcc-s1
│   └── libgcc-s1 (≥ 12)           # 直接（已包含在 base）
└── libgssapi-krb5-2
    ├── libkrb5-3                   # 自动安装
    ├── libkrb5support0             # 自动安装
    ├── libk5crypto3                # 自动安装
    └── libkeyutils1                # 自动安装
```

### Samba 传递依赖

```
samba
├── samba-common (= version)
├── samba-common-bin
├── samba-libs
├── samba-vfs-modules
├── libwbclient0
├── libsmbclient
├── samba-dsdb-modules
├── python3-samba
├── tdb-tools
└── libtalloc2, libtdb1, libtevent0, libldb2
```

### NetworkManager 传递依赖

```
network-manager
├── libnm0
├── libnl-3-200
├── libnl-route-3-200
├── libnl-genl-3-200
├── libndp0
├── libteamdctl0
├── dnsmasq-base
├── wireless-regdb
├── wpasupplicant
├── dbus
└── python3-dbus, python3-gi
```

---

## 5. 排除的服务及原因

| 被排除的服务/包类别 | 排除原因 |
|--------------------|----------|
| GNOME/KDE/Xfce/LXDE 桌面 | GORT 是 headless NAS，无需桌面环境 |
| Wayland/X11 显示服务 | 同上 |
| CUPS 打印服务 | NAS 不需要打印 |
| PulseAudio/ALSA 音频 | NAS 不需要音频 |
| Bluetooth 蓝牙 | NAS 不需要蓝牙 |
| Avahi/Bonjour | 企业环境不需要 mDNS |
| Postfix/Exim 邮件服务器 | GORT 通过外部 SMTP 发送告警 |
| Firefox/Chromium 浏览器 | headless 系统不需要 |
| LibreOffice | 不需要办公套件 |
| GIMP/Inkscape | 不需要图像处理 |
| 游戏/娱乐软件 | 不需要 |
| TeX/LaTeX | 不需要文档排版 |
| 开发工具 (gcc, g++, make) | 运行时不需要编译 |
| man-db.timer | 不需要定期更新 man 索引 |
| mlocate.timer | 不需要定期更新文件索引 |
| tty2-tty6 | 仅需一个 getty 控制台 |

---

## 6. 尺寸对比

| 版本 | 包数量 | 磁盘占用 (估计) | 启动服务数 |
|------|--------|----------------|-----------|
| 标准 Debian 12 最小安装 | ~800 | ~3.5 GB | ~40 |
| 标准 Debian 12 完整安装 | ~1500 | ~10 GB | ~80 |
| **GORT 裁剪版** | **~250** | **~1.2 GB** | **~15** |

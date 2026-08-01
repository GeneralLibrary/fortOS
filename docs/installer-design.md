# FortOS ISO 安装引导(UI)程序设计方案

> 状态:设计稿(Draft) · 目标版本:FortOS v1.1
> 决策:原生 GUI(Avalonia)+ 混合路线(自研 UI 驱动,复用成熟系统工具)

---

## 1. 背景与现状

当前 ISO(`eng/iso/build.sh` 通过 live-build 构建)内嵌 **Debian Installer 图形安装器**
(`lb config --debian-installer live --debian-installer-gui true`)。用户安装 FortOS 的体验是:

1. 进入 Debian 原生图形安装器(英文界面);
2. 手工完成语言/键盘/网络/分区/账户等通用步骤(其中大量步骤与 NAS 场景无关);
3. 安装完成后由 `fortos.service` 自动启动,再靠用户 SSH 手工做 NAS 特有初始化。

自动化路径使用 `eng/iso/tests/preseed.cfg` 注入 initrd,由 QEMU 脚本
(`eng/iso/test-install.sh`)驱动,用于 CI。

### 1.1 痛点

| 问题 | 影响 |
|------|------|
| 品牌与产品定位脱节 | 用户面对的是 Debian 安装器,而非 FortOS |
| 分区无 NAS 引导 | 系统盘/数据盘、RAID、加密布局需要用户自行理解 |
| 无安装后初始化 | 管理员账户、数据盘挂载、安全配置需手工完成 |
| 多语言缺失 | 仅 Debian 安装器自带语言,无 FortOS 品牌化 i18n |
| 进度不可控 | 安装执行阶段无统一的进度/日志视图 |

### 1.2 目标

- 提供 FortOS 品牌化的**图形安装向导**(参考 TrueNAS SCALE / Synology DSM 的安装体验);
- 面向 NAS 场景简化流程:语言 → 磁盘布局(系统盘 + 数据盘)→ 网络 → 账户 → 执行 → 完成;
- **复用成熟系统工具**做底层动作(sgdisk/parted、mkfs.*、mdadm、cryptsetup、rsync、chroot、grub-install),
  不重复造轮子;
- 保留无人值守(preseed)自动化路径,用于 CI 与批量部署;
- 技术栈与项目一致:.NET 10 + Avalonia UI;
- 镜像体积与启动时间可控(图形栈增量 ≤ ~300 MB)。

### 1.3 非目标(v1 不做)

- 安装时从网络拉取最新系统包(仅安装镜像内版本);
- 复杂的跨设备分区 UI(RAID 通过模板表达,不做自由布图);
- 安装阶段在线系统更新;
- ZFS 作为默认文件系统(镜像内置 zfs-dkms 体积大,列为实验选项,默认 btrfs)。

---

## 2. 总体架构

```
┌─────────────────────────────────────────────────────────────┐
│  FortOS.Installer.Gui (Avalonia)                            │
│  ┌───────────┬───────────┬───────────┬───────────┬─────────┐│
│  │ 语言/键盘 │ 磁盘布局   │ 网络配置  │ 账户/时区 │ 执行/完成││
│  └───────────┴───────────┴───────────┴───────────┴─────────┘│
├─────────────────────────────────────────────────────────────┤
│  FortOS.Installer.Core (无 UI 依赖,可单测)                   │
│  ┌───────────────────────────┐  ┌─────────────────────────┐ │
│  │ InstallerSession 状态机    │  │ Steps: Partition →      │ │
│  │ (编排/进度/回退/日志)       │  │ Format → Copy → Chroot  │ │
│  └───────────────────────────┘  │ → Bootloader            │ │
│                                  └─────────────────────────┘ │
│  Tools 适配层:lsblk · sgdisk · mkfs.* · mdadm · cryptsetup · │
│               rsync · chroot · grub-install · timedatectl    │
├─────────────────────────────────────────────────────────────┤
│  Live 环境(只读 rootfs)│ /run/live/medium/live/filesystem.squashfs │
└─────────────────────────────────────────────────────────────┘
```

- **UI 层**:Avalonia 应用,全屏 kiosk 运行在 live 环境的显示服务器上;
- **引擎层**:无 UI 依赖的 .NET 库,可被 GUI、无头 CLI、测试三种前端复用;
- **工具适配层**:进程级调用系统工具,输出解析为强类型 DTO。

三层分离保证:引擎可单测、可无头驱动(自动化)、UI 可替换。

---

## 3. UI 框架与 live 图形栈选型

### 3.1 UI 框架:Avalonia

- .NET 原生跨平台 UI,.NET 10 支持良好,技术栈与全项目统一(C#);
- Linux 下提供 X11 / Wayland 后端,支持无边框全屏 kiosk;
- `Avalonia.Headless` 支持 ViewModel 层无头测试;
- 对照:TUI(Spectre.Console)可作为失败回退,但不作为主 UI;GTK/Qt 与 .NET 绑定维护成本高。

### 3.2 live 环境显示服务器

Avalonia 不直接渲染到 DRM/framebuffer,需要显示服务器:

| 方案 | 增量体积 | 稳定性 | 结论 |
|------|---------|--------|------|
| **Xorg + Openbox** | ~120–200 MB | 驱动兼容性最好(D-I gui 同栈) | **推荐** |
| Wayland + Weston | ~60–100 MB | 部分老显卡/驱动有风险 | 备选实验 |

推荐 **Xorg + Openbox** 作为主图形栈,Weston 作为后续体积优化实验。

启动方式:live 环境默认进入 `multi-user.target`,由 `fortos-installer.service`
在 tty7 拉起 Xorg + Openbox,随后启动 Avalonia 安装器(kiosk 全屏)。
键盘完整导航(上下左右 + Tab/Enter),鼠标可用但不强制依赖。

### 3.3 失败回退

引擎层无 UI 依赖,因此可低成本增加一个 Spectre.Console TUI 前端
(`FortOS.Installer.Cli`,headless 模式 `fortos-installer --config install.yaml`):
- 显卡/驱动异常、纯串口安装时仍有完整安装能力;
- headless 模式同时服务自动化测试与批量部署。

---

## 4. 安装流程设计(向导页面)

```
欢迎/语言 ──► 磁盘布局 ──► 网络 ──► 账户/时区 ──► 确认 ──► 执行 ──► 完成
   │             │
   └── 键盘/时区  └── 系统盘 + 数据盘布局(可跳过数据盘,装后再配)
```

| 步骤 | 内容 | 关键行为 |
|------|------|----------|
| 1. 欢迎/语言 | FortOS 品牌页、版本、语言/键盘选择 | 记住选择,可回退 |
| 2. 磁盘布局 | 列出磁盘(`lsblk --json` + SMART 摘要);选择**系统盘**(将被擦除,二次确认);数据盘布局:单盘 ext4/btrfs/xfs、mdadm RAID1/5/10、LUKS 加密、ZFS(实验)或「暂不配置,装后由 FortOS 引导」 | 红色警示被清盘的目标盘;布局以模板表达 |
| 3. 网络 | DHCP 默认;静态 IP/DNS/网关可选;hostname | 安装期网络用于 NTP/可选 SSH 预置 |
| 4. 账户/时区 | 管理员用户名、密码(强度提示)、可选 SSH 公钥;时区/NTP | 写入目标系统的最终账户 |
| 5. 确认页 | 摘要:目标盘、布局、网络、账户;「开始安装」 | 此后进入不可回退区 |
| 6. 执行页 | 分阶段进度条 + 可展开实时日志(分区→格式化→复制→配置→引导→收尾) | 每阶段可重试;失败展示日志并允许重启重装 |
| 7. 完成页 | 卸载介质、重启按钮;展示访问地址与「首次启动向导」说明 | 写入安装摘要 `/etc/fortos/install-summary.json` |

安装完成后首次启动:FortOS API 首次运行时进入 first-boot 状态
(创建 NasToken 管理员、挂载数据盘、初始化存储卷),此部分属于运行时产品范围,
安装器仅在完成页给出指引(v1 不在安装器中实现)。

---

## 5. 安装引擎设计(FortOS.Installer.Core)

### 5.1 会话状态机

```
Idle → CollectInfo → Confirm → Partitioning → Formatting
     → Copying → Configuring → Bootloader → Finalize → Done
                                │
                                └── Failed(可重试/重启重装)
```

- 确认页之前可任意回退(状态可回滚);
- 确认页之后进入顺序执行,每步幂等、可重试;
- 日志双写:内存环形缓冲(UI 实时日志)+ 完成后落盘
  `/target/var/log/fortos-install.log` 与 `/target/etc/fortos/install-summary.json`。

### 5.2 系统盘分区布局(GPT)

| 分区 | 大小 | 类型 | 用途 |
|------|------|------|------|
| p1 | 1 MiB | BIOS boot(`bios_grub`) | Legacy BIOS 引导 |
| p2 | 512 MiB | EFI System(FAT32, `esp`) | UEFI 引导 |
| p3 | 剩余 | ext4(默认)/ btrfs(快照) | 系统根 `/` |
| p4 | 可选 | Linux swap | 交换分区(默认=内存,可关) |

数据盘:独立 GPT + 分区,或整盘 mdadm/zpool;文件系统模板:
- 单盘:ext4 / xfs / btrfs(默认推荐 btrfs,与快照能力一致);
- 冗余:mdadm RAID1/5/10(ext4/xfs/btrfs on md);
- 加密:LUKS2 单盘或 RAID 之上;
- ZFS:实验选项(镜像需带 zfs-dkms)。

### 5.3 系统复制(关键决策)

安装内容来源:**live 环境内嵌的完整 rootfs**(`/run/live/medium/live/filesystem.squashfs`)。

- live rootfs 就是「Debian 12 + FortOS 运行时 + Docker」的完整可用系统,
  无需网络、无需第二介质;
- 执行:挂载 squashfs 只读 → `rsync --one-file-system -aHAXS` 到 `/target`,
  排除 `/proc /sys /dev /run /tmp /mnt /target /live /media` 及缓存
  (`/var/cache/apt/archives`、`/var/lib/docker` 等);
- 复制后 chroot 清理 live-boot 特有残留(移除 live-boot 服务、重置 live 会话状态)。

### 5.4 chroot 配置(目标系统)

在 `/target` 内 chroot(`mount --bind` /dev /proc /sys /run)完成:

- 生成 `/etc/fstab`(分区经 `blkid` 取 UUID);
- hostname、时区、locale(继承向导选择);
- 创建管理员账户(`useradd` + `chpasswd`)、写入 sudoers、可选 SSH 公钥;
- 复用现有服务启用清单(与 `eng/iso/config/hooks/live/0100-fortos-runtime.hook.chroot`
  一致:docker、smbd/nmbd、nfs-server、vsftpd、ssh、NetworkManager、fortos);
- 写 `/etc/fortos/fortos.env`(FortOS_DATA_ROOT=/srv/nas)、`/etc/fortos/version`;
- 配置网络(NetworkManager connection 或 systemd-networkd)。

### 5.5 引导安装

- **UEFI**:从 live 环境拷贝 `shim-signed` + `grub-efi-amd64-signed` 至 `/target`,
  `grub-install --target=x86_64-efi --efi-directory=/target/boot/efi --bootloader-id=FortOS`,
  `grub-mkconfig` 生成配置;Secure Boot 链随 shim 生效;
- **Legacy BIOS**:`grub-install --target=i386-pc /dev/sdX`;
- 混合 ISO 场景同时安装两种引导(与 live-build `--uefi-secure-boot auto` 产物一致)。

---

## 6. 系统工具适配层

.NET 内以 `Process` 调用并解析输出(全部 `--json` / 结构化输出,禁止文本模糊解析):

| 适配器 | 工具 | 用途 |
|--------|------|------|
| `LsbklTool` | `lsblk --json` | 磁盘/分区枚举、挂载点、UUID |
| `SgdiskTool` | `sgdisk` | GPT 分区表创建/校验 |
| `MkfsTool` | `mkfs.ext4 / mkfs.btrfs / mkfs.xfs` | 格式化 |
| `MdadmTool` | `mdadm --create --detail --examine` | RAID 组装/状态 |
| `CryptsetupTool` | `cryptsetup luksFormat/open` | LUKS 加密 |
| `RsyncTool` | `rsync` | 系统复制 |
| `ChrootRunner` | `chroot` | 目标系统配置 |
| `GrubTool` | `grub-install / grub-mkconfig / efibootmgr` | 引导安装 |
| `SystemdTool` | `systemctl / timedatectl` | 服务启用、时钟/NTP |

- 每个适配器实现 `ITool` 接口,输出解析为强类型 DTO,便于单测(mock 进程输出 fixture);
- v2 优化:磁盘枚举/格式化部分可重构至 `FortOS.Platform`(`IDiskMgr`)供运行时与安装器共用,
  v1 独立实现以降低耦合风险。

---

## 7. 代码布局

```
src/
├── FortOS.Installer.Core/           安装引擎(无 UI,可测试)
│   ├── Session/                     InstallerSession 状态机、步骤编排、进度/日志
│   ├── Steps/                       Partition / Format / Copy / Chroot / Bootloader 步骤实现
│   ├── Tools/                       lsblk、sgdisk、mkfs、mdadm、cryptsetup、rsync、chroot、grub 适配器
│   ├── Models/                      磁盘、分区、布局模板、安装配置 DTO
│   └── Logging/                     环形日志、安装摘要序列化
├── FortOS.Installer.Gui/            Avalonia 向导 UI
│   ├── Views/                       每向导页一个 UserControl
│   ├── ViewModels/                  页面 VM(依赖引擎状态机)
│   └── Program.cs                   应用入口(检查 live 环境、提权)
└── FortOS.Installer.Cli/            Spectre.Console 无头前端(fallback + 自动化)
                                    (fortos-installer --config install.yaml)

tests/
└── FortOS.Tests.Installer/          引擎与适配器单测(Tools 以 fixture 注入)
eng/iso/
├── config/package-lists/fortos.list.chroot   增加图形栈与字体
├── config/includes.chroot/etc/systemd/system/fortos-installer.service
└── tests/installer-e2e.sh           QEMU 全流程集成测试(新)
```

---

## 8. ISO 构建集成(eng/iso 改动)

1. **包列表** `config/package-lists/fortos.list.chroot` 增加:
   `xorg`、`openbox`、`xinit`、`fonts-noto-cjk`(中文界面)、`rsync`、`mdadm`、
   `cryptsetup`、`parted`、`gdisk`、`dosfstools`、`grub-efi-amd64-signed`、`shim-signed`;
2. **发布**:`build-in-container.sh` 增加 `FortOS.Installer.Gui` 与
   `FortOS.Installer.Cli` 的 `dotnet publish`,落位 `/opt/fortos/installer/`;
3. **启动**:新增 `fortos-installer.service`(tty7 拉起 Xorg+Openbox → Avalonia 安装器);
4. **启动菜单**:自定义 isolinux/grub 菜单,默认项「FortOS 图形安装向导」;
   保留第二项「Debian Installer(专家/无人值守)」——依赖现有
   `--debian-installer live` 内嵌,D-I 与 preseed 自动化路径不回退;
5. **体积控制**:发布时 `--self-contained` + trimming,图形栈为最大增量(目标 ≤ 300 MB),验收基准纳入 CI 镜像检查;
6. **配套文档**:README「Debian 12 ISO」一节同步更新安装体验描述。

---

## 9. 测试策略

| 层级 | 内容 | 手段 |
|------|------|------|
| 单元 | 状态机编排、步骤逻辑、工具输出解析 | `FortOS.Tests.Installer`,Tools 以 fixture 注入 |
| ViewModel | 各向导页 VM 状态迁移 | Avalonia.Headless |
| 集成 | 完整安装(分区→复制→chroot→grub)无 UI 执行 | 新脚本 `eng/iso/tests/installer-e2e.sh`:`fortos-installer --config install.yaml` + QEMU(仿照现有 `test-install.sh`),断言分区表/`/etc/fstab`/grub/服务启用/`install-summary.json` |
| 场景矩阵 | 单盘 btrfs、ext4、mdadm RAID1、LUKS、UEFI/BIOS | CI 上 QEMU 子集(GitHub Actions) |
| UI 冒烟 | 向导可达性、键盘导航 | QEMU 截图断言(标注复杂度,CI 可选) |
| 回归 | 现有 preseed 无人值守路径 | 保持 `test-install.sh` 绿色 |

---

## 10. 里程碑

| 里程碑 | 内容 | 验收 |
|--------|------|------|
| **M1 引擎(无 UI)** | Core:状态机 + 分区/格式化/复制/chroot/grub 步骤;`FortOS.Installer.Cli` headless 跑通完整安装 | QEMU 全流程安装成功,重启后可启动 fortos 服务 |
| **M2 GUI 向导** | Avalonia 七页向导,绑定引擎;错误/重试/回退体验 | 人工走查 + ViewModel 测试 |
| **M3 ISO 集成** | 包列表、kiosk 启动、publish、启动菜单;生成可引导 ISO | 新 ISO 启动直达 FortOS 安装向导;D-I/preseed 项仍可用 |
| **M4 打磨与矩阵** | E2E 场景矩阵、i18n(中/英)、品牌视觉、体积验收、README 同步 | CI 全绿,镜像增量 ≤ 300 MB |

建议:M1 的 headless 模式同时成为自动化的官方通道,逐步替代部分 preseed 场景。

---

## 11. 风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| 部分硬件图形栈无驱动 | 安装向导无法显示 | 引擎无 UI 依赖 → `FortOS.Installer.Cli` TUI 回退;串口可用 |
| Secure Boot 签名链断裂 | 装后无法引导 | 安装时从 live 环境拷贝 shim-signed/grub 签名包;E2E 矩阵覆盖 UEFI |
| live rootfs 复制残留 live 会话配置 | 目标系统行为异常 | 明确排除清单 + chroot 清理脚本(live-boot 服务、会话状态);E2E 断言 |
| Avalonia 无头 kiosk 下输入受限 | 键盘导航缺失 | 向导全部操作可纯键盘完成;QEMU 截图冒烟 |
| 图形栈增大镜像体积 | 下载/写入变慢 | 接受 ≤ 300 MB 增量,CI 设体积护栏;Weston 作为后续优化 |
| 安装器自身崩溃 | 安装中断 | 日志先写内存环形缓冲,关键步骤完成后落盘;允许重启重装(分区幂等) |

---

## 12. 与现有系统的关系

- **D-I / preseed 路径保留**:作为无人值守与专家选项,CI 回归不破坏;
- **fortos.service 不变**:安装器只是替换「如何把系统放到磁盘」的交互方式,
  安装产物与现有 hook 产出一致(同一服务启用清单、同一 fortos.env);
- **FortOS.Platform 复用**:v1 独立实现工具适配层,v2 评估将磁盘能力上移到共享层。

// FortOS official website — i18n dictionaries (en / zh).
// Content is based on the fortOS codebase: README, src/FortOS.Modules.*,
// src/FortOS.Security, src/FortOS.Observability and the ISO workflow.

export const en = {
  common: {
    name: "FortOS",
    tagline:
      "A modern, security-first Linux NAS operating system, built on .NET 10.",
    github: "GitHub",
  },
  nav: {
    features: "Features",
    deploy: "Deploy",
    about: "About",
    contact: "Contact",
    language: "Language",
  },
  hero: {
    title: "A modern, security-first NAS operating system",
    badge: "Built on .NET 10",
    subtitle:
      "FortOS is a Linux NAS operating system built on .NET 10. Run it bare-metal from a Debian 12 ISO or inside Docker. Manage disks, shares, snapshots, backups, containers and security through one unified REST/gRPC API, a Web dashboard and a terminal CLI.",
    ctaPrimary: "Download ISO",
    ctaSecondary: "View on GitHub",
  },
  features: {
    heading: "Everything a modern NAS needs",
    subheading:
      "FortOS comes batteries included — six pillars cover the full lifecycle of your data, from raw disks to verified audit trails.",
    items: [
      {
        title: "Storage",
        description:
          "Disk discovery, RAID 0/1/5/6/10 (mdadm), ext4/XFS/Btrfs/ZFS filesystems, SMART monitoring and per-share quotas.",
        icon: "bx:hdd",
      },
      {
        title: "Sharing",
        description:
          "SMB, NFS and FTP with automatic config generation, Samba user provisioning and a recycle bin with retention policies.",
        icon: "bx:share-alt",
      },
      {
        title: "Protection",
        description:
          "Snapshots (btrfs/LVM thin), rsync and cloud backups with cron scheduling, plus point-in-time restore.",
        icon: "bx:shield-alt-2",
      },
      {
        title: "Security",
        description:
          "NasToken capability tokens, fine-grained NAbility permissions, data classification and an HMAC-chained, tamper-proof audit trail.",
        icon: "bx:lock-alt",
      },
      {
        title: "Containers",
        description:
          "Deploy containers from a curated agent catalog, with a capability-scoped token broker and hardened Compose generation.",
        icon: "bx:cube",
      },
      {
        title: "Observability",
        description:
          "Prometheus metrics, a 5-stage log pipeline with Loki, alerting via email/webhook and a live CLI dashboard.",
        icon: "bx:bar-chart-alt-2",
      },
    ],
  },
  logos: {
    heading: "Built on proven open-source foundations",
  },
  video: {
    watchOnBilibili: "Watch on Bilibili",
  },
  cta: {
    title: "Ready to take control of your data?",
    description:
      "Open source under GPL-3.0. Clone the repository, run it in Docker, or flash the ISO to your NAS hardware.",
    button: "Get Started on GitHub",
  },
  footer: {
    rights: "All rights reserved.",
    license: "Released under the GPL-3.0 license.",
    tagline: "A modern, security-first Linux NAS operating system.",
    madeWith: "Made with the",
    template: "Astroship",
    templateLink: "https://astroship.web3templates.com",
    templateName: "template",
  },
  about: {
    title: "About",
    desc: "The story behind FortOS.",
    h2: "A security-first NAS operating system",
    p1: "FortOS is an open-source Linux NAS operating system built on .NET 10. It runs bare-metal via a Debian 12 ISO, or inside Docker for evaluation. Every management surface — storage, sharing, protection, security, containers and observability — is exposed through a unified REST/gRPC API, a Web dashboard and a terminal CLI.",
    p2: "Docker containers are first-class citizens alongside native services such as SMB, NFS and rsync. The Web dashboard (Vue 3) ships with pages for overview, files, storage, shares, backups, snapshots, agents, network, services, logs, alerts and settings. Modules are hot-loadable DLLs, so the OS can grow over the air with SHA256-verified, staged updates.",
    p3: "Security is designed in from day one: NasToken capability tokens with delegation and device binding, fine-grained NAbility permissions, five-level data classification, TOTP two-factor authentication, account lockout and an HMAC-chained audit log you can verify with a single command.",
    listHeading: "Key capabilities",
    list: [
      "REST API on :5000 and gRPC (HTTP/2) on :5001",
      "Web dashboard served at /dashboard",
      "Terminal CLI with TUI dashboard (status --watch)",
      "Debian 12 ISO with BIOS & UEFI boot and a graphical installer",
      "SMART disk health, RAID health and Docker metrics",
      "Capability-scoped container agents with a token broker",
      "5-stage log pipeline, Loki storage and alert engine",
      "OTA module updates with staging and rollback",
    ],
    quickStart: "Quick start",
    quickStartCode:
      "git clone https://github.com/GeneralLibrary/fortOS.git\ncd fortOS\ndocker compose up -d --build",
  },
  deploy: {
    title: "Deploy FortOS",
    desc: "Three ways to run FortOS — pick what fits your environment.",
    popular: "Most Popular",
    cards: [
      {
        name: "Docker",
        price: "Evaluation",
        popular: false,
        features: [
          "One command to start",
          "docker compose with Loki included",
          "Ideal for testing and development",
          "First-class container support",
        ],
        button: { text: "Run with Docker", link: "https://github.com/GeneralLibrary/fortOS" },
      },
      {
        name: "Bare Metal",
        price: "Production",
        popular: true,
        features: [
          "Debian 12 ISO image",
          "BIOS & UEFI dual boot",
          "Graphical installer wizard",
          "Best for dedicated NAS hardware",
        ],
        button: {
          text: "Download ISO",
          link: "https://github.com/GeneralLibrary/fortOS/releases",
        },
      },
      {
        name: "From Source",
        price: "GPL-3.0",
        popular: false,
        features: [
          "Build with the .NET 10 SDK",
          "Contribute and customize freely",
          "Hot-loadable modules",
          "Full control over your stack",
        ],
        button: { text: "View Source", link: "https://github.com/GeneralLibrary/fortOS" },
      },
    ],
  },
  contact: {
    title: "Contact",
    desc: "Get in touch with the FortOS community.",
    h2: "Talk to the team",
    intro:
      "Have a question, a feature request or a bug report? The FortOS community is here to help. Open an issue or start a discussion on GitHub — or drop us an email.",
    github: "GitHub Issues",
    githubDesc: "Report bugs and request features.",
    discussions: "GitHub Discussions",
    discussionsDesc: "Ask questions and share setups.",
    repository: "GitHub Repository",
    repositoryDesc: "Explore the source code and star the project.",
    links: {
      github: "https://github.com/GeneralLibrary/fortOS/issues",
      discussions: "https://github.com/GeneralLibrary/fortOS/discussions",
      repository: "https://github.com/GeneralLibrary/fortOS",
    },
  },
  notfound: {
    title: "404 Not Found",
    desc: "Page not found.",
    home: "Back to Home",
  },
};

export const zh: typeof en = {
  common: {
    name: "FortOS",
    tagline: "基于 .NET 10 构建的现代、安全优先的 Linux NAS 操作系统。",
    github: "GitHub",
  },
  nav: {
    features: "特性",
    deploy: "部署",
    about: "关于",
    contact: "联系",
    language: "语言",
  },
  hero: {
    title: "现代、安全优先的 NAS 操作系统",
    badge: "基于 .NET 10 构建",
    subtitle:
      "FortOS 是一款基于 .NET 10 构建的 Linux NAS 操作系统。可通过 Debian 12 ISO 裸机运行，也可在 Docker 中快速体验。磁盘、共享、快照、备份、容器与安全，全部通过统一的 REST/gRPC API、Web 控制台与终端 CLI 管理。",
    ctaPrimary: "下载 ISO",
    ctaSecondary: "查看 GitHub",
  },
  features: {
    heading: "现代 NAS 所需的一切",
    subheading:
      "FortOS 开箱即用——六大支柱覆盖数据从裸盘到可验证审计链的完整生命周期。",
    items: [
      {
        title: "存储",
        description:
          "磁盘发现、RAID 0/1/5/6/10（mdadm）、ext4/XFS/Btrfs/ZFS 文件系统、SMART 监控与按共享配额。",
        icon: "bx:hdd",
      },
      {
        title: "共享",
        description:
          "SMB、NFS、FTP 自动生成配置，Samba 系统用户同步，带保留策略的回收站。",
        icon: "bx:share-alt",
      },
      {
        title: "保护",
        description:
          "快照（btrfs/LVM thin）、rsync 与云备份，支持 cron 调度与任意时间点恢复。",
        icon: "bx:shield-alt-2",
      },
      {
        title: "安全",
        description:
          "NasToken 能力令牌、NAbility 细粒度权限、数据分级，以及基于 HMAC 防篡改的审计链。",
        icon: "bx:lock-alt",
      },
      {
        title: "容器",
        description:
          "从精选 Agent 目录一键部署容器，配合能力收窄的 Token Broker 与加固的 Compose 生成。",
        icon: "bx:cube",
      },
      {
        title: "可观测性",
        description:
          "Prometheus 指标、五阶段日志管道 + Loki、邮件/Webhook 告警与实时 CLI 面板。",
        icon: "bx:bar-chart-alt-2",
      },
    ],
  },
  logos: {
    heading: "构建于成熟的开源技术之上",
  },
  video: {
    watchOnBilibili: "在 B 站观看",
  },
  cta: {
    title: "准备好掌控你的数据了吗？",
    description:
      "以 GPL-3.0 开源发布。克隆仓库，用 Docker 运行，或将 ISO 烧录到你的 NAS 硬件上。",
    button: "在 GitHub 上开始",
  },
  footer: {
    rights: "保留所有权利。",
    license: "以 GPL-3.0 许可证发布。",
    tagline: "现代、安全优先的 Linux NAS 操作系统。",
    madeWith: "使用",
    template: "Astroship",
    templateLink: "https://astroship.web3templates.com",
    templateName: "模板制作",
  },
  about: {
    title: "关于",
    desc: "FortOS 背后的故事。",
    h2: "安全优先的 NAS 操作系统",
    p1: "FortOS 是一款基于 .NET 10 构建的开源 Linux NAS 操作系统。它可通过 Debian 12 ISO 裸机运行，也可在 Docker 中评估体验。存储、共享、保护、安全、容器与可观测性等所有管理面，都通过统一的 REST/gRPC API、Web 控制台与终端 CLI 暴露。",
    p2: "Docker 容器与 SMB、NFS、rsync 等原生服务一样是一等公民。Web 控制台（Vue 3）内置总览、文件、存储、共享、备份、快照、Agent、网络、服务、日志审计、告警与设置等页面。模块以 DLL 形式热加载，系统可通过经 SHA256 校验、分阶段推进的 OTA 更新持续演进。",
    p3: "安全从第一天起就内建于设计之中：支持委派与设备绑定的 NasToken 能力令牌、NAbility 细粒度权限、五级数据分类、TOTP 双因子认证、登录锁定，以及一条命令即可验证完整性的 HMAC 审计链。",
    listHeading: "核心能力",
    list: [
      "REST API（:5000）与 gRPC/HTTP2（:5001）",
      "Web 控制台挂载于 /dashboard",
      "带 TUI 面板的终端 CLI（status --watch）",
      "Debian 12 ISO，支持 BIOS/UEFI 双启动与图形化安装向导",
      "SMART 磁盘健康、RAID 健康与 Docker 指标",
      "能力收窄的容器 Agent 与 Token Broker",
      "五阶段日志管道、Loki 存储与告警引擎",
      "支持分阶段推进与回滚的 OTA 模块更新",
    ],
    quickStart: "快速开始",
    quickStartCode:
      "git clone https://github.com/GeneralLibrary/fortOS.git\ncd fortOS\ndocker compose up -d --build",
  },
  deploy: {
    title: "部署 FortOS",
    desc: "三种方式运行 FortOS——选择适合你的环境。",
    popular: "最受欢迎",
    cards: [
      {
        name: "Docker",
        price: "体验模式",
        popular: false,
        features: [
          "一条命令启动",
          "docker compose，内置 Loki",
          "适合测试与开发",
          "一等公民的容器支持",
        ],
        button: { text: "用 Docker 运行", link: "https://github.com/GeneralLibrary/fortOS" },
      },
      {
        name: "裸机安装",
        price: "生产环境",
        popular: true,
        features: [
          "Debian 12 ISO 镜像",
          "BIOS 与 UEFI 双启动",
          "图形化安装向导",
          "最适合专用 NAS 硬件",
        ],
        button: {
          text: "下载 ISO",
          link: "https://github.com/GeneralLibrary/fortOS/releases",
        },
      },
      {
        name: "源码构建",
        price: "GPL-3.0",
        popular: false,
        features: [
          "使用 .NET 10 SDK 构建",
          "自由贡献与定制",
          "模块热加载",
          "完全掌控技术栈",
        ],
        button: { text: "查看源码", link: "https://github.com/GeneralLibrary/fortOS" },
      },
    ],
  },
  contact: {
    title: "联系",
    desc: "与 FortOS 社区取得联系。",
    h2: "和团队聊聊",
    intro:
      "有问题、功能需求或 Bug 反馈？FortOS 社区随时提供帮助。在 GitHub 上提交 Issue 或发起讨论，也可以直接给我们发邮件。",
    github: "GitHub Issues",
    githubDesc: "提交 Bug 与功能需求。",
    discussions: "GitHub Discussions",
    discussionsDesc: "提问并分享部署经验。",
    repository: "GitHub 仓库",
    repositoryDesc: "浏览源码并为项目点星。",
    links: {
      github: "https://github.com/GeneralLibrary/fortOS/issues",
      discussions: "https://github.com/GeneralLibrary/fortOS/discussions",
      repository: "https://github.com/GeneralLibrary/fortOS",
    },
  },
  notfound: {
    title: "404 页面不存在",
    desc: "页面不存在。",
    home: "返回首页",
  },
};

export type Dict = typeof en;

export function getDict(locale: string | undefined): Dict {
  return locale === "zh" ? zh : en;
}

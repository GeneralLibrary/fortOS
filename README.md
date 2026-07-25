# GNAS — 鸿蒙启发的新一代 NAS 系统

GNAS（General NAS）是一个以 **.NET 10 + Docker + OpenTelemetry** 构建的 Linux NAS 系统。它借鉴鸿蒙分布式安全思想，将身份、能力（NAbility）、数据分级与不可篡改审计链组合起来，为家庭、工作室与边缘设备提供可自部署、可观测、容器原生的 NAS 管理体验。

## 核心特性

- **Linux 原生运行**：支持 Linux x64 与 Linux ARM64，发行镜像基于 Debian 12。
- **统一 API 网关**：REST/gRPC 暴露健康、磁盘、共享、服务、Agent、审计与告警能力。
- **鸿蒙式安全模型**：NasToken + NAbility + NasDataLevel + ACL 联合决策。
- **Service Bus 服务管理**：统一监管原生进程与 Docker Compose 容器服务。
- **Agent 深度集成**：Agent Catalog、Token Broker、Compose Generator 与日志采集闭环。
- **全链路可观测性**：结构化日志、Loki 接入、告警规则、审计链完整性校验。
- **CLI 优先体验**：`gnas` 支持批处理、JSON 输出与终端 TUI。

## 快速开始

### 系统要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Linux x64 / Linux ARM64；ISO 安装目标为 Debian 12 x64 |
| 运行时 | .NET 10 SDK（开发）或 .NET 10 Runtime（部署） |
| 容器 | Docker Engine + Docker Compose v2 |
| 权限 | Linux 部署建议具备 `/srv/nas`、Docker socket 与必要磁盘管理权限 |

### Docker Compose 启动

```bash
docker compose up -d --build
curl http://localhost:5000/api/health
```

预期返回：

```json
{"status":"ok"}
```

停止服务：

```bash
docker compose down
```

> 默认 Compose 使用 host 网络、`/srv/nas` 数据根与 Docker socket，以便 GNAS 管理宿主服务与 Agent 容器。
> Docker 模式不提供内核 NFS 服务；NFS 共享仅在 Debian ISO 裸机安装中启用。

### Debian 12 ISO 安装

仓库提供基于 Debian `live-build` 的 amd64 混合启动镜像。镜像同时支持
Legacy BIOS 和 UEFI，包含 Debian 图形安装器、GNAS API/CLI、Docker Compose v2
以及 NAS 所需的磁盘和共享工具。

在 Linux x64 构建机上安装 Docker，然后执行：

```bash
VERSION=1.0.0 bash eng/iso/build.sh
```

构建过程在固定的 Debian 12 容器中完成，宿主机不会安装 `live-build` 或 .NET SDK。
产物写入 `artifacts/iso/`：

```text
gnas-debian12-1.0.0-amd64.iso
gnas-debian12-1.0.0-amd64.iso.sha256
```

验证并写入 U 盘（请将 `/dev/sdX` 替换为整块 U 盘设备，写入会清除其数据）：

```bash
cd artifacts/iso
sha256sum --check gnas-debian12-1.0.0-amd64.iso.sha256
sudo dd if=gnas-debian12-1.0.0-amd64.iso of=/dev/sdX bs=4M status=progress conv=fsync
```

从 U 盘启动设备，选择 Debian Installer，按向导完成系统盘、网络和管理员账户配置。
安装后的 GNAS 由 `gnas.service` 自动启动，默认监听 `http://0.0.0.0:5000`；
数据根目录为 `/srv/nas`。也可以在 GitHub Actions 中手动运行 **GNAS Debian ISO**
工作流下载 ISO 与 SHA-256 校验文件。

## 项目结构（16 个项目）

```text
GNAS.slnx
├── src/
│   ├── GNAS.Core/              # 核心模型、抽象、数据库与配置
│   ├── GNAS.Platform/          # Linux 平台实现
│   ├── GNAS.Security/          # NasToken、身份、权限与密钥存储
│   ├── GNAS.ServiceBus/        # 服务注册、监管、事件总线、健康检查
│   ├── GNAS.Agent/             # Agent 目录、令牌代理、Compose 生成器
│   ├── GNAS.Modules/           # 模块宿主与模块基类
│   ├── GNAS.Modules.Storage/   # 磁盘、RAID、文件系统模块
│   ├── GNAS.Modules.Share/     # SMB/NFS/FTP/回收站/配额模块
│   ├── GNAS.Modules.Network/   # 网络与防火墙配置模块
│   ├── GNAS.Modules.Agent/     # Agent 编排模块
│   ├── GNAS.Modules.Backup/    # 快照、rsync、云备份模块
│   ├── GNAS.Modules.Update/    # OTA 与版本检查模块
│   ├── GNAS.Observability/     # 日志、审计链、告警、Serilog
│   ├── GNAS.Api/               # ASP.NET Core REST/gRPC 网关
│   └── GNAS.Cli/               # gnas 命令列与 TUI
└── tests/
    └── GNAS.Tests.Integration/ # 集成与 E2E 测试
```

## 阶段开发顺序

| 阶段 | Issue | 内容 |
|------|-------|------|
| Phase 1 | [#2](../../issues/2) | Core contracts、配置、数据库基础 |
| Phase 2 | [#3](../../issues/3) | Platform 抽象与 Linux 实现 |
| Phase 3 | [#4](../../issues/4) | Security、NasToken、NAbility、Identity |
| Phase 4 | [#5](../../issues/5) | Service Bus、Registry、Supervisor、EventBus |
| Phase 5 | [#6](../../issues/6) | Storage/Share/Network/Backup/Update 模块 |
| Phase 6 | [#7](../../issues/7) | Agent Catalog、Token Broker、Compose Generator |
| Phase 7 | [#8](../../issues/8) | API 网关、gRPC、CLI 命令树 |
| Phase 8 | [#9](../../issues/9) | Observability、日志、告警、审计链 |
| Phase 9 | [#10](../../issues/10) | Docker/CI、README、集成与 E2E 测试完善 |

## 依赖注入注册顺序

| 注册方法 | 所属项目 | 主要职责 |
|----------|----------|----------|
| `AddGnasCore` | `GNAS.Core` | `IDatabaseProvider`、`IGnasConfiguration` |
| `AddPlatformServices` | `GNAS.Platform` | 磁盘、文件系统、进程、网络、用户平台实现 |
| `AddGnasSecurity` | `GNAS.Security` | 密钥存储、Token、Identity、Permission Engine |
| `AddServiceBus` | `GNAS.ServiceBus` | EventBus、Registry、Supervisor、HealthMonitor |
| `AddModuleHost` | `GNAS.Modules` | 模块发现、初始化与生命周期管理 |
| `AddAgentServices` | `GNAS.Agent` | Agent Catalog、Token Broker、Compose Generator、日志采集 |
| `AddObservability` | `GNAS.Observability` | LogPipeline、AuditChain、AlertEngine、Serilog |

典型 API 启动顺序：

```csharp
services.AddGnasCore();
services.AddPlatformServices();
services.AddGnasSecurity(configuration);
services.AddServiceBus();
services.AddModuleHost();
services.AddAgentServices();
services.AddObservability(configuration);
```

## CLI 使用示例

查看系统状态：

```bash
gnas status
```

以 JSON 输出磁盘列表：

```bash
gnas disk list --output json
```

指定服务地址与令牌：

```bash
gnas --server http://localhost:5000 --token "$NAS_TOKEN" service list
```

审计链校验：

```bash
gnas audit verify --output json
```

部署 Agent（模板 + 镜像 + 数据卷）：

```bash
gnas agent deploy nginx-basic --image nginx:alpine --agent-id web-nginx --volume /srv/nas/agents-data/web-nginx:/data:rw
```

文件管理（创建、读取、软删除、恢复）：

```bash
gnas file write /srv/nas/demo/hello.txt --content "hello gnas" --overwrite
gnas file read /srv/nas/demo/hello.txt
gnas file delete /srv/nas/demo/hello.txt --confirm
```

备份任务与手动执行：

```bash
gnas backup task set media-backup --source /srv/nas/media --target /srv/nas/backup/media --cron interval:60
gnas backup task run media-backup
gnas backup run list --task-id media-backup
```

恢复流程（rsync/snapshot）：

```bash
gnas recovery start /srv/nas/media --source /srv/nas/backup/media --mode rsync --dry-run --confirm
```

不带参数运行且终端可交互时，`gnas` 会进入 TUI 仪表盘。

## 开发与验证

还原、构建：

```bash
dotnet restore GNAS.slnx
dotnet build GNAS.slnx
```

运行全部测试：

```bash
dotnet test
```

仅运行集成/E2E 测试：

```bash
dotnet test tests/GNAS.Tests.Integration --filter "Category=Integration"
```

CI 使用 `.github/workflows/ci.yml` 执行矩阵构建、非集成测试、发布验证与 Docker 集成测试。Docker 相关 E2E 会先检测 Docker 可用性；不可用时安全跳过该测试路径。

## 配置与数据目录

常用环境变量：

```bash
GNAS_DATA_ROOT=/srv/nas
GNAS_CONFIG_PATH=/srv/nas/config/nas.yaml
ASPNETCORE_URLS=http://0.0.0.0:5000
ASPNETCORE_ENVIRONMENT=Production
```

数据根目录下会存放 SQLite 数据库、密钥存储、Agent Compose 文件、日志与模块数据。测试会将数据根指向 `TestArtifacts/` 下的隔离目录，避免污染宿主系统。

# GNAS — 新一代跨平台 NAS 系统技术架构

> **项目代号**: GNAS  
> **版本**: Architecture v2.0  
> **最新修订**: 2026-07-25  
> **技术栈**: .NET 9 + Docker + OpenTelemetry

---

## 目录

1. [设计目标与核心原则](#1-设计目标与核心原则)
2. [整体分层架构](#2-整体分层架构)
3. [展示层 — Desktop CLI Tool](#3-展示层)
4. [API 网关层](#4-api-网关层)
   - 4.3 [gRPC 服务定义 (Proto Contracts)](#43-grpc-服务定义-proto-contracts)
5. [应用层 — .NET Modules](#5-应用层)
   - 5.3 [存储池管理详细设计](#53-存储池管理详细设计)
   - 5.4 [共享协议详细设计](#54-共享协议详细设计)
   - 5.5 [数据保护与备份策略](#55-数据保护与备份策略)
6. [安全与身份认证层](#6-安全与身份认证层)
7. [服务总线容器层](#7-服务总线容器层)
8. [Docker/Agent 集成层](#8-dockeragent-集成层)
9. [平台抽象层](#9-平台抽象层)
10. [日志与可观测性层](#10-日志与可观测性层)
11. [数据流图](#11-数据流图)
12. [技术选型总览](#12-技术选型总览)
13. [部署架构](#13-部署架构)
14. [系统安装与初始化引导](#14-系统安装与初始化引导)
15. [UPS 集成 (不间断电源)](#15-ups-集成-不间断电源)
16. [安全增强设计](#16-安全增强设计)
17. [与 OMV 原架构的对比](#与-omv-原架构的对比)
18. [架构决策记录 (ADR)](#架构决策记录-adr--architecture-decision-records)
19. [变更记录 (Changelog)](#变更记录-changelog)

---

## 1. 设计目标与核心原则

### 1.1 设计目标

| 目标 | 说明 |
|------|------|
| **跨平台** | 支持 Linux x64、Windows x64、Linux ARM64 三大平台 |
| **Docker 原生集成** | Agent/应用以容器方式部署，与 NAS 系统深度交互 |
| **安全优先** | 借鉴鸿蒙分布式安全思想，适配 NAS 多用户场景 |
| **统一服务管理** | 一个容器管理所有服务（原生进程 + Docker 容器） |
| **.NET 全栈** | 全系统使用 .NET 技术栈，CLI 作为唯一交互界面 |

### 1.2 核心原则

```
原则                              实现方式
────                               ────────

1. 平台无关                       平台抽象层 + .NET RID 多目标编译
   (Linux/Windows/ARM)

2. Agent 深度集成                 Agent Catalog → Token Broker → Compose Generator
   (Docker + NAS Token + Volume)   → Service Bus 统一管理生命周期

3. 安全 = 能力 + 身份 + 数据分级   NasToken + NAbility + NasDataLevel
   (鸿蒙思想，NAS 场景化)          三者独立但联动

4. 统一服务管理                    Service Bus Container
   (原生进程 + Docker 容器)        同时管理 smb-daemon 和 openclaw-agent

5. .NET 全栈                       ASP.NET Core (API) + CLI (交互)
   (跨平台 + 高性能)                + gRPC (IPC) + 内置 Web Dashboard

6. 全面可观测                      六类日志 + 不可篡改审计链 + 全链路追踪
   (Observability)
```

### 1.3 品牌 NAS 架构参考

本架构综合参考了四大品牌 NAS 的 Docker 设计思路：

| 品牌 | 借鉴点 |
|------|--------|
| **Unraid** | 官方 Docker（不魔改）、社区模板机制、CLI 完全友好 |
| **TrueNAS SCALE** | ZFS Dataset 粒度的存储隔离、K8s→Compose 的架构教训 |
| **Synology DSM** | ACL 深度集成、权限自动映射 |
| **QNAP** | 多运行时架构思想（Docker + LXD + Kata） |

---

## 2. 整体分层架构

```
┌═══════════════════════════════════════════════════════════════════════════┐
║              NAS SYSTEM — COMPLETE ARCHITECTURE (WITH OBSERVABILITY)      ║
╠═══════════════════════════════════════════════════════════════════════════╣
║                                                                            ║
║  ┌──────────────────────────────────────────────────────────────────┐    ║
║  │  PRESENTATION       Desktop CLI Tool (gnas) │ Web Dashboard*       │    ║
║  └────────────────────────────┬─────────────────────────────────────┘    ║
║                               │ HTTPS / REST API                          ║
║  ┌────────────────────────────┴─────────────────────────────────────┐    ║
║  │  API GATEWAY       RESTful API │ gRPC (内部 IPC)               │    ║
║  └────────────────────────────┬─────────────────────────────────────┘    ║
║                               │                                          ║
║  ┌────────────────────────────┴─────────────────────────────────────┐    ║
║  │  APPLICATION (.NET)  Storage │ Share │ Network │ Agent │ Backup  │    ║
║  └────────────────────────────┬─────────────────────────────────────┘    ║
║                               │                                          ║
║  ┌────────────────────────────┴─────────────────────────────────────┐    ║
║  │  SECURITY            Identity │ ATM │ NAbility │ NasKeyStore     │    ║
║  │  (HarmonyOS Inspired)  NasToken + Capability + DataLevel          │    ║
║  └────────────────────────────┬─────────────────────────────────────┘    ║
║                               │                                          ║
║  ┌────────────────────────────┴─────────────────────────────────────┐    ║
║  │  SERVICE BUS          Registry │ Supervisor │ IPC │ Health │Event │    ║
║  │  CONTAINER            Native Host (smb/nfs/...) │ Container Host  │    ║
║  └────────────────────────────┬─────────────────────────────────────┘    ║
║                               │                                          ║
║  ┌────────────────────────────┴─────────────────────────────────────┐    ║
║  │  DOCKER/AGENT         Catalog │ Token Broker │ Compose Generator  │    ║
║  │  INTEGRATION          OpenClaw │ HomeAssistant │ Custom Agent     │    ║
║  └────────────────────────────┬─────────────────────────────────────┘    ║
║                               │                                          ║
║  ┌────────────────────────────┴─────────────────────────────────────┐    ║
║  │  PLATFORM             IDiskMgr │ IFS │ INetMgr │ IProcMgr │ ...  │    ║
║  │  ABSTRACTION          Linux-x64 │ Win-x64 │ Linux-arm64          │    ║
║  └────────────────────────────┬─────────────────────────────────────┘    ║
║                               │                                          ║
║  ┌────────────────────────────┴─────────────────────────────────────┐    ║
║  │  OPERATING SYSTEM    Debian/Ubuntu │ Windows 10/11 │ ARM Linux   │    ║
║  └──────────────────────────────────────────────────────────────────┘    ║
║                                                                            ║
╠═══════════════════════════════════════════════════════════════════════════╣
║                    横切关注点 — 全层贯通                                    ║
║                                                                            ║
║  ╔══════════════════════════════════════════════════════════════════════╗ ║
║  ║               OBSERVABILITY & LOGGING (可观测性)                     ║ ║
║  ║                                                                      ║ ║
║  ║  Producers ─→ Pipeline ─→ Classifier ─→ Storage ─→ Query ─→ Alert   ║ ║
║  ║                                                                      ║ ║
║  ║  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌───────┐ ║ ║
║  ║  │ System   │  │ Audit    │  │ Access   │  │ Agent    │  │Metric │ ║ ║
║  ║  │ Log      │  │ Chain    │  │ Log      │  │ Log      │  │Log    │ ║ ║
║  ║  │ File +   │  │ Vault    │  │ SQLite   │  │ Loki     │  │TSDB   │ ║ ║
║  ║  │ Loki     │  │ (防篡改) │  │          │  │          │  │       │ ║ ║
║  ║  └──────────┘  └──────────┘  └──────────┘  └──────────┘  └───────┘ ║ ║
║  ║                                                                      ║ ║
║  ║  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌─────────────────────┐ ║ ║
║  ║  │ Log      │  │ Alert    │  │ Retention│  │ Dashboard           │ ║ ║
║  ║  │ Viewer   │  │ Engine   │  │ Manager  │  │ (Grafana 风格)      │ ║ ║
║  ║  └──────────┘  └──────────┘  └──────────┘  └─────────────────────┘ ║ ║
║  ╚══════════════════════════════════════════════════════════════════════╝ ║
║                                                                            ║
║  ╔══════════════════════════════════════════════════════════════════════╗ ║
║  ║               TRACE PROPAGATION (链路追踪)                           ║ ║
║  ║  CLI/API → API GW → Module → Service Bus → Native/Container Service  ║ ║
║  ║  (同一个 TraceId 全链路透传)                                          ║ ║
║  ╚══════════════════════════════════════════════════════════════════════╝ ║
║                                                                            ║
║  ╔══════════════════════════════════════════════════════════════════════╗ ║
║  ║               SECURITY AUDIT (安全审计切面)                           ║ ║
║  ║  权限决策、数据访问、配置变更 → 强制写入 Audit Log → 不可篡改链        ║ ║
║  ╚══════════════════════════════════════════════════════════════════════╝ ║
╚═══════════════════════════════════════════════════════════════════════════╝
```

---

## 3. 展示层 — Desktop CLI Tool

GNAS 的展示层仅包含一个跨平台命令行工具 `gnas`，所有管理操作均通过 CLI 完成。

```
┌──────────────────────────────────────────────────────────────┐
│                   PRESENTATION LAYER  展示层                  │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │               Desktop CLI Tool (gnas)                   │ │
│  │                                                        │ │
│  │  · 跨平台 Console Application (.NET 9)                 │ │
│  │  · 通过 REST API 与 NAS 后端通信                        │ │
│  │  · 交互式 TUI 模式 (终端 UI) + 批处理模式                │ │
│  │  · 管道友好 (JSON / Table 输出)                         │ │
│  │  · 本地配置文件管理 (~/.gnas/config)                    │ │
│  └────────────────────────┬───────────────────────────────┘ │
│                           │                                  │
│  ┌────────────────────────┴───────────────────────────────┐ │
│  │  通信协议: HTTPS REST API (JSON)                        │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 3.1 交互式 TUI 模式

不带子命令直接运行 `gnas` 进入交互式终端 UI：

```text
┌──────────────────────────────────────────────────────────┐
│  GNAS v1.0.0                          nas://home-nas     │
│──────────────────────────────────────────────────────────│
│                                                          │
│  System Status: ● Healthy    Uptime: 14d 3h 21m          │
│                                                          │
│  ┌── Disks ──────────────────────────────────────────┐  │
│  │  sda  WD Red 4TB      ● OK     38°C   60% used    │  │
│  │  sdb  WD Red 4TB      ● OK     40°C   60% used    │  │
│  │  sdc  Samsung SSD 256 ● OK     45°C   30% used    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌── Services ────────────────────────────────────────┐  │
│  │  smb-daemon    ● Running    PID:1234               │  │
│  │  nfs-server    ● Running    PID:1235               │  │
│  │  openclaw      ● Running    Container:abc123       │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌── Recent Alerts ───────────────────────────────────┐  │
│  │  10:23  [WARN] 磁盘 sda 使用率 92%                  │  │
│  │  09:15  [INFO]  Agent openclaw 已部署               │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  Press F1-Help F2-Services F3-Logs F4-Agents F10-Quit   │
└──────────────────────────────────────────────────────────┘
```

### 3.2 批处理模式

```bash
# 查询
gnas status                    # 系统状态概览
gnas disk list                 # 磁盘列表
gnas share list                # 共享文件夹列表
gnas service list              # 服务列表
gnas agent list                # Agent 列表

# 操作
gnas share create media /mnt/nas/data/media
gnas service restart smb-daemon
gnas agent deploy openclaw --capabilities "storage:share:media:read"
gnas agent start openclaw

# 日志与监控
gnas log view --follow --category agent --agent openclaw
gnas log query --level error --last 1h
gnas audit verify              # 验证审计链完整性
gnas alert list --severity warning

# 输出格式
gnas disk list --output json   # JSON 输出 (管道友好)
gnas disk list --output table  # 表格输出 (默认)
gnas disk list --no-color      # 禁用 ANSI 颜色
```

### 3.3 内置 Web Dashboard（可选辅助）

CLI 是主要交互方式。对于需要图形化监控的场景，GNAS 可选择性启用一个轻量级内嵌 Web Dashboard：

- 通过浏览器访问 `http://nas-host:5000/dashboard`
- 纯静态 HTML + Vanilla JS，无框架依赖
- 通过 REST API 获取数据，无 WebSocket
- 提供基础的系统健康、磁盘、服务、Agent 状态面板
- 不提供管理操作（所有操作通过 CLI 完成）

### 3.4 CLI 设计原则

| 原则 | 说明 |
|------|------|
| **管道优先** | 所有查询命令支持 `--output json`，可接入 `jq`、PowerShell 等工具 |
| **幂等操作** | 管理操作设计为幂等（如 `create` 已存在时跳过或报错） |
| **确认保护** | 危险操作（删除、格式化）默认要求 `--confirm` 或交互确认 |
| **离线友好** | CLI 仅调用 REST API，不依赖 WebSocket 长连接 |
| **脚本化** | 支持 `--token` 参数直接传入 NasToken，无需交互登录

---

## 4. API 网关层

```
┌──────────────────────────────────────────────────────────────┐
│                     API GATEWAY LAYER  API网关层              │
│  ┌────────────────────────┐  ┌────────────────────────────┐  │
│  │     RESTful API        │  │         gRPC               │  │
│  │  (ASP.NET Core WebAPI) │  │   (内部服务间通信)          │  │
│  │                        │  │                            │  │
│  │  · CLI 客户端交互       │  │  · Module ↔ Service Bus   │  │
│  │  · 第三方集成           │  │  · 高性能 IPC             │  │
│  └────────────────────────┘  └────────────────────────────┘  │
│                                                              │
│  职责:                                                       │
│  · 请求认证 (JWT/NasToken 验证)                               │
│  · 速率限制 (Rate Limiting)                                   │
│  · 请求日志 (Access Log)                                      │
│  · 可选: 内嵌 Dashboard 静态文件                              │
└──────────────────────────────────────────────────────────────┘
```

### 4.1 协议选择

| 协议 | 用途 | 传输 |
|------|------|------|
| **RESTful API** | CLI 客户端、第三方集成、内嵌 Dashboard | HTTP/1.1, HTTP/2 |
| **gRPC** | 内部服务间高性能通信、Module ↔ Service Bus | HTTP/2 (Protocol Buffers) |

### 4.3 gRPC 服务定义 (Proto Contracts)

所有内部服务间通信基于以下 Protobuf 定义。每个 Module 必须暴露对应的 gRPC Service。

#### 4.3.1 存储服务 (Storage)

```protobuf
// protos/storage.proto
syntax = "proto3";
package gnas.storage;
option csharp_namespace = "GNAS.Proto.Storage";

service StorageService {
  // 磁盘管理
  rpc ListDisks (ListDisksRequest) returns (ListDisksResponse);
  rpc GetDiskDetail (GetDiskDetailRequest) returns (DiskDetail);
  rpc TriggerSmartCheck (SmartCheckRequest) returns (SmartCheckResponse);

  // RAID 管理
  rpc CreateRaid (CreateRaidRequest) returns (RaidResult);
  rpc GetRaidStatus (GetRaidStatusRequest) returns (RaidStatus);
  rpc DeleteRaid (DeleteRaidRequest) returns (RaidResult);

  // 文件系统管理
  rpc MountFilesystem (MountRequest) returns (MountResult);
  rpc UnmountFilesystem (UnmountRequest) returns (MountResult);
  rpc FormatFilesystem (FormatRequest) returns (FormatResult);
  rpc GetFilesystemInfo (FsInfoRequest) returns (FsInfo);

  // 流式 (重建进度 / Scrub 进度)
  rpc WatchRaidRebuild (RebuildWatchRequest) returns (stream RebuildProgress);
  rpc WatchScrubProgress (ScrubWatchRequest) returns (stream ScrubProgress);
}

message DiskInfo {
  string path = 1;
  string model = 2;
  string serial = 3;
  int64 size_bytes = 4;
  string interface_type = 5;     // SATA, NVMe, USB
  bool is_ssd = 6;
  string smart_status = 7;       // OK, Warning, Failed
  int32 temperature_celsius = 8;
  double used_percent = 9;
}

message ListDisksRequest {}
message ListDisksResponse { repeated DiskInfo disks = 1; }
message GetDiskDetailRequest { string disk_path = 1; }

message CreateRaidRequest {
  RaidLevel level = 1;
  repeated string disk_paths = 2;
  string pool_name = 3;
  string filesystem = 4;         // ext4, xfs, btrfs, zfs
}

enum RaidLevel { RAID_UNKNOWN = 0; RAID_0 = 1; RAID_1 = 2; RAID_5 = 3; RAID_6 = 4; RAID_10 = 5; }

message RaidResult {
  bool success = 1;
  string pool_id = 2;
  string message = 3;
  string error_code = 4;         // 统一错误码
}

message RebuildProgress {
  string pool_id = 1;
  double percent_complete = 2;
  int64 bytes_remaining = 3;
  int64 estimated_seconds = 4;
}
```

#### 4.3.2 共享服务 (Share)

```protobuf
// protos/share.proto
syntax = "proto3";
package gnas.share;
option csharp_namespace = "GNAS.Proto.Share";

service ShareService {
  rpc CreateShare (CreateShareRequest) returns (ShareResult);
  rpc DeleteShare (DeleteShareRequest) returns (ShareResult);
  rpc ListShares (ListSharesRequest) returns (ListSharesResponse);
  rpc UpdateSharePermissions (UpdatePermRequest) returns (ShareResult);
  rpc GetConnectedClients (ClientsRequest) returns (stream ConnectedClient);
}

message ShareDefinition {
  string share_id = 1;
  string name = 2;
  string path = 3;
  string comment = 4;
  repeated ShareProtocol protocols = 5;  // SMB, NFS, FTP, WebDAV
  bool read_only = 6;
  bool guest_ok = 7;
  bool browseable = 8;
  bool recycle_bin = 9;
  NasDataLevel data_level = 10;
}

enum ShareProtocol { PROTO_UNKNOWN = 0; SMB = 1; NFS = 2; FTP = 3; SFTP = 4; WEBDAV = 5; }

message CreateShareRequest { ShareDefinition share = 1; }
message ShareResult {
  bool success = 1;
  string share_id = 2;
  string message = 3;
  string error_code = 4;
}
message ListSharesResponse { repeated ShareDefinition shares = 1; }
```

#### 4.3.3 Agent 服务 (Agent)

```protobuf
// protos/agent.proto
syntax = "proto3";
package gnas.agent;
option csharp_namespace = "GNAS.Proto.Agent";

service AgentService {
  rpc ListAgents (ListAgentsRequest) returns (ListAgentsResponse);
  rpc DeployAgent (DeployAgentRequest) returns (DeployAgentResponse);
  rpc StartAgent (AgentActionRequest) returns (AgentActionResult);
  rpc StopAgent (AgentActionRequest) returns (AgentActionResult);
  rpc RemoveAgent (AgentActionRequest) returns (AgentActionResult);
  rpc GetAgentLogs (AgentLogRequest) returns (stream LogEntry);
  rpc GetAgentStatus (AgentStatusRequest) returns (AgentStatus);
  rpc ListAgentTemplates (TemplateListRequest) returns (TemplateListResponse);
}

message AgentConfig {
  string agent_id = 1;
  string template_id = 2;
  string display_name = 3;
  repeated string capabilities = 4;       // NAbility 字符串
  repeated VolumeMapping volumes = 5;
  repeated PortMapping ports = 6;
  ResourceQuota quota = 7;
}

message VolumeMapping {
  string host_path = 1;                   // /mnt/nas/data/media
  string container_path = 2;              // /data/media
  bool read_only = 3;
}

message PortMapping {
  int32 host_port = 1;
  int32 container_port = 2;
  string protocol = 3;                    // tcp | udp
}

message ResourceQuota {
  double cpu_limit = 1;
  int64 memory_limit_bytes = 2;
  int32 io_weight = 3;
}

message AgentStatus {
  string agent_id = 1;
  string status = 2;                      // running | stopped | crashed | deploying
  string container_id = 3;
  double cpu_percent = 4;
  int64 memory_bytes = 5;
  int64 uptime_seconds = 6;
}

message DeployAgentRequest { AgentConfig config = 1; }
message DeployAgentResponse {
  bool success = 1;
  string agent_id = 2;
  string compose_file_path = 3;
  string message = 4;
  string error_code = 5;
}
```

#### 4.3.4 服务总线 (Service Bus)

```protobuf
// protos/servicebus.proto
syntax = "proto3";
package gnas.servicebus;
option csharp_namespace = "GNAS.Proto.ServiceBus";

service ServiceBusService {
  rpc ListServices (ListServicesRequest) returns (ListServicesResponse);
  rpc StartService (ServiceActionRequest) returns (ServiceActionResult);
  rpc StopService (ServiceActionRequest) returns (ServiceActionResult);
  rpc RestartService (ServiceActionRequest) returns (ServiceActionResult);
  rpc GetServiceStatus (ServiceStatusRequest) returns (ServiceStatusInfo);
  rpc WatchServiceEvents (ServiceWatchRequest) returns (stream ServiceEvent);
}

message ServiceStatusInfo {
  string service_id = 1;
  string status = 2;               // stopped | starting | running | stopping | failed
  string type = 3;                  // Native | Container | Module
  int32 pid = 4;                    // 原生进程 PID (容器为 0)
  double cpu_percent = 5;
  int64 memory_bytes = 6;
  int64 uptime_seconds = 7;
}

message ServiceEvent {
  string event_id = 1;
  string service_id = 2;
  string event_type = 3;           // started | stopped | crashed | health_changed
  string message = 4;
  int64 timestamp_unix = 5;
}
```

#### 4.3.5 审计与日志 (Audit)

```protobuf
// protos/audit.proto
syntax = "proto3";
package gnas.audit;
option csharp_namespace = "GNAS.Proto.Audit";

service AuditService {
  rpc QueryLogs (LogQueryRequest) returns (LogQueryResponse);
  rpc StreamLogs (LogQueryRequest) returns (stream LogEntry);
  rpc VerifyChain (VerifyChainRequest) returns (VerifyChainResponse);
  rpc GetAuditStats (AuditStatsRequest) returns (AuditStats);
  rpc ExportAuditChain (ExportRequest) returns (stream ExportChunk);
}

message LogEntry {
  string log_id = 1;
  int64 timestamp_unix = 2;
  string category = 3;         // System | Audit | Access | Agent | Trace | Metric
  string level = 4;            // Trace | Debug | Info | Warn | Error | Fatal
  string source_component = 5;
  string message = 6;
  string trace_id = 7;
  string user_id = 8;
  string agent_id = 9;
  map<string, string> properties = 10;
  repeated string tags = 11;
}

message LogQueryRequest {
  string category = 1;
  string min_level = 2;
  int64 from_unix = 3;
  int64 to_unix = 4;
  string search_text = 5;
  repeated string tags = 6;
  int32 limit = 7;
  int32 offset = 8;
}

message LogQueryResponse {
  repeated LogEntry entries = 1;
  int32 total_count = 2;
  bool has_more = 3;
}

message VerifyChainResponse {
  bool valid = 1;
  int64 total_entries = 2;
  int32 invalid_entries = 3;
  string first_broken_at = 4;
  string message = 5;
}
```

#### 4.3.6 统一错误码

```protobuf
// protos/common.proto
syntax = "proto3";
package gnas.common;
option csharp_namespace = "GNAS.Proto.Common";

// gRPC 统一错误 — 通过 google.rpc.Status details 传递
// 所有服务在出错时返回此结构

message ErrorDetail {
  ErrorCode code = 1;
  string message = 2;
  string details = 3;            // 人类可读的详细信息
  string trace_id = 4;           // 关联链路追踪
  map<string, string> metadata = 5;
}

enum ErrorCode {
  // 0 保留为成功
  OK = 0;

  // 通用错误 1xxx
  UNKNOWN = 1000;
  INVALID_ARGUMENT = 1001;
  NOT_FOUND = 1002;
  ALREADY_EXISTS = 1003;
  PERMISSION_DENIED = 1004;
  RESOURCE_EXHAUSTED = 1005;
  INTERNAL_ERROR = 1006;
  UNAVAILABLE = 1007;
  TIMEOUT = 1008;

  // 存储错误 2xxx
  DISK_NOT_FOUND = 2001;
  DISK_IN_USE = 2002;
  DISK_IO_ERROR = 2003;
  RAID_DEGRADED = 2004;
  RAID_CREATE_FAILED = 2005;
  FS_MOUNT_FAILED = 2006;
  FS_FORMAT_FAILED = 2007;
  POOL_FULL = 2008;

  // 安全错误 3xxx
  TOKEN_EXPIRED = 3001;
  TOKEN_INVALID = 3002;
  TOKEN_REVOKED = 3003;
  CAPABILITY_INSUFFICIENT = 3004;
  DATA_LEVEL_INSUFFICIENT = 3005;
  ACCOUNT_LOCKED = 3006;
  TPM_UNAVAILABLE = 3007;

  // Agent 错误 4xxx
  AGENT_NOT_FOUND = 4001;
  AGENT_DEPLOY_FAILED = 4002;
  AGENT_START_FAILED = 4003;
  AGENT_CRASH_LOOP = 4004;
  COMPOSE_GENERATE_FAILED = 4005;
  DOCKER_UNAVAILABLE = 4006;

  // 服务总线错误 5xxx
  SERVICE_NOT_FOUND = 5001;
  SERVICE_DEPENDENCY_FAILED = 5002;
  SERVICE_ALREADY_RUNNING = 5003;
  CIRCULAR_DEPENDENCY = 5004;
}
```

#### 4.3.7 Proto 文件目录规范

```
protos/
├── common.proto              # 共享类型 + 错误码
├── storage.proto             # 存储服务
├── share.proto               # 共享服务
├── network.proto             # 网络服务
├── agent.proto               # Agent 服务
├── backup.proto              # 备份服务
├── servicebus.proto          # 服务总线
├── audit.proto               # 审计与日志
├── auth.proto                # 认证服务
└── update.proto              # 更新服务
```

所有 Proto 文件通过 `Grpc.Tools` MSBuild 集成编译，生成代码统一放在 `GNAS.Proto` 命名空间下。

---

## 5. 应用层

```
┌──────────────────────────────────────────────────────────────────┐
│              APPLICATION LAYER — .NET 8/9  应用层                 │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                  Module Host (模块主机)                      │  │
│  │  · 模块热加载/卸载 (AssemblyLoadContext)                    │  │
│  │  · 依赖注入注册 (IServiceCollection)                        │  │
│  │  · 能力声明与验证 (RequireCapability Attribute)             │  │
│  │  · 模块生命周期管理                                         │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────────┐   │
│  │ Storage  │ │  Share   │ │ Network  │ │   Monitoring     │   │
│  │ Module   │ │  Module  │ │  Module  │ │   Module         │   │
│  ├──────────┤ ├──────────┤ ├──────────┤ ├──────────────────┤   │
│  │· 磁盘管理│ │· SMB/CIFS│ │· 接口管理│ │· 资源监控        │   │
│  │· RAID/LVM│ │· NFS     │ │· 防火墙  │ │· 日志聚合        │   │
│  │· 文件系统│ │· FTP/SFTP│ │· DHCP/DNS│ │· 告警通知        │   │
│  │· 加密卷  │ │· WebDAV  │ │· VLAN    │ │· 健康检查        │   │
│  └──────────┘ └──────────┘ └──────────┘ └──────────────────┘   │
│                                                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────────┐   │
│  │  Agent   │ │  Backup  │ │  Update  │ │   Plugin         │   │
│  │  Module  │ │  Module  │ │  Module  │ │   Manager        │   │
│  ├──────────┤ ├──────────┤ ├──────────┤ ├──────────────────┤   │
│  │· Agent   │ │· Rsync   │ │· OTA升级 │ │· 热加载/卸载     │   │
│  │  生命周期│ │· 快照    │ │· 回滚    │ │· 依赖解析       │   │
│  │· 令牌管理│ │· 云备份  │ │· 版本检查│ │· 沙箱隔离       │   │
│  │· 能力授权│ │· 定时任务│ │          │ │                  │   │
│  └──────────┘ └──────────┘ └──────────┘ └──────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

### 5.1 模块定义（接口契约）

```csharp
/// <summary>
/// NAS 模块基接口。所有业务模块必须实现此接口。
/// </summary>
public interface INasModule
{
    /// <summary>模块唯一标识</summary>
    string ModuleId { get; }

    /// <summary>模块显示名称</summary>
    string DisplayName { get; }

    /// <summary>模块版本</summary>
    Version Version { get; }

    /// <summary>模块声明需要的系统能力</summary>
    IReadOnlyList<NAbility> RequiredCapabilities { get; }

    /// <summary>模块声明依赖的其他模块</summary>
    IReadOnlyList<string> Dependencies { get; }

    /// <summary>模块初始化（注册 DI、启动后台服务等）</summary>
    Task InitializeAsync(ModuleContext context, CancellationToken ct);

    /// <summary>模块优雅关闭</summary>
    Task ShutdownAsync(CancellationToken ct);

    /// <summary>模块健康检查</summary>
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct);
}

/// <summary>
/// 模块上下文，模块通过此对象访问系统服务
/// </summary>
public record ModuleContext
{
    public IServiceProvider Services { get; init; }
    public IEventBus EventBus { get; init; }
    public ILoggerFactory LoggerFactory { get; init; }
    public string DataDirectory { get; init; }
}
```

### 5.2 模块清单

| 模块 | ModuleId | 依赖 | 说明 |
|------|----------|------|------|
| **Storage** | `storage` | — | 磁盘枚举、RAID 管理、LVM、文件系统格式化、SMART 监控、加密卷管理 |
| **Share** | `share` | `storage` | SMB/CIFS、NFS v3/v4、FTP/SFTP、WebDAV 共享服务管理 |
| **Network** | `network` | — | 网络接口管理、防火墙规则、DHCP/DNS、VLAN 配置 |
| **Agent** | `agent` | `storage`, `security` | Agent 生命周期管理、Token 签发与续期、Compose 生成、容器监控 |
| **Backup** | `backup` | `storage` | Rsync 任务、快照计划、云备份、定时任务 |
| **Update** | `update` | — | OTA 固件升级、模块更新、灰度发布、回滚 |
| **Monitoring** | `monitoring` | — | 资源监控、日志聚合查看、Dashboard 数据提供 |
| **Plugin** | `plugin` | — | 第三方插件加载、依赖解析、沙箱隔离、版本兼容检查 |

### 5.3 存储池管理详细设计

存储池是 NAS 系统的核心数据容器。一个完整的存储池生命周期如下：

```
创建 → 格式化 → 挂载 → 数据集/子卷创建 → 共享 → 监控 → 扩容/替换 → 退役
```

#### 5.3.1 存储池创建流程

```
用户选择磁盘
      │
      ▼
┌─────────────────────────────────────────────────────────┐
│  1. 磁盘发现与验证                                      │
│     · 枚举所有未使用的物理磁盘 (IDiskManager.ListDisks) │
│     · 检查磁盘是否为空 (无分区表/无文件系统签名)        │
│     · SMART 快速检测 → 排除故障盘                       │
│     · 按接口类型分组 (SATA / NVMe / USB)                │
│     · 标记 SSD vs HDD，用于后续分层存储建议             │
└──────────────────────────┬──────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│  2. RAID 级别选择                                       │
│                                                         │
│  磁盘数  │ RAID 0  │ RAID 1  │ RAID 5  │ RAID 6  │ RAID10│
│  ────────┼─────────┼─────────┼─────────┼─────────┼───────│
│    1     │   ✓     │   —     │   —     │   —     │   —   │
│    2     │   ✓     │   ✓     │   —     │   —     │   —   │
│    3     │   ✓     │   —     │   ✓     │   —     │   —   │
│    4+    │   ✓     │   ✓     │   ✓     │   ✓     │   ✓   │
│                                                         │
│  推荐策略:                                              │
│  · 1-2 盘 → RAID 1 (镜像, 数据安全优先)                │
│  · 3-5 盘 → RAID 5 (容量与安全平衡)                    │
│  · 6+ 盘 → RAID 6 或 RAID 10 (性能+高可靠性)           │
│  · SSD 阵列 → 可选 RAID 5，但需注意写入放大            │
│  · 混合 SSD+HDD → 建议分层存储 (SSD 缓存池 + HDD 数据) │
└──────────────────────────┬──────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│  3. 文件系统选择                                        │
│                                                         │
│  文件系统 │ 最大卷 │ CoW  │ 压缩 │ 快照 │ 校验和 │ 自愈 │
│  ────────┼────────┼──────┼──────┼──────┼───────┼──────│
│  ext4    │  1EB  │  ✗   │  ✗   │  ✗   │   ✗   │  ✗   │
│  XFS     │  8EB  │  ✗   │  ✗   │  ✗   │   ✗   │  ✗   │
│  Btrfs   │ 16EB  │  ✓   │  ✓   │  ✓   │   ✓   │  ✓   │
│  ZFS     │ 256ZB │  ✓   │  ✓   │  ✓   │   ✓   │  ✓   │
│  NTFS    │  8PB  │  ✗   │  ✓   │  ✓*  │   ✗   │  ✗   │
│  ReFS    │ 35PB  │  ✓   │  ✗   │  ✗   │   ✓   │  ✓   │
│                                                         │
│  推荐:                                                  │
│  · Linux 主力 → Btrfs (轻量 CoW, 内建快照压缩)        │
│  · 高级需求 → ZFS (最高数据完整性, 自愈能力)           │
│  · 简单需求 → ext4/XFS (稳定, 低开销)                  │
│  · Windows → ReFS (Mirror Accelerated Parity)          │
│  · 跨平台共享盘 → NTFS 或 ext4 (兼容性最好)           │
└──────────────────────────┬──────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│  4. 数据集/子卷规划                                      │
│                                                         │
│  推荐结构 (Btrfs/ZFS):                                  │
│  pool-main/                                             │
│  ├── data/              # 通用数据                      │
│  │   ├── media/         # 媒体库 (无压缩, 大文件)       │
│  │   ├── documents/     # 文档 (压缩, 去重)            │
│  │   ├── photos/        # 照片 (轻度压缩)              │
│  │   └── downloads/     # 下载 (无快照, 不保留历史)    │
│  ├── backup/            # 备份目标 (压缩, 去重)        │
│  ├── appdata/           # Agent/Docker 持久化数据       │
│  ├── home/              # 用户主目录 (按用户分子卷)    │
│  └── timemachine/       # Time Machine 备份 (配额限制)  │
│                                                         │
│  每个数据集独立配置:                                    │
│  · 压缩算法 (zstd/lz4/gzip)                             │
│  · 快照策略 (保留数量/频率)                              │
│  · 配额 (容量硬限制/软限制)                             │
│  · 数据分级标签 (NasDataLevel L0-L4)                    │
│  · 记录大小 (recordsize, 适配文件类型)                  │
└─────────────────────────────────────────────────────────┘
```

#### 5.3.2 磁盘替换与重建流程

```
┌─────────────────────────────────────────────────────────┐
│  磁盘故障检测                                          │
│  · SMART 自检 (每30分钟)                               │
│  · 内核 I/O 错误监控 (/sys/block/*/stat)               │
│  · RAID 事件监听 (mdadm --monitor / zed)               │
│  · 触发: storage.disk.failed 事件                      │
└──────────────────────────┬──────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│  自动处理流程                                          │
│                                                         │
│  1. 标记磁盘为 FAULTY                                   │
│     · mdadm: mdadm --manage /dev/mdX --fail /dev/sdY    │
│     · ZFS: zpool offline pool disk-id                  │
│                                                         │
│  2. 通知管理员                                          │
│     · Critical 告警 → 邮件/Webhook/终端通知             │
│     · CLI 提示: gnas status 显示红色磁盘状态            │
│                                                         │
│  3. 插入新磁盘后                                        │
│     · 自动检测: udev 事件 → storage.disk.added          │
│     · 检查磁盘容量 ≥ 故障盘容量                          │
│     · 自动分区 (如需要, 参照原分区表)                   │
│                                                         │
│  4. 开始重建                                            │
│     · mdadm: mdadm --manage /dev/mdX --add /dev/sdZ    │
│     · ZFS: zpool replace pool old-disk new-disk        │
│     · 监控重建进度: /proc/mdstat 或 zpool status       │
│     · 进度事件: storage.pool.rebuilding {percent}       │
│                                                         │
│  5. 重建完成                                            │
│     · 发布 storage.pool.healthy 事件                   │
│     · 恢复定时 Scrub 计划                               │
│                                                         │
│  热备盘 (Hot Spare):                                    │
│  · 预先配置 1-2 个热备盘                                │
│  · 故障时自动触发重建，无需人工介入                     │
│  · 重建完成后原有热备盘变为普通盘，自动补充新热备盘     │
└─────────────────────────────────────────────────────────┘
```

#### 5.3.3 数据清洗 (Scrub) 调度

```
┌─────────────────────────────────────────────────────────┐
│  Scrub 策略配置                                        │
│                                                         │
│  类型          │ 频率   │ 优先级  │ 说明                │
│  ─────────────┼────────┼────────┼─────────────────────│
│  快速 Scrub   │ 每周   │  低    │ 仅检查元数据校验和   │
│  完整 Scrub   │ 每月   │  中    │ 检查全部数据校验和   │
│  深度 Scrub   │ 每季度 │  低    │ 全盘读取 (检测静默错误)│
│                                                         │
│  调度窗口: 默认 02:00-06:00 (备份窗口同期)             │
│  限速: 默认 100MB/s 读取, 可配置 (避免影响正常IO)     │
│                                                         │
│  ZFS Scrub 命令:                                        │
│  zpool scrub pool-main                                 │
│                                                         │
│  Btrfs Scrub 命令:                                      │
│  btrfs scrub start /mnt/nas/data                       │
│                                                         │
│  mdadm RAID Check:                                      │
│  echo check > /sys/block/mdX/md/sync_action            │
│                                                         │
│  Scrub 结果:                                            │
│  · 发现错误 → storage.pool.scrub.error 事件            │
│  · 已修复   → storage.pool.scrub.repaired 事件 (CoW)   │
│  · 完成     → storage.pool.scrub.completed 事件         │
│  · 错误数超过阈值 → Critical 告警                      │
└─────────────────────────────────────────────────────────┘
```

#### 5.3.4 存储池扩容

```
扩容方式:
─────────────────────────────────────────────
1. 添加磁盘到现有 RAID (仅部分RAID级别支持)
   · mdadm RAID 5/6: mdadm --grow --raid-devices=N /dev/mdX --add /dev/sdY
   · ZFS: zpool add pool /dev/sdY (成为新 VDEV，注意数据平衡)
   · Btrfs: btrfs device add /dev/sdY /mnt/nas/data

2. 替换更大容量磁盘 (逐个替换)
   · 替换 → 重建 → 下一块 → ... → 全部替换后自动扩容
   · ZFS 支持 autoexpand 属性

3. 添加 JBOD/单盘池 (灵活性最高)
   · 适合非关键数据 (downloads, 临时文件)

扩容限制:
· RAID 级别降级期间禁止扩容
· 扩容过程中暂停 Scrub
· 扩容进度实时可查
· 禁止拔出正在重建的磁盘 (Critical 告警)
```

### 5.4 共享协议详细设计

#### 5.4.1 SMB/CIFS 配置细节

```yaml
# /srv/nas/config/services/smb.yaml
smb:
  workgroup: WORKGROUP
  server_string: "GNAS File Server"
  netbios_name: gnas-nas
  
  # 全局安全设置
  security: user                      # user | share (已废弃) | ads (AD域)
  encrypt_passwords: true
  server_signing: mandatory           # disabled | auto | mandatory
  smb_encrypt: desired                # 传输加密 (SMB 3.1.1)
  
  # 协议版本
  server_min_protocol: SMB2_10        # 最低 SMB 2.1 (Win7+)
  server_max_protocol: SMB3_11        # 最高 SMB 3.1.1
  
  # 性能优化
  socket_options: "TCP_NODELAY IPTOS_LOWDELAY SO_RCVBUF=131072 SO_SNDBUF=131072"
  read_raw: yes
  write_raw: yes
  strict_allocate: yes
  aio_read_size: 1
  aio_write_size: 1
  
  # macOS 兼容
  vfs_objects:
    - catia                          # macOS 特殊字符映射
    - fruit                          # macOS SMB 扩展
  fruit:aapl: true
  fruit:nfs_aces: false              # 不传输 NFS ACE
  
  # 共享定义
  shares:
    - name: media
      path: /mnt/nas/data/media
      comment: "Media Library (Read Only)"
      read_only: true
      guest_ok: false
      browseable: true
      vfs_objects:
        - recycle                    # 回收站
      recycle:
        repository: .recycle/%U      # 每用户回收站
        keeptree: yes
        versions: yes
        maxsize: 0                   # 不限制文件大小
        exclude: ["*.tmp", "*.temp", ".DS_Store"]
      
    - name: documents
      path: /mnt/nas/data/documents
      comment: "Shared Documents"
      read_only: false
      guest_ok: false
      browseable: true
      create_mask: 0660
      directory_mask: 0770
      force_user: nasuser
      force_group: nasusers
```

#### 5.4.2 NAS 权限模型映射

```
GNAS 权限层              SMB/NFS               POSIX/Linux
─────────────────────────────────────────────────────────────
NasDataLevel (L0-L4)  → 文件目录标签         → xattr (user.nas_level)
NAbility (能力)       → Token Capability     → N/A (API 侧校验)
RBAC (角色)           → SMB Group Mapping    → Linux Group
ACL (文件级)          → SMB ACL / NFSv4 ACL  → POSIX ACL (getfacl/setfacl)
用户配额              → SMB quota            → Linux Quota (xfs_quota / btrfs qgroup)

权限决策流程:
  SMB 请求 → Samba 验证用户 → 查询文件 POSIX ACL → 执行 I/O
                    │
                    ▼
           若配置了 NasToken 集成:
           Samba VFS Module → 检查 NasDataLevel → 发布 Audit Log
```

#### 5.4.3 用户配额管理

```csharp
/// <summary>
/// 存储配额定义。支持每用户和每共享粒度的配额控制。
/// </summary>
public record StorageQuota
{
    /// <summary>配额目标: user:{username} | share:{shareName} | group:{groupName}</summary>
    public string TargetId { get; init; }

    /// <summary>配额类型</summary>
    public QuotaType Type { get; init; }  // User | Share | Group

    /// <summary>硬限制 (字节), null = 不限制</summary>
    public long? HardLimitBytes { get; init; }

    /// <summary>软限制 (字节), 超过后宽限期开始计时</summary>
    public long? SoftLimitBytes { get; init; }

    /// <summary>软限制宽限期 (秒), 默认 7 天</summary>
    public long GracePeriodSeconds { get; init; } = 604800;

    /// <summary>文件数硬限制, null = 不限制</summary>
    public long? HardLimitInodes { get; init; }

    /// <summary>当前使用量 (查询时填充)</summary>
    public long? UsedBytes { get; init; }
    public long? UsedInodes { get; init; }

    /// <summary>使用率百分比 (0-100)</summary>
    public double UsedPercent => HardLimitBytes.HasValue && HardLimitBytes.Value > 0
        ? (double)(UsedBytes ?? 0) / HardLimitBytes.Value * 100
        : 0;
}

public enum QuotaType { User, Share, Group }
```

**配额实现方式：**

| 文件系统 | 配额机制 | 命令 |
|----------|----------|------|
| **ext4/XFS** | Linux Quota | `xfs_quota -x -c 'limit bsoft=900G bhard=1T user1' /mnt` |
| **Btrfs** | qgroup | `btrfs qgroup limit 1T /mnt/nas/data/home/user1` |
| **ZFS** | ZFS Quota | `zfs set quota=1T pool-main/home/user1` |
| **Windows (NTFS)** | FSRM | `dirquota quota add /path:... /limit:1TB /type:hard` |

**配额告警阈值：**
- 80% → Info 级通知用户
- 90% → Warning 级通知用户 + 管理员
- 95% → Warning 级，开始拒绝新写入（软限制到达）
- 100% → Error 级，强制拒绝写入（硬限制到达）

#### 5.4.4 回收站机制

```
回收站配置 (类似 Synology #recycle):
─────────────────────────────────────
· 每共享独立启用/禁用
· 删除文件移动到 .recycle/{username}/ 而非直接删除
· 保留策略:
  - 按天数: 保留 30 天 → 自动清理
  - 按容量: 回收站 > 共享容量的 5% → 清理最旧文件
  - 按文件数: 超过 10000 个 → 清理最旧文件
· 排除规则: *.tmp, *.temp, ~$*, .DS_Store, Thumbs.db
· 通过 CLI 管理:
  gnas recycle list <share>         # 查看回收站内容
  gnas recycle restore <id>         # 恢复指定文件
  gnas recycle empty <share>        # 清空回收站
  gnas recycle config <share>       # 配置回收站策略
```

#### 5.4.5 NFS 配置细节

```yaml
# /srv/nas/config/services/nfs.yaml
nfs:
  # NFS 版本支持
  versions:
    - 3       # 兼容旧客户端
    - 4.0     # 状态协议
    - 4.1     # pNFS 并行访问
    - 4.2     # 服务端 Copy, Sparse Files
  
  # 并发设置
  nfsd_threads: 16                    # NFS 服务线程数
  nfsd_grace_period: 90               # 锁恢复宽限期(秒)
  
  # 导出定义
  exports:
    - path: /mnt/nas/data/media
      clients:
        - network: 192.168.1.0/24
          options:
            - ro                        # 只读
            - sync                      # 同步写入
            - no_subtree_check          # 不检查子树
            - all_squash                # 所有客户端映射为匿名用户
            - anonuid=1000
            - anongid=1000
        - network: 10.0.0.0/8
          options: [ro, sync, no_subtree_check]
    
    - path: /mnt/nas/data/documents
      clients:
        - network: 192.168.1.0/24
          options:
            - rw                        # 读写
            - async                     # 异步写入 (性能优先)
            - no_subtree_check
            - sec=krb5p                 # Kerberos 加密+完整性
```

### 5.5 数据保护与备份策略

#### 5.5.1 快照体系

```
快照层次:
─────────────────────────────────────────────
┌─────────────────────────────────────────────────────────┐
│  文件级快照 (Btrfs / ZFS / ReFS)                       │
│  · 即时创建, COW 机制, 仅存储差异数据                   │
│  · 用户可自助恢复 (类似 Windows 以前的版本)            │
│  · gnas snapshot create <dataset>                       │
│  · gnas snapshot list <dataset>                         │
│  · gnas snapshot restore <dataset> <snapshot-id>        │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  快照调度策略 (每数据集独立配置)                        │
│                                                         │
│  数据集       │ 频率         │ 保留           │ 说明    │
│  ────────────┼──────────────┼────────────────┼─────────│
│  documents   │ 每15分钟     │ 24h:48, 30d:30 │ 高频    │
│  media       │ 每天         │ 7d:7, 4w:4     │ 低频    │
│  photos      │ 每小时       │ 24h:24, 30d:30 │ 中频    │
│  appdata     │ 每6小时      │ 7d:28, 4w:4    │ 中低频  │
│  home/*      │ 每小时       │ 24h:24, 7d:7   │ 中频    │
│  downloads   │ 无快照       │ —              │ 不保留  │
│                                                         │
│  快照命名: gnas-{dataset}-{yyyyMMdd-HHmmss}             │
│  自动清理: 超过保留策略的旧快照自动删除                 │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  Windows 以前的版本 (Previous Versions) 集成            │
│                                                         │
│  Samba vfs_shadow_copy2 模块暴露快照为卷影副本:          │
│  vfs_objects = shadow_copy2                             │
│  shadow:snapdir = /mnt/nas/.snapshots/{dataset}         │
│  shadow:sort = desc                                     │
│  shadow:format = gnas-{dataset}-%Y%m%d-%H%M%S           │
│                                                         │
│  → Windows 资源管理器右键 → "还原以前的版本" 可用       │
│  → macOS Time Machine 可通过 SMB 共享作为备份目标       │
└─────────────────────────────────────────────────────────┘
```

#### 5.5.2 备份体系

```
┌─────────────────────────────────────────────────────────┐
│              备份三层架构                               │
│                                                         │
│  第一层: 本地快照 (即时恢复)                             │
│  ├── 优势: 秒级创建/恢复, 零额外存储                    │
│  └── 不足: 无法防御硬件故障/灾难                        │
│                                                         │
│  第二层: 本地备份 (快速恢复)                             │
│  ├── 目标: 外置 USB 硬盘 / 第二存储池 / 独立备份盘     │
│  ├── 工具: rsync / btrfs send / zfs send               │
│  ├── 频率: 每天 (增量备份) + 每周 (完整备份)            │
│  └── 保留: 14 天增量 + 4 周完整 + 12 月完整            │
│                                                         │
│  第三层: 异地/云备份 (灾难恢复)                         │
│  ├── 目标: S3 / Backblaze B2 / 远程 GNAS 节点           │
│  ├── 工具: rclone / restic / borgbackup                 │
│  ├── 加密: AES-256-GCM, 客户端加密后上传               │
│  ├── 频率: 每天 (加密增量)                             │
│  └── 保留: 7 天增量 + 4 周完整 + 12 月完整            │
└─────────────────────────────────────────────────────────┘

备份任务定义:
```csharp
public record BackupTask
{
    public string TaskId { get; init; }
    public string Name { get; init; }

    // 源与目标
    public string SourcePath { get; init; }        // /mnt/nas/data/documents
    public BackupTarget Target { get; init; }

    // 调度
    public string CronExpression { get; init; }    // "0 2 * * *" = 每天 02:00
    public bool Enabled { get; init; } = true;

    // 策略
    public BackupMethod Method { get; init; }      // Incremental | Full | Mirror
    public int RetentionDays { get; init; } = 30;
    public int RetentionCount { get; init; } = 10;
    public bool Compression { get; init; } = true;
    public bool Encryption { get; init; } = true;  // 异地备份强制加密

    // 排除
    public string[] ExcludePatterns { get; init; } // ["*.tmp", "Thumbs.db", "@eaDir"]
}

public record BackupTarget
{
    public BackupTargetType Type { get; init; }    // Local | RemoteNas | S3 | B2 | WebDAV
    public string ConnectionString { get; init; }  // 连接串或远程路径
    public string BucketOrPath { get; init; }      // 目标路径
    public string? AccessKey { get; init; }        // 加密存储, 不落盘明文
    public string? SecretKey { get; init; }        // 加密存储, 不落盘明文
}

public enum BackupTargetType { Local, RemoteNas, S3, B2, WebDAV, SFTP }
public enum BackupMethod { Incremental, Full, Mirror }
```

备份验证:
```
· 每次备份任务完成后 → 校验 checksum
· 每周 → 自动恢复测试 (恢复到临时目录 → 验证文件完整性)
· 验证失败 → Warning 告警
· 连续 3 次验证失败 → Critical 告警
```

#### 5.5.3 灾难恢复流程

```
┌─────────────────────────────────────────────────────────┐
│  全新系统恢复流程                                      │
│                                                         │
│  1. 安装 GNAS 系统 (ISO / 脚本)                         │
│  2. 运行恢复向导: gnas recovery start                   │
│  3. 选择恢复源:                                         │
│     · 本地备份盘 → 自动挂载 → 扫描备份目录              │
│     · 云存储 → 输入凭证 → 列出可用备份                  │
│     · 远程 GNAS → 输入地址 + 凭证                       │
│  4. 恢复系统配置: /srv/nas/config/ → 恢复 YAML + SQLite │
│  5. 恢复存储池配置: ZFS/btrfs 池定义 → 重新导入池       │
│     (ZFS: zpool import, Btrfs: 直接挂载)               │
│  6. 恢复数据: rsync / restic restore → 目标路径         │
│  7. 恢复 Agent: 重新生成 compose.yml + 启动容器         │
│  8. 验证恢复: 自动 checksum 校验 + 服务健康检查         │
│  9. 完成通知: "系统已在 {timestamp} 恢复到 {source}"   │
└─────────────────────────────────────────────────────────┘
```

---

## 6. 安全与身份认证层

本层借鉴鸿蒙系统（HarmonyOS）的分布式安全设计思想，不照搬原模型，而是针对 NAS 多用户、多设备、多 Agent 的场景做了深度适配。

### 6.1 鸿蒙概念 → NAS 适配映射

```
鸿蒙概念              →    GNAS 适配

Access Token          →    NasToken (JWT with embedded capabilities)
ATM (令牌管理器)       →    NasTokenManager (签发/验证/吊销/轮换)
Capability            →    NAbility (细粒度能力原子)
HUKS (密钥管理)        →    NasKeyStore (TPM + 软件回退)
Data Level (S0-S4)    →    NasDataLevel (文件/目录级数据分级)
Device Certification  →    DeviceTrust (设备信任链)
```

### 6.2 架构图

```
┌═══════════════════════════════════════════════════════════════┐
║           SECURITY & IDENTITY LAYER  安全与身份层              ║
║                                                                ║
║  ┌───────────────────────────────────────────────────────────┐ ║
║  │                  ┌──────────────────────┐                  │ ║
║  │                  │   Identity Service   │  身份服务         │ ║
║  │                  │   (身份即服务)        │                  │ ║
║  │                  └──────────┬───────────┘                  │ ║
║  │                             │                               │ ║
║  │    ┌────────────────────────┼────────────────────────┐     │ ║
║  │    │                        │                        │     │ ║
║  │    ▼                        ▼                        ▼     │ ║
║  │ ┌──────────┐    ┌──────────────────┐    ┌──────────────────┐│ ║
║  │ │ 本地身份  │    │   联合身份        │    │   设备/Agent身份  ││ ║
║  │ │          │    │                  │    │                  ││ ║
║  │ │· 用户名  │    │· LDAP/AD 域用户  │    │· Agent Token     ││ ║
║  │ │· 密码    │    │· OAuth2/OIDC     │    │· Device Cert     ││ ║
║  │ │· 生物特征│    │· 第三方登录      │    │· Service Account ││ ║
║  │ │· 二次验证│    │· SAML 企业SSO    │    │· API Key         ││ ║
║  │ └─────┬────┘    └────────┬─────────┘    └────────┬─────────┘│ ║
║  │       │                  │                       │          │ ║
║  │       └──────────────────┼───────────────────────┘          │ ║
║  │                          │                                   │ ║
║  │                          ▼                                   │ ║
║  │  ┌─────────────────────────────────────────────────────────┐│ ║
║  │  │            Access Token Manager (ATM)                    ││ ║
║  │  │             访问令牌管理器                                ││ ║
║  │  │                                                         ││ ║
║  │  │  · JWT 令牌签发/验证/吊销                                ││ ║
║  │  │  · 令牌内嵌能力 (Capability-Embedded Token)              ││ ║
║  │  │  · 令牌层级: 用户令牌 > 会话令牌 > 操作令牌              ││ ║
║  │  │  · Agent 令牌自动轮换                                    ││ ║
║  │  │  · 跨设备令牌同步 (多NAS集群场景)                        ││ ║
║  │  └──────────────────────────┬──────────────────────────────┘│ ║
║  │                             │                                │ ║
║  │                             ▼                                │ ║
║  │  ┌─────────────────────────────────────────────────────────┐│ ║
║  │  │            Permission Engine (权限引擎)                  ││ ║
║  │  │                                                         ││ ║
║  │  │   ┌─────────────┐  ┌─────────────┐  ┌──────────────┐   ││ ║
║  │  │   │ Capability  │  │   RBAC     │  │  ACL Engine  │   ││ ║
║  │  │   │  Engine     │  │  Engine    │  │  (文件级)    │   ││ ║
║  │  │   │ (能力模型)  │  │ (角色模型) │  │              │   ││ ║
║  │  │   └──────┬──────┘  └─────┬───────┘  └──────┬───────┘   ││ ║
║  │  │          └───────────────┼─────────────────┘            ││ ║
║  │  │                          │                              ││ ║
║  │  │                          ▼                              ││ ║
║  │  │              统一权限决策 (Policy Decision Point)       ││ ║
║  │  └─────────────────────────────────────────────────────────┘│ ║
║  │                                                                ║
║  │  ┌─────────────────────────────────────────────────────────┐  │ ║
║  │  │              NasKeyStore (密钥存储)                       │  │ ║
║  │  │  · TPM/Secure Enclave 集成    · 共享加密密钥管理         │  │ ║
║  │  │  · Agent Secret 安全注入      · TLS 证书管理             │  │ ║
║  │  └─────────────────────────────────────────────────────────┘  │ ║
║  └──────────────────────────────────────────────────────────────────┘ ║
╚═════════════════════════════════════════════════════════════════════════╝
```

### 6.3 NAbility 能力模型

```
能力命名规范: <domain>:<resource>:<action>[:<scope>]

示例层级:
  storage:*:*                    ← 存储完全控制 (Admin)
  storage:pool:main:*            ← 主存储池完全控制
  storage:pool:main:read         ← 主存储池只读
  storage:share:media:*          ← media共享完全控制
  storage:share:media:read       ← media共享只读
  storage:snapshot:*             ← 快照管理

  share:smb:*:*                  ← SMB服务完全控制
  share:smb:config:write         ← SMB配置修改
  share:nfs:export:read          ← NFS导出查看

  agent:*:*                      ← Agent完全控制 (Admin)
  agent:lifecycle:deploy          ← Agent部署权限
  agent:lifecycle:start_stop      ← Agent启停权限
  agent:token:issue               ← 签发Agent令牌
  agent:config:write              ← 修改Agent配置

  admin:user:*                    ← 用户管理
  admin:network:*                 ← 网络管理
  admin:audit:read                ← 审计日志查看

  data:level:public               ← 访问L0公开数据
  data:level:internal             ← 访问L1内部数据
  data:level:personal             ← 访问L2个人数据
  data:level:sensitive            ← 访问L3敏感数据
  data:level:system               ← 访问L4系统数据
```

### 6.4 NasToken 结构

```json
{
  "iss": "nas://home-nas.local",
  "sub": "user:alice | agent:openclaw | service:smb | device:livingroom-pc",
  "iat": 1750000000,
  "exp": 1750003600,
  "token_type": "access | session | agent | service",
  "trust_level": 3,
  "capabilities": [
    "storage:share:media:read",
    "storage:share:media:write",
    "share:smb:access",
    "data:level:internal"
  ],
  "delegation_chain": ["user:admin", "user:alice"],
  "device_binding": "device:nas-master-01",
  "jti": "unique-token-id"
}
```

### 6.5 NasDataLevel 数据分级

```
Level │ 名称       │ 标签色  │  权限规则
──────┼────────────┼────────┼────────────────────────────────
  L0  │ 公开数据    │ 绿色    │  匿名可读, 无需认证
  L1  │ 内部数据    │ 蓝色    │  任何已认证用户可读
  L2  │ 个人数据    │ 黄色    │  仅Owner + 显式授权者可访问
  L3  │ 敏感数据    │ 橙色    │  显式授权 + 操作审计 + 加密存储
  L4  │ 系统数据    │ 红色    │  仅Admin + 强制审计 + 硬件加密
```

### 6.6 Agent 授权流程

```
用户部署 OpenClaw Agent
        │
        ▼
  ┌─────────────────────────────────────────┐
  │  1. 用户指定 Agent 需要的能力:           │
  │     - storage:share:media:read          │
  │     - share:smb:access                  │
  │     - data:level:internal               │
  │     - agent:lifecycle:start_stop (自管) │
  └──────────────────┬──────────────────────┘
                     │
                     ▼
  ┌─────────────────────────────────────────┐
  │  2. ATM 签发 Agent Token:               │
  │     - token_type: "agent"               │
  │     - capabilities: [指定能力列表]       │
  │     - delegation_chain: [admin, alice]  │
  │     - exp: 24h (自动续期)               │
  │     - device_binding: nas-host-id       │
  └──────────────────┬──────────────────────┘
                     │
                     ▼
  ┌─────────────────────────────────────────┐
  │  3. Token 注入容器:                      │
  │     - 环境变量: NAS_TOKEN=<jwt>         │
  │     - 或 Secret 文件: /run/secrets/     │
  │     - API 端点: NAS_API_ENDPOINT        │
  └──────────────────┬──────────────────────┘
                     │
                     ▼
  ┌─────────────────────────────────────────┐
  │  4. Agent 运行时每次 API 调用携带 Token: │
  │     Header: Authorization: Bearer <jwt> │
  │     → Permission Engine 解析能力        │
  │     → 能力匹配? 放行 : 403              │
  └─────────────────────────────────────────┘
```

---

## 7. 服务总线容器层

本层是 GNAS 的核心创新——一个跨平台的应用级服务管理器，统一管理 NAS 中所有服务进程（原生服务 + Docker 容器）。

```
┌═══════════════════════════════════════════════════════════════┐
║              SERVICE BUS CONTAINER  服务总线容器               ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │            Service Registry (服务注册中心)                 │ ║
║  │  注册所有服务的元数据：名称/类型/版本/依赖/端口/健康检查    │ ║
║  └──────────────────────────────────────────────────────────┘ ║
║                                                                ║
║  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐          ║
║  │  Supervisor   │ │  IPC Bus     │ │  Health      │          ║
║  │  进程监管器   │ │  进程通信总线 │ │  健康监控    │          ║
║  │              │ │              │ │              │          ║
║  │ · 启动/停止  │ │ · 事件总线   │ │ · 心跳检测   │          ║
║  │ · 重启策略   │ │ · 命令通道   │ │ · 存活探针   │          ║
║  │ · 依赖排序   │ │ · 数据流     │ │ · 就绪探针   │          ║
║  │ · 优雅关闭   │ │ · 广播/单播  │ │ · 自动恢复   │          ║
║  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘          ║
║         │                │                │                  ║
║         └────────────────┼────────────────┘                  ║
║                          │                                   ║
║  ┌───────────────────────┴────────────────────────────────┐  ║
║  │                Service Hosts (服务宿主)                  │  ║
║  │                                                         │  ║
║  │  ┌──────────────────────┐  ┌──────────────────────────┐ │  ║
║  │  │  Native Service Host │  │  Container Service Host  │ │  ║
║  │  │  (原生进程宿主)       │  │  (容器化服务宿主)        │ │  ║
║  │  │                      │  │                          │ │  ║
║  │  │  · smb-daemon        │  │  · openclaw-agent        │ │  ║
║  │  │  · nfs-server        │  │  · home-assistant        │ │  ║
║  │  │  · ftp-server        │  │  · plex-media-server     │ │  ║
║  │  │  · nginx             │  │  · nextcloud             │ │  ║
║  │  │  · .NET Modules      │  │  · immich                │ │  ║
║  │  │                      │  │                          │ │  ║
║  │  │  管理方式:            │  │  管理方式:               │ │  ║
║  │  │  · 直接进程 fork     │  │  · docker compose        │ │  ║
║  │  │  · systemd (Linux)   │  │  · Docker API            │ │  ║
║  │  │  · SCM (Windows)     │  │  · containerd            │ │  ║
║  │  └──────────────────────┘  └──────────────────────────┘ │  ║
║  └─────────────────────────────────────────────────────────┘  ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │                Event Bus (事件总线)                        │ ║
║  │                                                           │ ║
║  │  Topic 示例:                                               │ ║
║  │  ┌──────────────────┐ ┌──────────────────┐                │ ║
║  │  │ storage.*        │ │ service.*        │                │ ║
║  │  │ · disk.added     │ │ · smb.started    │                │ ║
║  │  │ · disk.failed    │ │ · smb.stopped    │                │ ║
║  │  │ · pool.degraded  │ │ · nfs.exported   │                │ ║
║  │  │ · share.mounted  │ │ · config.changed │                │ ║
║  │  └──────────────────┘ └──────────────────┘                │ ║
║  │  ┌──────────────────┐                                     │ ║
║  │  │ agent.*          │                                     │ ║
║  │  │ · deployed       │                                     │ ║
║  │  │ · started        │                                     │ ║
║  │  │ · crashed        │                                     │ ║
║  │  │ · token.expiring │                                     │ ║
║  │  └──────────────────┘                                     │ ║
║  └──────────────────────────────────────────────────────────┘ ║
╚═══════════════════════════════════════════════════════════════╝
```

### 7.1 服务定义模型

```csharp
/// <summary>
/// 服务定义。描述一个受 Service Bus 管理的服务/进程/容器。
/// </summary>
public record ServiceDefinition
{
    /// <summary>服务唯一标识 (e.g. "smb-daemon")</summary>
    public string ServiceId { get; init; }

    /// <summary>服务显示名称 (e.g. "SMB/CIFS File Sharing")</summary>
    public string DisplayName { get; init; }

    /// <summary>服务类型</summary>
    public ServiceType Type { get; init; }  // Native | Container | Module

    /// <summary>依赖的其他服务ID列表</summary>
    public string[] DependsOn { get; init; }  // ["network", "storage-pool-main"]

    /// <summary>服务声明需要的能力</summary>
    public string[] RequiredCapabilities { get; init; }

    /// <summary>启动策略</summary>
    public ServiceStartup Startup { get; init; }  // Automatic | Manual | Disabled

    /// <summary>重启策略</summary>
    public RestartPolicy RestartPolicy { get; init; }  // Always | OnFailure | Never | ExponentialBackoff

    /// <summary>原生进程: 可执行文件路径</summary>
    public string Executable { get; init; }

    /// <summary>容器进程: compose.yml 路径</summary>
    public string ComposeFile { get; init; }

    /// <summary>健康检查配置</summary>
    public HealthCheckConfig HealthCheck { get; init; }

    /// <summary>资源配额</summary>
    public ResourceQuota Quota { get; init; }
}

public enum ServiceType
{
    Native,     // 原生操作系统进程
    Container,  // Docker 容器
    Module      // .NET Module (进程内)
}

public record HealthCheckConfig
{
    public HealthCheckType Type { get; init; }  // HttpGet | TcpConnect | ExecCommand | Grpc
    public string Endpoint { get; init; }       // 检查端点/命令
    public int IntervalSeconds { get; init; }   // 检查间隔
    public int TimeoutSeconds { get; init; }    // 超时时间
    public int Retries { get; init; }           // 失败重试次数
    public int StartPeriodSeconds { get; init; }// 启动宽限期
}

public record ResourceQuota
{
    public double? CpuLimit { get; init; }     // CPU 核数上限
    public long? MemoryLimitBytes { get; init; }// 内存上限
    public int? IoWeight { get; init; }         // IO 权重 (1-1000)
}
```

### 7.2 服务间通信模式

```
服务间通信有两种模式:

1. 事件总线 (发布/订阅):
   ┌─────────────────┐                    ┌─────────────────┐
   │  SMB Service    │                    │  Permission     │
   │  (Native)       │                    │  Engine         │
   │                 │                    │  (.NET Module)  │
   └────────┬────────┘                    └────────┬────────┘
            │                                      │
            │  ┌──────────────────────────────┐    │
            │  │         Event Bus             │    │
            └──┤  · file.access.requested      ├────┘
               │  · file.access.granted        │
               │  · file.access.denied         │
               └──────────────────────────────┘

2. gRPC (请求/响应):
   ┌─────────────────┐                    ┌─────────────────┐
   │  Agent Manager  │                    │  Docker         │
   │  (.NET Module)  │                    │  Engine         │
   └────────┬────────┘                    └────────┬────────┘
            │                                      │
            │  ┌──────────────────────────────┐    │
            │  │      gRPC (IPC Channel)       │    │
            └──┤  · DeployContainer(req)       ├────┘
               │  · GetContainerStatus(id)      │
               │  · StreamContainerLogs(id)     │
               └──────────────────────────────┘
```

---

## 8. Docker/Agent 集成层

```
┌═══════════════════════════════════════════════════════════════┐
║        DOCKER & AGENT INTEGRATION LAYER  容器与代理集成层      ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │              Agent Manager (代理管理器)                    │ ║
║  │                                                           │ ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐   │ ║
║  │  │ Agent Catalog│  │ Token Broker │  │ Compose        │   │ ║
║  │  │ 代理目录     │  │ 令牌经纪人   │  │ Generator      │   │ ║
║  │  │              │  │              │  │ 编排生成器     │   │ ║
║  │  │ · 可用Agent  │  │ · 签发Agent  │  │                │   │ ║
║  │  │   模板市场   │  │   Token      │  │ · 生成compose  │   │ ║
║  │  │ · 版本管理   │  │ · 能力范围   │  │ · 自动挂载Vol  │   │ ║
║  │  │ · 依赖检查   │  │ · 时效控制   │  │ · 网络策略注入 │   │ ║
║  │  │ · 评分/评论  │  │ · 吊销/续期  │  │ · 环境变量注入 │   │ ║
║  │  └──────┬───────┘  └──────┬───────┘  └────────┬───────┘   │ ║
║  └─────────┼─────────────────┼───────────────────┼────────────┘ ║
║            │                 │                   │               ║
║            ▼                 ▼                   ▼               ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │               Agent Runtime Environment                   │ ║
║  │                                                           │ ║
║  │   ┌─────────────────────────────────────────────────┐    │ ║
║  │   │           Docker Engine (官方, 未修改)            │    │ ║
║  │   │   Storage Driver: overlay2 | btrfs | zfs         │    │ ║
║  │   │   Network: bridge | host | macvlan | ipvlan      │    │ ║
║  │   └─────────────────────────────────────────────────┘    │ ║
║  │                                                           │ ║
║  │   ┌──────────┐  ┌───────────┐  ┌──────────┐             │ ║
║  │   │ OpenClaw │  │ Home      │  │ Custom   │             │ ║
║  │   │ Agent    │  │ Assistant │  │ Agent    │             │ ║
║  │   ├──────────┤  ├───────────┤  ├──────────┤             │ ║
║  │   │ NAS_TOKEN│  │ NAS_TOKEN │  │ NAS_TOKEN│             │ ║
║  │   │ NAS_API  │  │ NAS_API   │  │ NAS_API  │             │ ║
║  │   │ Vol:     │  │ Vol:      │  │ Vol:     │             │ ║
║  │   │  /data   │  │  /config  │  │  ...     │             │ ║
║  │   └──────────┘  └───────────┘  └──────────┘             │ ║
║  └──────────────────────────────────────────────────────────┘ ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │              Agent ↔ NAS 交互通道                         │ ║
║  │                                                           │ ║
║  │  ┌──────────────────┐  ┌──────────────────┐              │ ║
║  │  │ Filesystem I/O   │  │  NAS REST/gRPC   │              │ ║
║  │  │ (Volume Mount)   │  │  API             │              │ ║
║  │  │                  │  │                  │              │ ║
║  │  │ · 直接文件读写   │  │ · 调用NAS服务    │              │ ║
║  │  │ · POSIX 权限     │  │ · 查询系统状态   │              │ ║
║  │  │ · 共享文件夹访问 │  │ · 管理共享/用户  │              │ ║
║  │  │ · 快照数据访问   │  │ · 触发备份任务   │              │ ║
║  │  └──────────────────┘  └──────────────────┘              │ ║
║  │                                                           │ ║
║  │  ┌──────────────────┐                                    │ ║
║  │  │  Polling / Query  │                                    │ ║
║  │  │  (REST API)       │                                    │ ║
║  │  │                  │                                    │ ║
║  │  │ · 查询系统状态   │                                    │ ║
║  │  │ · 拉取日志       │                                    │ ║
║  │  │ · 定时轮询       │                                    │ ║
║  │  └──────────────────┘                                    │ ║
║  └──────────────────────────────────────────────────────────┘ ║
╚═══════════════════════════════════════════════════════════════╝
```

### 8.1 Agent 部署流程（端到端）

```
用户浏览 Agent Catalog
        │
        ▼
  ┌──────────────────────────────────────────────────┐
  │  选择 OpenClaw Agent, 配置:                      │
  │  · 可访问的共享文件夹: media, documents           │
  │  · 需要的API能力: storage:read, share:access     │
  │  · 网络模式: bridge, 端口: 8080                  │
  │  · 资源限制: CPU 1核, 内存 512MB                 │
  └──────────────────────┬───────────────────────────┘
                         │
                         ▼
  ┌──────────────────────────────────────────────────┐
  │  Agent Manager 编排:                             │
  │                                                  │
  │  1. 生成 Agent Token (NAS Token)                 │
  │  2. 生成 docker-compose.yml:                     │
  │                                                  │
  │     services:                                    │
  │       openclaw:                                  │
  │         image: openclaw/agent:latest             │
  │         environment:                             │
  │           - NAS_TOKEN=eyJhbGc...                 │
  │           - NAS_API_ENDPOINT=http://host:5000    │
  │           - AGENT_CAPABILITIES=storage:share:... │
  │         volumes:                                 │
  │           - /mnt/nas/media:/data/media:ro        │
  │           - /mnt/nas/documents:/data/docs:rw     │
  │           - agent_openclaw_config:/config         │
  │         networks:                                │
  │           - nas_bridge                           │
  │         deploy:                                  │
  │           resources:                             │
  │             limits: {cpus: '1', memory: 512M}    │
  │                                                  │
  │  3. 写入 /srv/nas/agents/openclaw/              │
  │  4. 注册到 Service Bus                           │
  │  5. docker compose up -d                         │
  └──────────────────────┬───────────────────────────┘
                         │
                         ▼
  ┌──────────────────────────────────────────────────┐
  │  Service Bus 接管 Agent 生命周期:                 │
  │  · 监控容器状态 (Docker events + Healthcheck)    │
  │  · Token 到期前自动续期                           │
  │  · 崩溃自动重启 (RestartPolicy)                   │
  │  · 资源使用监控 & 告警                            │
  └──────────────────────────────────────────────────┘
```

---

## 9. 平台抽象层

```
┌═══════════════════════════════════════════════════════════┐
║      PLATFORM ABSTRACTION LAYER  平台抽象层 (.NET)         ║
║                                                            ║
║  ┌──────────────────────────────────────────────────────┐ ║
║  │                 Interface Definitions                 │ ║
║  │                                                       │ ║
║  │  IProcessManager     IFileSystem       INetworkManager│ ║
║  │  ┌──────────────┐   ┌──────────────┐  ┌─────────────┐│ ║
║  │  │ StartProc()  │   │ Mount()      │  │ ConfigIf()  ││ ║
║  │  │ StopProc()   │   │ Unmount()    │  │ SetFw()     ││ ║
║  │  │ RestartProc()│   │ Format()     │  │ DHCP/DNS()  ││ ║
║  │  │ ListProc()   │   │ GetInfo()    │  │             ││ ║
║  │  └──────────────┘   └──────────────┘  └─────────────┘│ ║
║  │                                                       │ ║
║  │  IDiskManager       IUserAccount       IServiceCtrl   │ ║
║  │  ┌──────────────┐   ┌──────────────┐   ┌───────────┐ │ ║
║  │  │ ListDisks()  │   │ CreateUser() │   │ Install() │ │ ║
║  │  │ Partition()  │   │ DeleteUser() │   │ Uninstall()│ │ ║
║  │  │ CreateRaid() │   │ SetGroup()   │   │ Enable()  │ │ ║
║  │  │ SmartCheck() │   │ SetPerm()    │   │ Disable() │ │ ║
║  │  └──────────────┘   └──────────────┘   └───────────┘ │ ║
║  └──────────────────────────────────────────────────────┘ ║
║                                                            ║
║  ┌────────────────────┬────────────────────┬─────────────┐║
║  │  Linux Impl        │  Windows Impl      │  ARM Impl   │║
║  │  (linux-x64)       │  (win-x64)         │  (arm64)    │║
║  ├────────────────────┼────────────────────┼─────────────┤║
║  │  · systemd         │  · SCM (服务控制)   │  · systemd  │║
║  │  · udev (设备)     │  · WMI (设备枚举)   │  · udev     │║
║  │  · /proc, /sys     │  · diskpart        │  · /proc    │║
║  │  · mdadm, LVM      │  · Storage Spaces  │  · mdadm    │║
║  │  · iptables/nft    │  · Windows Firewall│  · nft      │║
║  │  · samba, nfs-     │  · SMB Server      │  · samba    │║
║  │    kernel-server   │    (Win内置)        │  · nfs      │║
║  │  · Docker CE       │  · Docker Desktop  │  · Docker CE│║
║  │  · ext4/XFS/Btrfs  │    / Podman        │  · ext4/XFS │║
║  │                    │  · NTFS/ReFS       │            │║
║  └────────────────────┴────────────────────┴─────────────┘║
║                                                            ║
║  ┌──────────────────────────────────────────────────────┐ ║
║  │          .NET Runtime Identifiers (RID)               │ ║
║  │                                                       │ ║
║  │  Target Frameworks: net9.0                            │ ║
║  │  RIDs: linux-x64 | linux-arm64 | win-x64 | win-arm64 │ ║
║  │                                                       │ ║
║  │  通过依赖注入 (DI) 自动选择平台实现:                   │ ║
║  │                                                       │ ║
║  │  if (OperatingSystem.IsLinux())                       │ ║
║  │      services.Add<IDiskManager, LinuxDiskManager>();  │ ║
║  │  else if (OperatingSystem.IsWindows())                │ ║
║  │      services.Add<IDiskManager, WindowsDiskManager>();│ ║
║  │                                                       │ ║
║  │  if (RuntimeInformation.ProcessArchitecture ==        │ ║
║  │      Architecture.Arm64)                              │ ║
║  │      services.Add<IHardwareOptimizer, ArmOptimizer>();│ ║
║  └──────────────────────────────────────────────────────┘ ║
╚═══════════════════════════════════════════════════════════╝
```

### 9.1 平台接口定义

```csharp
/// <summary>
/// 磁盘管理抽象。
/// </summary>
public interface IDiskManager
{
    Task<IReadOnlyList<DiskInfo>> ListDisksAsync(CancellationToken ct);
    Task<PartitionResult> CreatePartitionAsync(string diskPath, PartitionSpec spec, CancellationToken ct);
    Task<RaidResult> CreateRaidAsync(RaidLevel level, string[] diskPaths, CancellationToken ct);
    Task<SmartData> GetSmartDataAsync(string diskPath, CancellationToken ct);
    Task WipeDiskAsync(string diskPath, CancellationToken ct);
}

/// <summary>
/// 文件系统抽象。
/// </summary>
public interface IFileSystem
{
    Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct);
    Task UnmountAsync(string mountPoint, CancellationToken ct);
    Task FormatAsync(string device, string fsType, CancellationToken ct);
    Task<FsInfo> GetFilesystemInfoAsync(string mountPoint, CancellationToken ct);
}

/// <summary>
/// 进程/服务管理抽象。
/// </summary>
public interface IProcessManager
{
    Task<int> StartProcessAsync(ProcessStartConfig config, CancellationToken ct);
    Task StopProcessAsync(int pid, CancellationToken ct);
    Task RestartProcessAsync(int pid, CancellationToken ct);
    Task<IReadOnlyList<ProcessInfo>> ListProcessesAsync(CancellationToken ct);
    Task EnableServiceAsync(string serviceName, CancellationToken ct);
    Task DisableServiceAsync(string serviceName, CancellationToken ct);
}

/// <summary>
/// 网络管理抽象。
/// </summary>
public interface INetworkManager
{
    Task<IReadOnlyList<NetInterface>> ListInterfacesAsync(CancellationToken ct);
    Task ConfigureInterfaceAsync(string name, NetConfig config, CancellationToken ct);
    Task AddFirewallRuleAsync(FirewallRule rule, CancellationToken ct);
    Task RemoveFirewallRuleAsync(string ruleId, CancellationToken ct);
}

/// <summary>
/// 用户账户抽象。
/// </summary>
public interface IUserAccount
{
    Task CreateUserAsync(string username, string password, CancellationToken ct);
    Task DeleteUserAsync(string username, CancellationToken ct);
    Task AddUserToGroupAsync(string username, string group, CancellationToken ct);
    Task SetFilePermissionsAsync(string path, UnixFileMode mode, CancellationToken ct);
}
```

---

## 10. 日志与可观测性层

### 10.1 日志体系全景

```
┌═══════════════════════════════════════════════════════════════┐
║            OBSERVABILITY & LOGGING LAYER  可观测性与日志层      ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │                  日志产生端 (Log Producers)                │ ║
║  │                                                           │ ║
║  │  ┌───────────┐  ┌───────────┐  ┌──────────┐             │ ║
║  │  │ CLI Tool │  │ .NET API  │  │ Modules  │             │ ║
║  │  │ (客户端)  │  │ (网关日志) │  │ (业务日志)│             │ ║
║  │  └─────┬─────┘  └─────┬─────┘  └────┬─────┘             │ ║
║  │        │              │             │                    │ ║
║  │  ┌─────┴──────────────┴─────────────┴────────────────┐   │ ║
║  │  │             Agent 容器日志 (stdout/stderr)         │   │ ║
║  │  └───────────────────────────────────────────────────┘   │ ║
║  └──────────────────────────────┬───────────────────────────┘ ║
║                                 │                              ║
║  ┌──────────────────────────────┴───────────────────────────┐ ║
║  │              日志采集管道 (Collection Pipeline)           │ ║
║  │                                                           │ ║
║  │  ┌────────────────────┐    ┌────────────────────┐        │ ║
║  │  │  Logging Provider   │    │  OpenTelemetry SDK │        │ ║
║  │  │  (.NET ILogger)     │    │  (Traces + Metrics │        │ ║
║  │  │                     │    │   + Logs)          │        │ ║
║  │  │  · ConsoleProvider  │    │                    │        │ ║
║  │  │  · FileProvider     │    │  · ActivitySource  │        │ ║
║  │  │  · StructuredLog    │    │  · Meter           │        │ ║
║  │  │  · AuditLogProvider │    │  · Logger          │        │ ║
║  │  └─────────┬───────────┘    └─────────┬──────────┘        │ ║
║  │            │                          │                    │ ║
║  └────────────┼──────────────────────────┼────────────────────┘ ║
║               │                          │                       ║
║  ┌────────────┴──────────────────────────┴────────────────────┐ ║
║  │               日志分类器 (Log Classifier)                    │ ║
║  │                                                             │ ║
║  │   System Log  ────→ 系统运行日志 (INFO/WARN/ERROR)         │ ║
║  │   Audit Log   ────→ 审计日志 (安全事件, 不可篡改)          │ ║
║  │   Access Log  ────→ 访问日志 (文件访问, API调用)           │ ║
║  │   Agent Log   ────→ Agent运行日志 (容器stdout/状态)        │ ║
║  │   Trace Log   ────→ 分布式链路追踪 (跨服务调用链)          │ ║
║  │   Metric Log  ────→ 指标数据 (CPU/内存/磁盘/网络)          │ ║
║  └───────────────────────────────┬─────────────────────────────┘ ║
║                                  │                               ║
║  ┌───────────────────────────────┴─────────────────────────────┐ ║
║  │                日志存储引擎 (Storage Engines)                │ ║
║  │                                                              │ ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │ ║
║  │  │  File Store  │  │  SQLite/     │  │  Loki (内嵌) │       │ ║
║  │  │  (轮转文件)  │  │  PostgreSQL  │  │              │       │ ║
║  │  │ /var/log/nas │  │  (结构化查询)│  │ 容器日志     │       │ ║
║  │  │ system/*.log │  │              │  │ 聚合搜索     │       │ ║
║  │  │ agent/*.log  │  │ metrics.db   │  │ 标签索引     │       │ ║
║  │  │              │  │ audit.db     │  │              │       │ ║
║  │  └──────────────┘  └──────────────┘  └──────────────┘       │ ║
║  │                                                              │ ║
║  │  ┌──────────────────────┐                                    │ ║
║  │  │  Audit Vault (防篡改) │                                   │ ║
║  │  │  · 审计链存储        │                                    │ ║
║  │  │  · 完整性校验        │                                    │ ║
║  │  └──────────────────────┘                                    │ ║
║  └──────────────────────────────────────────────────────────────┘ ║
║                                                                   ║
║  ┌──────────────────────────────────────────────────────────────┐ ║
║  │                日志服务层 (Log Services)                      │ ║
║  │                                                              │ ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │ ║
║  │  │ Log Query    │  │ Alert Engine │  │ Retention    │       │ ║
║  │  │ Service      │  │ 告警引擎     │  │ Manager      │       │ ║
║  │  │              │  │              │  │ 保留策略     │       │ ║
║  │  │ · 全文搜索   │  │ · 规则评估   │  │ · 自动归档   │       │ ║
║  │  │ · 时间范围   │  │ · 阈值触发   │  │ · 自动删除   │       │ ║
║  │  │ · 标签过滤   │  │ · 聚合告警   │  │ · 存储配额   │       │ ║
║  │  │ · 关联查询   │  │ · 静默规则   │  │              │       │ ║
║  │  └──────────────┘  └──────────────┘  └──────────────┘       │ ║
║  └──────────────────────────────────────────────────────────────┘ ║
║                                                                   ║
║  ┌──────────────────────────────────────────────────────────────┐ ║
║  │                展示层 (Visualization)                         │ ║
║  │                                                              │ ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │ ║
║  │  │ Log Viewer   │  │ Dashboard    │  │ Alert        │       │ ║
║  │  │ (实时+历史)  │  │ (系统+Agent) │  │ Notifications│       │ ║
║  │  └──────────────┘  └──────────────┘  └──────────────┘       │ ║
║  └──────────────────────────────────────────────────────────────┘ ║
╚══════════════════════════════════════════════════════════════════╝
```

### 10.2 统一日志结构

```csharp
/// <summary>
/// 统一日志条目，所有类型的日志都使用此结构。
/// </summary>
public record LogEntry
{
    // === 基础字段 ===
    public string LogId { get; init; }             // UUID v7 (时间有序)
    public DateTimeOffset Timestamp { get; init; }
    public LogCategory Category { get; init; }     // System | Audit | Access | Agent | Trace | Metric
    public LogLevel Level { get; init; }           // Trace | Debug | Info | Warn | Error | Fatal

    // === 来源标识 ===
    public string SourceComponent { get; init; }   // "StorageModule", "SmbService", "OpenClawAgent"
    public string SourceLayer { get; init; }       // "API", "ServiceBus", "Module", "Agent", "OS"
    public string HostName { get; init; }
    public string HostArch { get; init; }          // x64, arm64

    // === 业务上下文 ===
    public string UserId { get; init; }            // 关联用户 (audit/access 必须)
    public string AgentId { get; init; }           // 关联 Agent
    public string ServiceId { get; init; }         // 关联服务
    public string TraceId { get; init; }           // 分布式追踪
    public string SpanId { get; init; }            // 调用跨度

    // === 内容 ===
    public string Message { get; init; }
    public string Template { get; init; }          // "User {UserId} accessed {FilePath}"
    public Dictionary<string, object> Properties { get; init; }
    public string[] Tags { get; init; }            // ["security", "permission-denied"]

    // === 审计专用 ===
    public AuditDetail Audit { get; init; }

    // === 指标专用 ===
    public MetricData Metric { get; init; }
}

/// <summary>
/// 审计日志扩展：记录每次权限决策和敏感操作。
/// </summary>
public record AuditDetail
{
    public string Action { get; init; }            // "file.read", "user.create", "agent.deploy"
    public string Resource { get; init; }          // "/mnt/nas/media/movie.mkv"
    public string ResourceType { get; init; }      // "file", "user", "share", "config", "agent"
    public string PermissionRequired { get; init; }// "storage:share:media:read"
    public bool Granted { get; init; }
    public string ClientIp { get; init; }
    public string UserAgent { get; init; }
    public string SessionId { get; init; }
    public string BeforeState { get; init; }       // 变更前状态 (JSON)
    public string AfterState { get; init; }        // 变更后状态 (JSON)

    // === 防篡改 (审计链) ===
    public string PreviousHash { get; init; }      // 前一条审计日志的 SHA-256
    public string CurrentHash { get; init; }       // 本条日志的 SHA-256
    public string ChainSignature { get; init; }    // HMAC-SHA256(CurrentHash, ChainKey)
}

/// <summary>
/// 指标日志扩展。
/// </summary>
public record MetricData
{
    public string MetricName { get; init; }        // "cpu.usage", "disk.iops", "memory.available"
    public double Value { get; init; }
    public string Unit { get; init; }              // "percent", "bytes", "iops", "mbps"
    public Dictionary<string, string> Dimensions { get; init; } // {"disk":"sda", "pool":"main"}
}

/// <summary>
/// 日志类别枚举。
/// </summary>
public enum LogCategory
{
    System,    // 系统运行日志
    Audit,     // 审计日志 (不可篡改)
    Access,    // 访问日志 (文件+API)
    Agent,     // Agent/容器运行日志
    Trace,     // 分布式链路追踪
    Metric     // 指标数据
}
```

### 10.3 审计防篡改链

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Audit Chain (审计链)                              │
│                                                                          │
│   Log #1              Log #2              Log #3              Log #4     │
│  ┌──────────┐        ┌──────────┐        ┌──────────┐        ┌──────────┐│
│  │PrevHash: │        │PrevHash: │        │PrevHash: │        │PrevHash: ││
│  │NULL      │   ┌───►│HASH(#1)  │   ┌───►│HASH(#2)  │   ┌───►│HASH(#3)  ││
│  │Content:  │   │    │Content:  │   │    │Content:  │   │    │Content:  ││
│  │user:admin│   │    │action:   │   │    │action:   │   │    │action:   ││
│  │login     │   │    │agent.    │   │    │file.read │   │    │config.   ││
│  │success   │   │    │deploy    │   │    │denied    │   │    │change    ││
│  │CurrHash: │   │    │CurrHash: │   │    │CurrHash: │   │    │CurrHash: ││
│  │= H(#1)   │───┘    │= H(#2)   │───┘    │= H(#3)   │───┘    │= H(#4)   ││
│  └──────────┘        └──────────┘        └──────────┘        └──────────┘│
│                                                                          │
│  Hash = SHA-256(PrevHash + Timestamp + Action + Resource + UserId        │
│                 + Result + AfterState)                                   │
│                                                                          │
│  ChainSignature = HMAC-SHA256(CurrentHash, NasKeyStore.ChainKey)         │
│                                                                          │
│  验证规则:                                                                │
│  1. 遍历链, 验证每个 CurrentHash = H(PrevHash + Content...)              │
│  2. 验证 ChainSignature 匹配                                             │
│  3. 任一环节断裂 → 告警 "审计日志可能被篡改"                              │
│  4. 审计链定期导出到外部存储 (云/外置硬盘)                                 │
└─────────────────────────────────────────────────────────────────────────┘
```

### 10.4 告警引擎

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Alert Engine (告警引擎)                         │
│                                                                          │
│  告警分级:                                                                │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ Critical (严重) — 磁盘故障/RAID降级/审计链断裂/入侵检测            │ │
│  │  通知: 邮件 + Webhook + 系统告警                                    │ │
│  │                                                                     │ │
│  │ Warning (警告) — 磁盘>90%/高内存/Token即将过期/多次登录失败         │ │
│  │  通知: 邮件 + UI 提示                                               │ │
│  │                                                                     │ │
│  │ Info (信息) — 服务重启/Agent部署/存储池扩容/OTA更新                 │ │
│  │  通知: UI 事件流 + 日志记录                                         │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                                                          │
│  静默规则:                                                                │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ · 维护模式: 手动进入维护模式时, 抑制所有非Critical告警              │ │
│  │ · 时间窗口: 备份窗口 (02:00-06:00) 抑制IO相关告警                  │ │
│  │ · 依赖抑制: 网络不可达时, 抑制下游服务的连接失败告警                │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

### 10.5 日志保留策略

```
日志类型      │ 存储位置    │  热存储  │  温存储  │  冷存储  │  说明
─────────────┼────────────┼─────────┼─────────┼─────────┼───────────
System       │ 文件+Loki  │  7 天   │  30 天  │  删除   │  自动轮转
Audit        │ Audit Vault│  90 天  │  1 年   │ 永久(外部)│ 不可删除
Access       │ SQLite/DB  │  30 天  │  90 天  │ 180天   │  可配置
Agent        │ Loki       │  7 天   │  30 天  │  删除   │  按Agent分
Trace        │ Loki       │  3 天   │  7 天   │  删除   │  采样存储
Metric       │ SQLite/DB  │  30 天  │  90 天  │ 365天   │  降采样

存储配额:
· 默认总配额: NAS总容量的 2% (最小 5GB, 最大 50GB)
· 审计日志: 不纳入配额管控, 独立存储池
· 达到 80% 配额 → Info 告警
· 达到 95% 配额 → Warning 告警 + 自动清理最老的冷数据
· 达到 100% 配额 → Critical 告警 + 强制清理

指标降采样:
原始 (10s) → 1min 聚合 → 5min 聚合 → 1h 聚合 → 1d 聚合
保留 7 天      30 天       90 天       365 天    永久
```

### 10.6 Agent 日志采集链路

```
  ┌───────────────────────────────────────────────────────────────┐
  │  OpenClaw Agent Container                                     │
  │  stdout ──► 应用日志                                          │
  │  stderr ──► 错误日志                                          │
  │  /var/log/agent/ ──► 结构化日志文件 (JSON Lines)              │
  │  NAS API ──► 通过 API 上报关键事件                             │
  └────────────────────────────┬──────────────────────────────────┘
                               │
                    Docker Log Driver (json-file)
                               │
                               ▼
  ┌───────────────────────────────────────────────────────────────┐
  │  Agent Log Collector (.NET BackgroundService)                  │
  │  1. Docker Events 监听: container start/stop/die/health       │
  │  2. Docker Logs API 拉取: docker logs --since {timestamp}    │
  │  3. Volume Mount 读取: /mnt/nas/agents/{agent}/logs/*.jsonl  │
  │  4. NAS API 接收: Agent 通过 /api/agent/logs 推送            │
  └────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
  ┌───────────────────────────────────────────────────────────────┐
  │  Log Pipeline:                                                 │
  │  Parse (JSON→Obj) → Enrich (+AgentId,+TraceId)                 │
  │  → Classify (System/Agent/Audit)                               │
  │  → Dispatch (File/Loki/DB/Vault)                               │
  └───────────────────────────────────────────────────────────────┘
```

---

## 11. 数据流图

### 11.1 用户操作的完整数据流

```
┌──────────┐    HTTPS     ┌──────────────┐    gRPC     ┌──────────────┐
│ CLI Tool │ ◄──────────► │  API Gateway │ ◄─────────► │  .NET Module │
│ (gnas)   │              │  (Kestrel)   │             │  (例如Agent) │
└──────────┘              └──────┬───────┘             └──────┬───────┘
                                 │                            │
                          ┌──────┴────────────┐      ┌────────┴────────┐
                          │  NasToken 验证    │      │  业务逻辑处理   │
                          │  NAbility 权限校验 │      │  · Token生成    │
                          └───────────────────┘      │  · Compose生成  │
                                                     └────────┬────────┘
                                                              │
                                              ┌───────────────┼───────────┐
                                              │               │           │
                                        ┌─────┴─────┐  ┌──────┴──────┐ ┌─┴──────────┐
                                        │ 存储配置  │  │ Service Bus │ │ Docker API  │
                                        │ SQLite/DB │  │ 注册服务    │ │ compose up  │
                                        └───────────┘  └──────┬──────┘ └──────┬──────┘
                                                              │               │
                                                              ▼               ▼
                                                       ┌──────────┐  ┌────────────┐
                                                       │ 服务监控  │  │ 容器运行    │
                                                       │ 健康检查  │  │ OpenClaw   │
                                                       └──────────┘  └─────┬──────┘
                                                                           │
                                                                     ┌─────┴──────┐
                                                                     │ NAS API    │
                                                                     │ (Agent调用)│
                                                                     └────────────┘
```

### 11.2 Agent ↔ NAS 交互数据流

```
                      ┌─────────────────┐
                      │   OpenClaw Agent │
                      │   Container      │
                      └────────┬────────┘
                               │
              ┌────────────────┼────────────────┐
              │                │
     Volume Mount        NAS API (REST/gRPC)
     (文件I/O)           │
              │                │
              ▼                ▼
      ┌───────────┐    ┌───────────────┐
      │ /mnt/nas/ │    │ NAS API       │
      │  media/   │    │ · 查询共享    │
      │  documents│    │ · 读取配置    │
      │           │    │ · 触发备份   │
      │ Permission│    │ Permission    │
      │ Check:    │    │ Check:        │
      │ POSIX ACL │    │ NasToken      │
      └───────────┘    └───────────────┘
```

---

## 12. 技术选型总览

| 层次 | 技术 | 说明 |
|------|------|------|
| **交互层** | .NET 9 Console Application (gnas CLI) | 跨平台命令行工具, 支持 TUI + 批处理 |
| **API Gateway** | ASP.NET Core Minimal API + gRPC | REST 对外 (CLI/第三方), gRPC 对内 |
| **内嵌 Dashboard** | 纯静态 HTML + Vanilla JS (可选) | 只读监控面板, 无框架依赖 |
| **内部 IPC** | gRPC + Unix Sockets / Named Pipes | 服务间高性能通信 |
| **业务模块** | .NET 9 Class Libraries + AssemblyLoadContext | 模块热加载, 沙箱隔离 |
| **配置存储** | SQLite (默认) / PostgreSQL (集群) | 轻量但功能完整 |
| **声明式配置** | YAML + JSON Schema | 替代传统 XML+SaltStack |
| **容器运行时** | Docker Engine (官方, 未修改) | 社区标准, 避免厂商锁定 |
| **容器编排** | docker compose (文件生成) | 每 Agent 一个 compose.yml |
| **安全令牌** | JWT + NAbility (内嵌能力) | 鸿蒙启发的 NAS 安全模型 |
| **密钥存储** | TPM 2.0 (优先) / 软件 KeyStore (回退) | 硬件安全模块集成 |
| **文件级权限** | POSIX ACL / Windows ACL (适配层) | 继承原有系统权限 |
| **数据分级** | NasDataLevel (L0-L4) | 文件/目录级标签 |
| **日志 SDK** | `Microsoft.Extensions.Logging` + Serilog | .NET 标准日志基础设施 |
| **链路追踪** | OpenTelemetry (.NET SDK) | OTLP 协议, 行业标准 |
| **日志存储** | 文件轮转 + 内嵌 Loki + SQLite + Audit Vault | 按日志类型分流存储 |
| **审计防篡改** | 自研 Audit Chain (SHA-256 + HMAC) | 区块链思想, 零外部依赖 |
| **告警通知** | SMTP + Webhook + CLI 输出 | 多渠道通知 |
| **跨平台** | .NET 9 RID 多目标编译 | linux-x64, win-x64, linux-arm64 |
| **进程管理** | 自研 Service Bus + systemd/SCM 适配 | 跨平台统一服务管理 |

---

## 13. 部署架构

### 13.1 目录结构

```
/srv/nas/                          # NAS 数据根目录 (可配置)
├── config/
│   ├── nas.yaml                   # 主配置文件
│   ├── modules/                   # 模块配置
│   │   ├── storage.yaml
│   │   ├── share.yaml
│   │   └── agent.yaml
│   ├── services/                  # 服务定义
│   │   ├── smb.yaml
│   │   ├── nfs.yaml
│   │   └── openclaw.yaml
│   └── alerts/                    # 告警规则
│       ├── disk.yaml
│       └── agent.yaml
├── agents/                        # Agent 部署目录
│   ├── openclaw/
│   │   ├── docker-compose.yml     # 生成的编排文件
│   │   ├── token.env              # Agent Token (600权限)
│   │   └── data/                  # Agent 持久化数据
│   ├── home-assistant/
│   │   ├── docker-compose.yml
│   │   └── data/
│   └── catalog/                   # Agent 模板目录
│       ├── openclaw.template.yaml
│       └── plex.template.yaml
├── data/                          # NAS 共享数据根
│   ├── media/                     # 媒体文件
│   ├── documents/                 # 文档
│   └── backups/                   # 备份
├── logs/                          # 日志存储
│   ├── system/                    # 系统日志 (轮转)
│   ├── audit/                     # 审计日志 (防篡改链)
│   ├── access/                    # 访问日志 (SQLite)
│   └── agents/                    # Agent 日志 (Loki)
├── database/
│   ├── nas.db                     # 主配置数据库 (SQLite)
│   ├── metrics.db                 # 指标时序数据库
│   └── access.db                  # 访问日志数据库
└── keystore/
    ├── chain.key                  # 审计链密钥
    ├── tls/                       # TLS 证书
    └── agent-secrets/             # Agent 密钥 (加密存储)
```

### 13.2 Docker Compose (自部署参考)

```yaml
# docker-compose.yml — GNAS 自身也可以容器化部署
version: '3.8'

services:
  gnas-core:
    image: gnas/core:latest
    container_name: gnas-core
    restart: unless-stopped
    network_mode: host
    privileged: true              # 需要访问硬件和 Docker socket
    volumes:
      - /srv/nas:/srv/nas
      - /var/run/docker.sock:/var/run/docker.sock
      - /proc:/host/proc:ro
      - /sys:/host/sys:ro
      - /dev:/host/dev
    environment:
      - GNAS_CONFIG_PATH=/srv/nas/config/nas.yaml
      - GNAS_DATA_ROOT=/srv/nas/data
      - ASPNETCORE_URLS=http://0.0.0.0:5000
      - ASPNETCORE_ENVIRONMENT=Production

  gnas-loki:
    image: grafana/loki:latest
    container_name: gnas-loki
    restart: unless-stopped
    volumes:
      - /srv/nas/logs/loki:/loki
    command: -config.file=/etc/loki/local-config.yaml
```

---

---

## 14. 系统安装与初始化引导

### 14.1 安装方式

| 方式 | 适用场景 | 说明 |
|------|----------|------|
| **ISO 镜像安装** | 裸机安装 | 基于 Debian/Ubuntu Live ISO 定制，包含 GNAS 全部依赖 |
| **脚本安装** | 已有 Linux 系统 | `curl -fsSL https://get.gnas.io | bash` 一键安装 |
| **Docker 自部署** | 开发/测试/轻量部署 | 容器化运行 GNAS Core，挂载 Docker Socket |
| **Windows 安装包** | Windows 平台 | MSI 安装包，注册为 Windows Service |

```
安装方式决策树:
─────────────────
  是否裸机?
    ├── 是 → ISO 镜像安装 (推荐)
    │        · 下载 gnas-{version}-{arch}.iso
    │        · 使用 balenaEtcher/Rufus 制作启动盘
    │        · 从 U 盘启动 → 进入安装向导
    │
    └── 否 → 是否已有 Docker 环境?
              ├── 是 → Docker Compose 部署 (最简单)
              │        · wget https://get.gnas.io/docker-compose.yml
              │        · docker compose up -d
              │
              └── 否 → 脚本安装
                       · curl -fsSL https://get.gnas.io | bash
                       · 自动检测平台 → 安装 .NET Runtime + 依赖 → 部署 GNAS
```

### 14.2 ISO 安装流程

```
┌─────────────────────────────────────────────────────────┐
│  GNAS Installer (TUI 安装向导)                         │
│                                                         │
│  Step 1: 语言与时区选择                                 │
│  ┌───────────────────────────────────────────────────┐ │
│  │  Language: [English ▾]                            │ │
│  │  Timezone: [Asia/Shanghai ▾]                      │ │
│  │  Keyboard: [US English ▾]                         │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Step 2: 网络配置                                      │
│  ┌───────────────────────────────────────────────────┐ │
│  │  Interface: eth0 [✓] Connected                    │ │
│  │  IP Config:  (●) DHCP  ( ) Static                 │ │
│  │  Hostname:   [gnas-nas_____________]              │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Step 3: 选择系统盘                                     │
│  ┌───────────────────────────────────────────────────┐ │
│  │  [sda] Samsung SSD 256GB — (●) 系统盘             │ │
│  │  [sdb] WD Red 4TB       — ( ) 数据盘             │ │
│  │  [sdc] WD Red 4TB       — ( ) 数据盘             │ │
│  │                                                    │ │
│  │  ⚠ 系统盘将被格式化，所有数据将丢失               │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Step 4: 创建管理员账户                                 │
│  ┌───────────────────────────────────────────────────┐ │
│  │  Username:     [admin____________]                │ │
│  │  Password:     [****************]                 │ │
│  │  Confirm:      [****************]                 │ │
│  │  Email:        [admin@example.com]                │ │
│  │                                                    │ │
│  │  Password strength: ████████████ Strong           │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Step 5: 确认安装                                       │
│  ┌───────────────────────────────────────────────────┐ │
│  │  系统盘:    /dev/sda (Samsung SSD 256GB)          │ │
│  │  主机名:    gnas-nas                              │ │
│  │  管理员:    admin                                 │ │
│  │  时区:      Asia/Shanghai                         │ │
│  │                                                    │ │
│  │  安装后请访问 http://gnas-nas:5000                 │ │
│  │                                                    │ │
│  │  [ 开始安装 ]  [ 返回修改 ]                        │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 14.3 首次启动向导 (Onboarding Wizard)

系统安装完成后的首次启动引导，CLI 和 内嵌 Dashboard 均可完成：

```
首次启动后 CLI 自动进入引导模式:

┌─────────────────────────────────────────────────────────┐
│  Welcome to GNAS v1.0.0!                                │
│                                                         │
│  It looks like this is your first time running GNAS.    │
│  Let's set up your NAS system.                          │
│                                                         │
│  向导流程:                                              │
│  ┌───────────────────────────────────────────────────┐ │
│  │ 1. 网络初始化          [✓] 已完成                 │ │
│  │ 2. 存储池创建          [→] 进行中                 │ │
│  │ 3. 共享文件夹创建      [ ] 待处理                 │ │
│  │ 4. 用户账户创建        [ ] 待处理                 │ │
│  │ 5. 基础服务启用        [ ] 待处理                 │ │
│  │ 6. 完成                [ ] 待处理                 │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘

Step 2 — 存储池创建向导:
─────────────────────────────

$ gnas setup pool create

  发现以下可用磁盘:

  [1] /dev/sdb  WD Red 4TB     (空闲)    ● SATA
  [2] /dev/sdc  WD Red 4TB     (空闲)    ● SATA
  [3] /dev/sdd  Seagate IronWolf 8TB (空闲) ● SATA

  请选择要使用的磁盘 (用逗号分隔, 如 "1,2,3"): 1,2

  已选择 2 个磁盘。推荐 RAID 级别: RAID 1 (镜像)

  请选择 RAID 级别:
  [1] RAID 1 (推荐) — 4TB 可用, 1 盘容错
  [2] RAID 0 — 8TB 可用, 无容错

  请选择: 1

  请选择文件系统:
  [1] Btrfs (推荐) — CoW, 压缩, 快照
  [2] ext4 — 传统稳定
  [3] XFS — 大文件优化

  请选择: 1

  创建中... [████████████████████] 100%
  存储池 "pool-main" 创建成功

Step 3 — 创建默认共享文件夹:
─────────────────────────────

  为存储池 "pool-main" 创建推荐的共享文件夹结构?

  pool-main/
  ├── data/media/        媒体库
  ├── data/documents/    文档
  ├── data/downloads/    下载
  ├── backup/            备份目标
  ├── appdata/           Agent 数据
  └── home/              用户目录

  创建? [Y/n]: Y
  创建完成 ✓

Step 4 — 可选: 创建其他用户账户:
────────────────────────────────

  是否创建其他用户? [y/N]: n

Step 5 — 启用默认服务:
───────────────────────

  以下服务将在系统启动时自动运行:

  [✓] SMB/CIFS 文件共享 (端口 445)
  [✓] NFS 文件共享 (端口 2049)
  [ ] FTP 文件共享 (端口 21)
  [ ] WebDAV (端口 8080)
  [ ] Agent 市场 (Docker 容器支持)

  按 Enter 确认或修改选择。

Step 6 — 完成:
────────────────

  ┌─────────────────────────────────────────────────────┐
  │  GNAS 初始化完成!                                   │
  │                                                     │
  │  系统信息:                                          │
  │    主机名:   gnas-nas                               │
  │    地址:     http://gnas-nas:5000 (管理 API)        │
  │    Dashboard: http://gnas-nas:5000/dashboard         │
  │    存储池:   pool-main (4TB, RAID 1, Btrfs)         │
  │    共享:     media, documents, downloads             │
  │                                                     │
  │  运行 'gnas' 进入交互式 TUI 管理模式                │
  │  运行 'gnas help' 查看所有可用命令                  │
  └─────────────────────────────────────────────────────┘
```

### 14.4 配置初始化详情

首次启动时，GNAS 自动生成初始配置：

```yaml
# /srv/nas/config/nas.yaml — 自动生成的主配置文件
nas:
  hostname: gnas-nas
  version: "1.0.0"
  data_root: /srv/nas/data
  timezone: Asia/Shanghai
  language: zh-CN

api:
  host: "0.0.0.0"
  port: 5000
  grpc_port: 5001
  tls:
    enabled: true
    cert_path: /srv/nas/keystore/tls/server.crt
    key_path: /srv/nas/keystore/tls/server.key
    # 首次启动自动生成自签名证书

storage:
  pools:
    - id: pool-main
      level: raid1
      filesystem: btrfs
      disks: [/dev/sdb, /dev/sdc]
      datasets:
        - path: data/media
          compression: false
          snapshots: {schedule: "0 3 * * *", retain: 7}
          data_level: L1
        - path: data/documents
          compression: true
          snapshots: {schedule: "*/15 * * * *", retain: 48}
          data_level: L2
        - path: data/downloads
          compression: false
          snapshots: false
          data_level: L1
        - path: backup
          compression: true
          snapshots: false
          data_level: L2
        - path: appdata
          compression: false
          snapshots: {schedule: "0 */6 * * *", retain: 28}
          data_level: L3
        - path: home
          compression: true
          snapshots: {schedule: "0 * * * *", retain: 24}
          data_level: L2

security:
  password_policy:
    min_length: 8
    require_uppercase: true
    require_lowercase: true
    require_digit: true
    require_special: false
    max_failed_attempts: 5
    lockout_minutes: 15
  session:
    token_lifetime_hours: 24
    refresh_lifetime_days: 7

services:
  autostart:
    - smb-daemon
    - nfs-server
    - smart-monitor

logging:
  retention:
    system_days: 30
    audit_days: 365
    access_days: 90
    agent_days: 30
    metric_days: 365
  quota:
    max_percent: 2
    min_gb: 5
    max_gb: 50

dashboard:
  enabled: true

alerts:
  email:
    enabled: false
    smtp_server: ""
    smtp_port: 587
  webhook:
    enabled: false
    url: ""
```

---

## 15. UPS 集成 (不间断电源)

NAS 系统必须支持 UPS 以防止意外断电导致数据损坏。

### 15.1 NUT (Network UPS Tools) 集成

```
┌─────────────────────────────────────────────────────────┐
│  UPS 集成架构                                          │
│                                                         │
│  ┌──────────┐     USB/Serial     ┌──────────────┐      │
│  │  UPS 设备 │ ◄───────────────► │  GNAS NAS     │      │
│  │  (APC/   │                    │  (NUT Client  │      │
│  │  Eaton/  │                    │   + Server)   │      │
│  │  Cyber-  │                    │               │      │
│  │  Power)  │                    │  · upsd       │      │
│  └──────────┘                    │  · upsmon     │      │
│                                  └──────┬─────────┘      │
│                                         │                │
│                           ┌─────────────┴──────────┐    │
│                           │                        │    │
│                     ┌─────┴─────┐          ┌──────┴──┐ │
│                     │ 从属 NAS  │          │ 其他设备 │ │
│                     │ (NUT Client)│         │ (NUT     │ │
│                     │           │          │  Client) │ │
│                     └───────────┘          └─────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 15.2 断电处理策略

```
┌─────────────────────────────────────────────────────────┐
│  UPS 事件处理流程                                      │
│                                                         │
│  市电断电 (ONBATT)                                      │
│        │                                                │
│        ▼                                                │
│  ┌──────────────────────────────────────────────┐      │
│  │ 阶段 1: 即时响应 (0 秒)                      │      │
│  │ · 发布 system.power.onbattery 事件           │      │
│  │ · 所有服务收到通知: 准备降级/暂停非关键任务  │      │
│  │ · Dashboard / TUI 显示电池状态              │      │
│  └──────────────────┬───────────────────────────┘      │
│                     │                                   │
│        ┌────────────┴────────────┐                      │
│        │                         │                      │
│  电池 > 50%                  电池 < 50%                  │
│        │                         │                      │
│        ▼                         ▼                      │
│  继续运行                  ┌──────────────────────┐    │
│  发布定期状态              │ 阶段 2: 安全模式     │    │
│  (每30秒)                  │ · 暂停非必要服务     │    │
│                            │ · 停止文件索引       │    │
│                            │ · 停止 Scrub         │    │
│                            │ · 停止备份任务       │    │
│                            │ · 强制 sync 文件系统 │    │
│                            │ · Warning 告警       │    │
│                            └──────────┬───────────┘    │
│                                       │                │
│                                 电池 < 20%              │
│                                       │                │
│                                       ▼                │
│                            ┌──────────────────────┐    │
│                            │ 阶段 3: 准备关机     │    │
│                            │ · 停止所有 Agent 容器│    │
│                            │ · 卸载文件共享 (SMB) │    │
│                            │ · 停止所有非核心服务 │    │
│                            │ · 写入审计日志       │    │
│                            │ · sync + 卸载文件系统│    │
│                            │ · Critical 告警      │    │
│                            └──────────┬───────────┘    │
│                                       │                │
│                                 电池 < 5% 或 2分钟     │
│                                       │                │
│                                       ▼                │
│                            ┌──────────────────────┐    │
│                            │ 阶段 4: 紧急关机     │    │
│                            │ · systemctl poweroff │    │
│                            │   (或 shutdown /s)   │    │
│                            │ · 等待 UPS 电量耗尽  │    │
│                            └──────────────────────┘    │
│                                                         │
│  电力恢复 (ONLINE)                                      │
│        │                                                │
│        ▼                                                │
│  ┌──────────────────────────────────────────────┐      │
│  │ 恢复处理                                     │      │
│  │ · 发布 system.power.online 事件              │      │
│  │ · 如果系统已关机: BIOS 设置 "Restore on AC" │      │
│  │ · 启动后自动: 文件系统检查 → 服务恢复        │      │
│  │ · Info 级通知: "电力已恢复"                  │      │
│  └──────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────┘
```

### 15.3 UPS 配置

```yaml
# /srv/nas/config/ups.yaml
ups:
  enabled: true
  driver: usbhid-ups              # NUT driver name
  device: /dev/usb/hiddev0
  
  # 电池阈值
  battery:
    warning_level: 50             # 低电量警告 (%)
    safe_mode_level: 50           # 进入安全模式 (%)
    shutdown_level: 20            # 准备关机 (%)
    emergency_level: 5            # 紧急关机 (%)
    
  # 关机前延迟 (给从属设备时间)
  shutdown_delay_seconds: 120
  
  # 通知
  notify_on_events:
    - onbatt                       # 切换到电池供电
    - lowbatt                      # 电池低电量
    - online                       # 电力恢复
    
  # 从属设备 (可选)
  slaves:
    - hostname: gnas-backup
      port: 3493
    - network: 192.168.1.0/24
      port: 3493
```

### 15.4 CLI 命令

```bash
gnas ups status                  # UPS 状态 (电量/负载/剩余时间)
gnas ups list                    # 列出连接的 UPS 设备
gnas ups test                    # 触发 UPS 自检
gnas ups config set             # 配置 UPS 参数
```

---

## 16. 安全增强设计

### 16.1 暴力破解防护 (Fail2Ban 集成)

```yaml
# /srv/nas/config/security/fail2ban.yaml
brute_force_protection:
  enabled: true
  
  jails:
    - name: api-auth
      filter: "Failed login attempt from <HOST>"
      source: /srv/nas/logs/access/auth.log
      max_retries: 5
      find_time_seconds: 300      # 5 分钟内
      ban_time_seconds: 900       # 封禁 15 分钟
      
    - name: smb-auth
      filter: "NT_STATUS_WRONG_PASSWORD from <HOST>"
      source: /srv/nas/logs/system/smb-auth.log
      max_retries: 3
      find_time_seconds: 60
      ban_time_seconds: 1800      # 封禁 30 分钟
      
    - name: ssh-brute
      filter: "Failed password for .* from <HOST>"
      source: /var/log/auth.log
      max_retries: 5
      find_time_seconds: 300
      ban_time_seconds: 3600      # 封禁 60 分钟

  # 封禁升级策略
  recidive:
    enabled: true
    watch_jail: api-auth
    max_retries: 3                # 被封禁 3 次后
    ban_time_seconds: 86400       # 封禁 24 小时
    
  # 白名单
  whitelist:
    - 127.0.0.1
    - 192.168.1.0/24              # 内网不封禁
    - 10.0.0.0/8
```

### 16.2 API 速率限制策略

```yaml
# /srv/nas/config/security/ratelimit.yaml
rate_limit:
  default:
    requests_per_minute: 100
    burst: 20
    
  endpoints:
    - path: /api/auth/login
      requests_per_minute: 5
      burst: 2
      message: "登录过于频繁,请稍后再试"
      
    - path: /api/auth/*
      requests_per_minute: 10
      burst: 5
      
    - path: /api/logs/*
      requests_per_minute: 60
      burst: 10
      
    - path: /api/agents/*/start
      requests_per_minute: 10
      burst: 3
      
    - path: /api/agents/*/stop
      requests_per_minute: 10
      burst: 3

  scope: per_ip                    # per_ip | per_token | per_user
  
  # 分布式速率限制 (多节点场景)
  distributed:
    enabled: false
    redis: "redis://localhost:6379"
```

---

## 与 OMV 原架构的对比

| 维度 | OMV 原架构 | GNAS 新架构 |
|------|-----------|-------------|
| **后端语言** | PHP | .NET 9 (C#) |
| **Web 服务** | Nginx + PHP-FPM | Kestrel (ASP.NET Core 内建) |
| **配置存储** | XML (config.xml) | SQLite + YAML 声明式配置 |
| **配置管理** | SaltStack (masterless) | 自研 Service Bus (事件驱动) |
| **客户端** | Web UI (Angular/ExtJS) | Desktop CLI Tool (gnas) + 可选 Web Dashboard |
| **Docker** | 无原生支持 (OMV-Extras 插件) | 一等公民, 深度集成 |
| **Agent** | 无 | Agent Catalog + Token + Compose |
| **跨平台** | Debian Only | Linux / Windows / ARM |
| **权限模型** | 传统 Linux ACL | NAbility 能力模型 + RBAC + ACL |
| **服务管理** | systemd (Linux only) | Service Bus (跨平台) |
| **IPC** | 文件/socket (隐式) | gRPC + Event Bus (显式) |
| **日志** | syslog (分散) | 统一六类日志 + 审计链 |
| **可观测性** | 无 | OpenTelemetry + Loki + Dashboard |
| **安装方式** | ISO 固化 | ISO + 脚本 + Docker 多方式 |
| **UPS 支持** | 无原生支持 | NUT 深度集成 + 分级断电策略 |
| **审计** | syslog | 防篡改审计链 |
| **快照备份** | 无 | Btrfs/ZFS 快照 + 三层备份体系 |

---

## 架构决策记录 (ADR — Architecture Decision Records)

### ADR-001: 选择 .NET 9 而非 Go/Rust

| 项 | 内容 |
|----|------|
| **状态** | ✅ 已决定 |
| **背景** | NAS 系统涉及大量系统调用、文件操作、网络协议处理 |
| **决策** | 使用 .NET 9 (C# 13) |
| **理由** | 1. 跨平台成熟度高 (linux-x64/arm64/win-x64) 2. ASP.NET Core 提供完整的 API/中间件生态 3. gRPC 原生支持 4. 作者团队技术栈以 .NET 为主 5. 热加载 (AssemblyLoadContext) 支持模块化 |
| **替代方案** | Go (并发优秀但泛型生态弱), Rust (性能极致但开发效率低) |

### ADR-002: 默认存储使用 SQLite 而非 PostgreSQL

| 项 | 内容 |
|----|------|
| **状态** | ✅ 已决定 |
| **背景** | NAS 系统需要一个嵌入式配置存储 |
| **决策** | SQLite 作为默认存储，PostgreSQL 作为集群模式可选替代 |
| **理由** | 1. 零运维 (无需独立数据库进程) 2. 数据量小 (配置+审计约几百 MB) 3. 单文件备份/恢复简单 4. 支持 JSON 查询 5. NAS 通常单节点运行 |
| **折衷** | 多节点集群时切换到 PostgreSQL |

### ADR-003: 默认文件系统选择 Btrfs 而非 ZFS

| 项 | 内容 |
|----|------|
| **状态** | ✅ 已决定 |
| **背景** | CoW 文件系统对快照/压缩/自愈至关重要 |
| **决策** | Btrfs 作为默认推荐，ZFS 作为高级备选 |
| **理由** | 1. Btrfs 内置于 Linux 主线内核 (无 DKMS) 2. 更灵活的磁盘添加/移除 3. 内存占用更低 4. RAID 5/6 已基本稳定 (kernel 5.15+) |
| **折衷** | ZFS 提供更成熟的数据完整性，但需要 DKMS 且内存开销大。高级用户可通过 `gnas pool create --fs zfs` 选择 |

### ADR-004: Docker Compose 而非 Kubernetes

| 项 | 内容 |
|----|------|
| **状态** | ✅ 已决定 |
| **背景** | 需要容器编排来管理 Agent |
| **决策** | 使用 docker compose (每 Agent 一个 compose 文件) |
| **理由** | 1. NAS 单节点场景不需要 K8s 的复杂性 2. TrueNAS SCALE 从 K8s 迁移到 Compose 的教训 3. Docker Compose 是社区标准 4. 声明式 + 易于生成和修改 |
| **折衷** | 不支持多节点 Agent 编排，但 NAS 场景下这不是核心需求 |

### ADR-005: CLI 作为唯一管理界面

| 项 | 内容 |
|----|------|
| **状态** | ✅ 已决定 |
| **背景** | NAS 管理界面的选择 |
| **决策** | Desktop CLI Tool 为主要管理界面，Web Dashboard 仅作为只读监控面板 |
| **理由** | 1. CLI 对所有操作脚本化友好 2. 管道优先设计兼容 Unix 哲学 3. TUI 提供足够的交互体验 4. 减少 Web 安全攻击面 5. 避免维护复杂 Web 前端 |
| **折衷** | 提供可选 Web Dashboard 用于非技术用户的只看不操作场景 |

### ADR-006: 审计链采用自研轻量实现而非区块链

| 项 | 内容 |
|----|------|
| **状态** | ✅ 已决定 |
| **背景** | 审计日志需要防篡改 |
| **决策** | 自研 SHA-256 链式哈希 + HMAC 签名 |
| **理由** | 1. 区块链引入不必要的复杂度和依赖 2. 单节点 NAS 不需要分布式共识 3. SHA-256 + HMAC 对于防篡改足够 4. 定期导出到外部存储作为额外保险 |
| **折衷** | 不提供多节点审计共识，高级安全需求可对接外部 SIEM |

### ADR-007: NAbility 能力模型借鉴鸿蒙思想

| 项 | 内容 |
|----|------|
| **状态** | ✅ 已决定 |
| **背景** | NAS 需要细粒度权限控制 |
| **决策** | 设计 `domain:resource:action:scope` 四级能力命名体系 |
| **理由** | 1. 比 RBAC 更细粒度 (精确到具体的共享文件夹和操作) 2. 比纯 ACL 可读性好 (结构化字符串) 3. 通配符匹配支持灵活的授权层级 4. 与 NasToken 内嵌能力绑定，实现自包含鉴权 |

---

## 变更记录 (Changelog)

| 版本 | 日期 | 变更内容 |
|------|------|---------|
| **v2.0** | 2026-07-25 | 新增: §5.3 存储池管理详细设计、§5.4 共享协议详细设计、§5.5 数据保护与备份策略、§4.3 gRPC 服务定义(Proto)、§14 系统安装与初始化引导、§15 UPS 集成、§16 安全增强设计、ADR 架构决策记录 |
| **v1.0** | 2026-07-24 | 初始版本: 13 个章节的完整架构设计 |

---

> **文档版本**: Architecture v2.0  
> **更新日期**: 2026-07-25  
> **关联文档**: [GNAS Implementation Prompts](gnas-implementation-prompts.md)

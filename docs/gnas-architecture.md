# GNAS — New Generation Linux NAS System Technical Architecture

> **Project Codename**: GNAS  
> **Version**: Architecture v2.1  
> **Last Updated**: 2026-07-25  
> **Tech Stack**: .NET 10 + Docker + OpenTelemetry

---

## Table of Contents

1. [Design Goals and Core Principles](#1-design-goals-and-core-principles)
2. [Overall Layered Architecture](#2-overall-layered-architecture)
3. [Presentation Layer — Desktop CLI Tool](#3-presentation-layer)
4. [API Gateway Layer](#4-api-gateway-layer)
   - 4.3 [gRPC Service Definitions (Proto Contracts)](#43-grpc-service-definitions-proto-contracts)
5. [Application Layer — .NET Modules](#5-application-layer)
   - 5.3 [Storage Pool Management Detailed Design](#53-storage-pool-management-detailed-design)
   - 5.4 [Share Protocol Detailed Design](#54-share-protocol-detailed-design)
   - 5.5 [Data Protection and Backup Strategy](#55-data-protection-and-backup-strategy)
6. [Security and Identity Layer](#6-security-and-identity-layer)
7. [Service Bus Container Layer](#7-service-bus-container-layer)
8. [Docker/Agent Integration Layer](#8-dockeragent-integration-layer)
9. [Platform Abstraction Layer](#9-platform-abstraction-layer)
10. [Logging and Observability Layer](#10-logging-and-observability-layer)
11. [Data Flow Diagrams](#11-data-flow-diagrams)
12. [Technology Selection Overview](#12-technology-selection-overview)
13. [Deployment Architecture](#13-deployment-architecture)
14. [System Installation and Initialization Guide](#14-system-installation-and-initialization-guide)
15. [UPS Integration (Uninterruptible Power Supply)](#15-ups-integration-uninterruptible-power-supply)
16. [Security Enhancement Design](#16-security-enhancement-design)
17. [Comparison with OMV Original Architecture](#comparison-with-omv-original-architecture)
18. [Architecture Decision Records (ADR)](#architecture-decision-records-adr)
19. [Changelog](#changelog)

---

## 1. Design Goals and Core Principles

### 1.1 Design Goals

| Goal | Description |
|------|------|
| **Linux Platform** | Supports Linux x64, Linux ARM64; official distribution based on Debian 12 |
| **Docker Native Integration** | Agents/applications deployed as containers, deeply interacting with the NAS system |
| **Security First** | Draws inspiration from HarmonyOS distributed security concepts, adapted for NAS multi-user scenarios |
| **Unified Service Management** | One container manages all services (native processes + Docker containers) |
| **.NET Full Stack** | Full system uses .NET technology stack, CLI as the only interaction interface |

### 1.2 Core Principles

```
Principle                          Implementation
───────                            ──────────────

1. Multi-Architecture Linux       Platform Abstraction Layer + .NET RID multi-target build
   (Linux x64/ARM64)

2. Deep Agent Integration         Agent Catalog → Token Broker → Compose Generator
   (Docker + NAS Token + Volume)   → Service Bus unified lifecycle management

3. Security = Capability +        NasToken + NAbility + NasDataLevel
   Identity + Data Level          Three independent but interconnected
   (HarmonyOS concept, NAS adapted)

4. Unified Service Management      Service Bus Container
   (Native Process + Docker)       Manages both smb-daemon and openclaw-agent

5. .NET Full Stack                 ASP.NET Core (API) + CLI (Interaction)
   (Linux + High Performance)       + gRPC (IPC) + Built-in Web Dashboard

6. Comprehensive Observability     Six log categories + Immutable audit chain + Full trace
   (Observability)
```

### 1.3 Brand NAS Architecture Reference

This architecture comprehensively references Docker design approaches from four major NAS brands:

| Brand | Reference Points |
|------|--------|
| **Unraid** | Official Docker (no modifications), community template mechanism, fully CLI-friendly |
| **TrueNAS SCALE** | ZFS Dataset-level storage isolation, K8s→Compose architecture lessons |
| **Synology DSM** | Deep ACL integration, automatic permission mapping |
| **QNAP** | Multi-runtime architecture concept (Docker + LXD + Kata) |

---

## 2. Overall Layered Architecture

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
║  │  API GATEWAY       RESTful API │ gRPC (Internal IPC)           │    ║
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
║  │  ABSTRACTION          Linux-x64 │ Linux-arm64                  │    ║
║  └────────────────────────────┬─────────────────────────────────────┘    ║
║                               │                                          ║
║  ┌────────────────────────────┴─────────────────────────────────────┐    ║
║  │  OPERATING SYSTEM    Debian 12 │ Compatible Linux │ ARM Linux   │    ║
║  └──────────────────────────────────────────────────────────────────┘    ║
║                                                                            ║
╠═══════════════════════════════════════════════════════════════════════════╣
║                    CROSS-CUTTING CONCERNS — THROUGH ALL LAYERS           ║
║                                                                            ║
║  ╔══════════════════════════════════════════════════════════════════════╗ ║
║  ║               OBSERVABILITY & LOGGING                               ║ ║
║  ║                                                                      ║ ║
║  ║  Producers ─→ Pipeline ─→ Classifier ─→ Storage ─→ Query ─→ Alert   ║ ║
║  ║                                                                      ║ ║
║  ║  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌───────┐ ║ ║
║  ║  │ System   │  │ Audit    │  │ Access   │  │ Agent    │  │Metric │ ║ ║
║  ║  │ Log      │  │ Chain    │  │ Log      │  │ Log      │  │Log    │ ║ ║
║  ║  │ File +   │  │ Vault    │  │ SQLite   │  │ Loki     │  │TSDB   │ ║ ║
║  ║  │ Loki     │  │ (Tamper  │  │          │  │          │  │       │ ║ ║
║  ║  │          │  │  Proof)  │  │          │  │          │  │       │ ║ ║
║  ║  └──────────┘  └──────────┘  └──────────┘  └──────────┘  └───────┘ ║ ║
║  ║                                                                      ║ ║
║  ║  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌─────────────────────┐ ║ ║
║  ║  │ Log      │  │ Alert    │  │ Retention│  │ Dashboard           │ ║ ║
║  ║  │ Viewer   │  │ Engine   │  │ Manager  │  │ (Grafana Style)     │ ║ ║
║  ║  └──────────┘  └──────────┘  └──────────┘  └─────────────────────┘ ║ ║
║  ╚══════════════════════════════════════════════════════════════════════╝ ║
║                                                                            ║
║  ╔══════════════════════════════════════════════════════════════════════╗ ║
║  ║               TRACE PROPAGATION                                     ║ ║
║  ║  CLI/API → API GW → Module → Service Bus → Native/Container Service  ║ ║
║  ║  (Same TraceId propagated across the entire chain)                   ║ ║
║  ╚══════════════════════════════════════════════════════════════════════╝ ║
║                                                                            ║
║  ╔══════════════════════════════════════════════════════════════════════╗ ║
║  ║               SECURITY AUDIT CROSS-CUTTING                           ║ ║
║  ║  Permission decisions, data access, config changes → forced write to ║ ║
║  ║  Audit Log → immutable chain                                         ║ ║
║  ╚══════════════════════════════════════════════════════════════════════╝ ║
╚═══════════════════════════════════════════════════════════════════════════╝
```

---

## 3. Presentation Layer — Desktop CLI Tool

GNAS's presentation layer includes the Linux command-line tool `gnas`; all management operations are performed through the CLI.

```
┌──────────────────────────────────────────────────────────────┐
│                   PRESENTATION LAYER                         │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │               Desktop CLI Tool (gnas)                   │ │
│  │                                                        │ │
│  │  · Linux Console Application (.NET 10)                  │ │
│  │  · Communicates with NAS backend via REST API          │ │
│  │  · Interactive TUI mode (Terminal UI) + Batch mode     │ │
│  │  · Pipeline-friendly (JSON / Table output)             │ │
│  │  · Local config file management (~/.gnas/config)       │ │
│  └────────────────────────┬───────────────────────────────┘ │
│                           │                                  │
│  ┌────────────────────────┴───────────────────────────────┐ │
│  │  Communication Protocol: HTTPS REST API (JSON)         │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 3.1 Interactive TUI Mode

Running `gnas` without subcommands enters the interactive terminal UI:

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
│  │  10:23  [WARN]  Disk sda usage 92%                  │  │
│  │  09:15  [INFO]  Agent openclaw deployed             │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  Press F1-Help F2-Services F3-Logs F4-Agents F10-Quit   │
└──────────────────────────────────────────────────────────┘
```

### 3.2 Batch Mode

```bash
# Query
gnas status                    # System status overview
gnas disk list                 # Disk list
gnas share list                # Shared folder list
gnas service list              # Service list
gnas agent list                # Agent list

# Operations
gnas share create media /mnt/nas/data/media
gnas service restart smb-daemon
gnas agent deploy openclaw --capabilities "storage:share:media:read"
gnas agent start openclaw

# Logs and monitoring
gnas log view --follow --category agent --agent openclaw
gnas log query --level error --last 1h
gnas audit verify              # Verify audit chain integrity
gnas alert list --severity warning

# Output format
gnas disk list --output json   # JSON output (pipeline-friendly)
gnas disk list --output table  # Table output (default)
gnas disk list --no-color      # Disable ANSI colors
```

### 3.3 Built-in Web Dashboard (Optional)

CLI is the primary interaction method. For scenarios requiring graphical monitoring, GNAS can optionally enable a lightweight embedded Web Dashboard:

- Access via browser at `http://nas-host:5000/dashboard`
- Pure static HTML + Vanilla JS, no framework dependencies
- Data fetched via REST API, no WebSocket
- Provides basic system health, disk, service, and Agent status panels
- Does not provide management operations (all operations via CLI)

### 3.4 CLI Design Principles

| Principle | Description |
|------|------|
| **Pipeline-First** | All query commands support `--output json`, compatible with `jq` and similar tools |
| **Idempotent Operations** | Management operations designed to be idempotent (e.g., `create` skips or errors if already exists) |
| **Confirmation Protection** | Dangerous operations (delete, format) require `--confirm` or interactive confirmation by default |
| **Offline-Friendly** | CLI only calls REST API, does not rely on WebSocket persistent connections |
| **Scriptable** | Supports `--token` parameter to pass NasToken directly, no interactive login required

---

## 4. API Gateway Layer

```
┌──────────────────────────────────────────────────────────────┐
│                     API GATEWAY LAYER                        │
│  ┌────────────────────────┐  ┌────────────────────────────┐  │
│  │     RESTful API        │  │         gRPC               │  │
│  │  (ASP.NET Core WebAPI) │  │   (Internal Service Comms) │  │
│  │                        │  │                            │  │
│  │  · CLI client          │  │  · Module ↔ Service Bus   │  │
│  │  · Third-party         │  │  · High-performance IPC   │  │
│  │    integration         │  │                            │  │
│  └────────────────────────┘  └────────────────────────────┘  │
│                                                              │
│  Responsibilities:                                           │
│  · Request authentication (JWT/NasToken verification)        │
│  · Rate limiting                                             │
│  · Request logging (Access Log)                              │
│  · Optional: Embedded Dashboard static files                 │
└──────────────────────────────────────────────────────────────┘
```

### 4.1 Protocol Selection

| Protocol | Use Case | Transport |
|------|------|------|
| **RESTful API** | CLI client, third-party integration, embedded Dashboard | HTTP/1.1, HTTP/2 |
| **gRPC** | Internal high-performance inter-service communication, Module ↔ Service Bus | HTTP/2 (Protocol Buffers) |

### 4.3 gRPC Service Definitions (Proto Contracts)

All internal inter-service communication is based on the following Protobuf definitions. Each Module must expose the corresponding gRPC Service.

#### 4.3.1 Storage Service (Storage)

```protobuf
// protos/storage.proto
syntax = "proto3";
package gnas.storage;
option csharp_namespace = "GNAS.Proto.Storage";

service StorageService {
  // Disk management
  rpc ListDisks (ListDisksRequest) returns (ListDisksResponse);
  rpc GetDiskDetail (GetDiskDetailRequest) returns (DiskDetail);
  rpc TriggerSmartCheck (SmartCheckRequest) returns (SmartCheckResponse);

  // RAID management
  rpc CreateRaid (CreateRaidRequest) returns (RaidResult);
  rpc GetRaidStatus (GetRaidStatusRequest) returns (RaidStatus);
  rpc DeleteRaid (DeleteRaidRequest) returns (RaidResult);

  // Filesystem management
  rpc MountFilesystem (MountRequest) returns (MountResult);
  rpc UnmountFilesystem (UnmountRequest) returns (MountResult);
  rpc FormatFilesystem (FormatRequest) returns (FormatResult);
  rpc GetFilesystemInfo (FsInfoRequest) returns (FsInfo);

  // Streaming (Rebuild progress / Scrub progress)
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
  string error_code = 4;         // Unified error code
}

message RebuildProgress {
  string pool_id = 1;
  double percent_complete = 2;
  int64 bytes_remaining = 3;
  int64 estimated_seconds = 4;
}
```

#### 4.3.2 Share Service (Share)

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
  repeated ShareProtocol protocols = 5;  // SMB, NFS, FTP; WebDAV value reserved for future compatibility
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

#### 4.3.3 Agent Service (Agent)

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
  repeated string capabilities = 4;       // NAbility string
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

#### 4.3.4 Service Bus (Service Bus)

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
  int32 pid = 4;                    // Native process PID (0 for containers)
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

#### 4.3.5 Audit and Logging (Audit)

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

#### 4.3.6 Unified Error Codes

```protobuf
// protos/common.proto
syntax = "proto3";
package gnas.common;
option csharp_namespace = "GNAS.Proto.Common";

// gRPC unified error — passed via google.rpc.Status details
// All services return this structure on error

message ErrorDetail {
  ErrorCode code = 1;
  string message = 2;
  string details = 3;            // Human-readable detailed information
  string trace_id = 4;           // Associated trace ID
  map<string, string> metadata = 5;
}

enum ErrorCode {
  // 0 reserved for success
  OK = 0;

  // General errors 1xxx
  UNKNOWN = 1000;
  INVALID_ARGUMENT = 1001;
  NOT_FOUND = 1002;
  ALREADY_EXISTS = 1003;
  PERMISSION_DENIED = 1004;
  RESOURCE_EXHAUSTED = 1005;
  INTERNAL_ERROR = 1006;
  UNAVAILABLE = 1007;
  TIMEOUT = 1008;

  // Storage errors 2xxx
  DISK_NOT_FOUND = 2001;
  DISK_IN_USE = 2002;
  DISK_IO_ERROR = 2003;
  RAID_DEGRADED = 2004;
  RAID_CREATE_FAILED = 2005;
  FS_MOUNT_FAILED = 2006;
  FS_FORMAT_FAILED = 2007;
  POOL_FULL = 2008;

  // Security errors 3xxx
  TOKEN_EXPIRED = 3001;
  TOKEN_INVALID = 3002;
  TOKEN_REVOKED = 3003;
  CAPABILITY_INSUFFICIENT = 3004;
  DATA_LEVEL_INSUFFICIENT = 3005;
  ACCOUNT_LOCKED = 3006;
  TPM_UNAVAILABLE = 3007;

  // Agent errors 4xxx
  AGENT_NOT_FOUND = 4001;
  AGENT_DEPLOY_FAILED = 4002;
  AGENT_START_FAILED = 4003;
  AGENT_CRASH_LOOP = 4004;
  COMPOSE_GENERATE_FAILED = 4005;
  DOCKER_UNAVAILABLE = 4006;

  // Service Bus errors 5xxx
  SERVICE_NOT_FOUND = 5001;
  SERVICE_DEPENDENCY_FAILED = 5002;
  SERVICE_ALREADY_RUNNING = 5003;
  CIRCULAR_DEPENDENCY = 5004;
}
```

#### 4.3.7 Proto File Directory Conventions

```
protos/
├── common.proto              # Shared types + error codes
├── storage.proto             # Storage service
├── share.proto               # Share service
├── network.proto             # Network service
├── agent.proto               # Agent service
├── backup.proto              # Backup service
├── servicebus.proto          # Service Bus
├── audit.proto               # Audit and logging
├── auth.proto                # Authentication service
└── update.proto              # Update service
```

All Proto files are compiled through `Grpc.Tools` MSBuild integration, with generated code placed under the `GNAS.Proto` namespace.

---

## 5. Application Layer

```
┌──────────────────────────────────────────────────────────────────┐
│              APPLICATION LAYER — .NET 8/9                        │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                  Module Host                                │  │
│  │  · Hot-load/unload modules (AssemblyLoadContext)            │  │
│  │  · Dependency injection registration (IServiceCollection)   │  │
│  │  · Capability declaration and validation (RequireCapability │  │
│  │    Attribute)                                               │  │
│  │  · Module lifecycle management                              │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────────┐   │
│  │ Storage  │ │  Share   │ │ Network  │ │   Monitoring     │   │
│  │ Module   │ │  Module  │ │  Module  │ │   Module         │   │
│  ├──────────┤ ├──────────┤ ├──────────┤ ├──────────────────┤   │
│  │· Disk    │ │· SMB/CIFS│ │· Interface│ │· Resource       │   │
│  │ mgmt     │ │· NFS     │ │  mgmt    │ │  monitoring     │   │
│  │· RAID/LVM│ │· FTP/SFTP│ │· Firewall│ │· Log aggregation│   │
│  │· File    │ │· FTP     │ │· DHCP/DNS│ │· Alert          │   │
│  │  system  │ │          │ │· VLAN    │ │  notifications  │   │
│  │· Encrypt │ │          │ │          │ │· Health checks   │   │
│  │  volume  │ │          │ │          │ │                  │   │
│  └──────────┘ └──────────┘ └──────────┘ └──────────────────┘   │
│                                                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────────┐   │
│  │  Agent   │ │  Backup  │ │  Update  │ │   Plugin         │   │
│  │  Module  │ │  Module  │ │  Module  │ │   Manager        │   │
│  ├──────────┤ ├──────────┤ ├──────────┤ ├──────────────────┤   │
│  │· Agent   │ │· Rsync   │ │· OTA     │ │· Hot-load/unload │   │
│  │  lifecycle│ │· Snapshot│ │  upgrade │ │· Dependency      │   │
│  │· Token   │ │· Cloud   │ │· Rollback│ │  resolution      │   │
│  │  mgmt    │ │  backup  │ │· Version │ │· Sandbox         │   │
│  │· Cap.    │ │· Scheduled│ │  check   │ │  isolation       │   │
│  │  auth.   │ │  tasks   │ │          │ │                  │   │
│  └──────────┘ └──────────┘ └──────────┘ └──────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

### 5.1 Module Definition (Interface Contract)

```csharp
/// <summary>
/// NAS module base interface. All business modules must implement this interface.
/// </summary>
public interface INasModule
{
    /// <summary>Module unique identifier</summary>
    string ModuleId { get; }

    /// <summary>Module display name</summary>
    string DisplayName { get; }

    /// <summary>Module version</summary>
    Version Version { get; }

    /// <summary>System capabilities required by the module</summary>
    IReadOnlyList<NAbility> RequiredCapabilities { get; }

    /// <summary>Other modules this module depends on</summary>
    IReadOnlyList<string> Dependencies { get; }

    /// <summary>Module initialization (register DI, start background services, etc.)</summary>
    Task InitializeAsync(ModuleContext context, CancellationToken ct);

    /// <summary>Module graceful shutdown</summary>
    Task ShutdownAsync(CancellationToken ct);

    /// <summary>Module health check</summary>
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct);
}

/// <summary>
/// Module context, through which modules access system services
/// </summary>
public record ModuleContext
{
    public IServiceProvider Services { get; init; }
    public IEventBus EventBus { get; init; }
    public ILoggerFactory LoggerFactory { get; init; }
    public string DataDirectory { get; init; }
}
```

### 5.2 Module Inventory

| Module | ModuleId | Dependencies | Description |
|------|----------|------|------|
| **Storage** | `storage` | — | Disk enumeration, RAID management, LVM, filesystem formatting, SMART monitoring, encrypted volume management |
| **Share** | `share` | `storage` | SMB/CIFS, NFS v3/v4, FTP share service management |
| **Network** | `network` | — | Network interface management, firewall rules, DHCP/DNS, VLAN configuration |
| **Agent** | `agent` | `storage`, `security` | Agent lifecycle management, token issuance and renewal, Compose generation, container monitoring |
| **Backup** | `backup` | `storage` | Rsync tasks, snapshot scheduling, cloud backup, scheduled tasks |
| **Update** | `update` | — | OTA firmware upgrades, module updates, canary releases, rollback |
| **Monitoring** | `monitoring` | — | Resource monitoring, log aggregation viewer, Dashboard data provider |
| **Plugin** | `plugin` | — | Third-party plugin loading, dependency resolution, sandbox isolation, version compatibility checking

### 5.3 Storage Pool Management Detailed Design

The storage pool is the core data container of the NAS system. A complete storage pool lifecycle is as follows:

```
Create → Format → Mount → Dataset/Subvolume Create → Share → Monitor → Expand/Replace → Retire
```

#### 5.3.1 Storage Pool Creation Process

```
User selects disks
      │
      ▼
┌─────────────────────────────────────────────────────────┐
│  1. Disk Discovery and Verification                     │
│     · Enumerate all unused physical disks               │
│       (IDiskManager.ListDisks)                          │
│     · Check if disk is empty (no partition table /      │
│       no filesystem signature)                          │
│     · SMART quick check → exclude faulty disks          │
│     · Group by interface type (SATA / NVMe / USB)       │
│     · Mark SSD vs HDD for later tiered storage          │
│       recommendations                                   │
└──────────────────────────┬──────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│  2. RAID Level Selection                                │
│                                                         │
│  Disks   │ RAID 0 │ RAID 1 │ RAID 5 │ RAID 6 │ RAID10  │
│  ────────┼────────┼────────┼────────┼────────┼─────────│
│    1     │   ✓    │   —    │   —    │   —    │   —     │
│    2     │   ✓    │   ✓    │   —    │   —    │   —     │
│    3     │   ✓    │   —    │   ✓    │   —    │   —     │
│    4+    │   ✓    │   ✓    │   ✓    │   ✓    │   ✓     │
│                                                         │
│  Recommended Strategy:                                  │
│  · 1-2 disks → RAID 1 (mirror, data safety priority)   │
│  · 3-5 disks → RAID 5 (capacity and safety balance)    │
│  · 6+ disks → RAID 6 or RAID 10 (performance + high    │
│    reliability)                                         │
│  · SSD array → optional RAID 5, but watch write         │
│    amplification                                        │
│  · Mixed SSD+HDD → tiered storage recommended           │
│    (SSD cache pool + HDD data)                          │
└──────────────────────────┬──────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│  3. Filesystem Selection                                │
│                                                         │
│  Filesystem │ Max Vol │ CoW │ Compress │ Snap │Chksum│ │
│             │         │     │          │      │Heal  │ │
│  ───────────┼─────────┼─────┼──────────┼──────┼──────│ │
│  ext4       │  1EB    │  ✗  │   ✗     │  ✗   │  ✗   │ │
│  XFS        │  8EB    │  ✗  │   ✗     │  ✗   │  ✗   │ │
│  Btrfs      │ 16EB    │  ✓  │   ✓     │  ✓   │  ✓   │ │
│  ZFS        │ 256ZB   │  ✓  │   ✓     │  ✓   │  ✓   │ │
│  NTFS       │  8PB    │  ✗  │   ✓     │  ✓*  │  ✗   │ │
│  ReFS       │ 35PB    │  ✓  │   ✗     │  ✗   │  ✓   │ │
│                                                         │
│  Recommendations:                                       │
│  · Linux primary → Btrfs (lightweight CoW, built-in    │
│    snapshots/compression)                               │
│  · Advanced needs → ZFS (highest data integrity,        │
│    self-healing)                                        │
│  · Simple needs → ext4/XFS (stable, low overhead)      │
│  · Generic shared disk → ext4 (best compatibility)     │
└──────────────────────────┬──────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│  4. Dataset/Subvolume Planning                          │
│                                                         │
│  Recommended Structure (Btrfs/ZFS):                     │
│  pool-main/                                             │
│  ├── data/              # General data                  │
│  │   ├── media/         # Media library (no compress,   │
│  │   │                    large files)                  │
│  │   ├── documents/     # Documents (compress, dedup)   │
│  │   ├── photos/        # Photos (light compression)    │
│  │   └── downloads/     # Downloads (no snapshots,     │
│  │                        no history)                   │
│  ├── backup/            # Backup target (compress,      │
│  │                        dedup)                        │
│  ├── appdata/           # Agent/Docker persistent data  │
│  ├── home/              # User home directories         │
│  │                        (subvolumes per user)         │
│  └── timemachine/       # Time Machine backup           │
│                          (quota limited)                │
│                                                         │
│  Each dataset independently configurable:               │
│  · Compression algorithm (zstd/lz4/gzip)                │
│  · Snapshot policy (retention count/frequency)          │
│  · Quota (hard limit / soft limit)                      │
│  · Data classification label (NasDataLevel L0-L4)       │
│  · Record size (recordsize, adapted to file type)       │
└─────────────────────────────────────────────────────────┘
```

#### 5.3.2 Disk Replacement and Rebuild Process

```
┌─────────────────────────────────────────────────────────┐
│  Disk Failure Detection                                 │
│  · SMART self-test (every 30 minutes)                   │
│  · Kernel I/O error monitoring (/sys/block/*/stat)      │
│  · RAID event monitoring (mdadm --monitor / zed)        │
│  · Trigger: storage.disk.failed event                   │
└──────────────────────────┬──────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Automatic Handling Process                             │
│                                                         │
│  1. Mark disk as FAULTY                                 │
│     · mdadm: mdadm --manage /dev/mdX --fail /dev/sdY   │
│     · ZFS: zpool offline pool disk-id                   │
│                                                         │
│  2. Notify administrator                                │
│     · Critical alert → Email/Webhook/Terminal           │
│       notification                                      │
│     · CLI prompt: gnas status shows red disk status     │
│                                                         │
│  3. After inserting new disk                            │
│     · Auto-detect: udev event → storage.disk.added      │
│     · Check disk capacity ≥ faulty disk capacity        │
│     · Auto-partition (if needed, refer to original      │
│       partition table)                                   │
│                                                         │
│  4. Start rebuild                                       │
│     · mdadm: mdadm --manage /dev/mdX --add /dev/sdZ    │
│     · ZFS: zpool replace pool old-disk new-disk        │
│     · Monitor rebuild progress: /proc/mdstat or         │
│       zpool status                                      │
│     · Progress event: storage.pool.rebuilding {percent} │
│                                                         │
│  5. Rebuild complete                                    │
│     · Publish storage.pool.healthy event                │
│     · Resume scheduled Scrub schedule                   │
│                                                         │
│  Hot Spare:                                             │
│  · Pre-configure 1-2 hot spare disks                    │
│  · Automatically trigger rebuild on failure,            │
│    no manual intervention required                      │
│  · After rebuild, original hot spare becomes normal     │
│    disk, automatically replenish new hot spare          │
└─────────────────────────────────────────────────────────┘
```

#### 5.3.3 Data Scrub Scheduling

```
┌─────────────────────────────────────────────────────────┐
│  Scrub Policy Configuration                             │
│                                                         │
│  Type           │ Frequency │ Priority │ Description    │
│  ───────────────┼──────────┼─────────┼────────────────│
│  Quick Scrub    │ Weekly    │  Low    │ Metadata        │
│                 │           │         │ checksums only  │
│  Full Scrub     │ Monthly   │  Medium │ All data        │
│                 │           │         │ checksums       │
│  Deep Scrub     │ Quarterly │  Low    │ Full disk read  │
│                 │           │         │ (detect silent  │
│                 │           │         │ errors)         │
│                                                         │
│  Schedule window: default 02:00-06:00 (same as backup   │
│                    window)                               │
│  Rate limit: default 100MB/s read, configurable (avoid  │
│    affecting normal I/O)                                │
│                                                         │
│  ZFS Scrub command:                                     │
│  zpool scrub pool-main                                  │
│                                                         │
│  Btrfs Scrub command:                                   │
│  btrfs scrub start /mnt/nas/data                        │
│                                                         │
│  mdadm RAID Check:                                      │
│  echo check > /sys/block/mdX/md/sync_action             │
│                                                         │
│  Scrub results:                                         │
│  · Errors found → storage.pool.scrub.error event        │
│  · Repaired   → storage.pool.scrub.repaired event (CoW) │
│  · Complete   → storage.pool.scrub.completed event      │
│  · Error count exceeds threshold → Critical alert       │
└─────────────────────────────────────────────────────────┘
```

#### 5.3.4 Storage Pool Expansion

```
Expansion methods:
─────────────────────────────────────────────
1. Add disk to existing RAID (only some RAID levels support)
   · mdadm RAID 5/6: mdadm --grow --raid-devices=N /dev/mdX --add /dev/sdY
   · ZFS: zpool add pool /dev/sdY (becomes new VDEV, watch data balance)
   · Btrfs: btrfs device add /dev/sdY /mnt/nas/data

2. Replace with larger capacity disks (one by one)
   · Replace → Rebuild → Next → ... → auto-expand after all replaced
   · ZFS supports autoexpand property

3. Add JBOD/single-disk pool (highest flexibility)
   · Suitable for non-critical data (downloads, temporary files)

Expansion limitations:
· Expansion prohibited during RAID level degradation
· Pause Scrub during expansion
· Expansion progress visible in real-time
· Do not remove a disk that is being rebuilt (Critical alert)
```

### 5.4 Share Protocol Detailed Design

#### 5.4.1 SMB/CIFS Configuration Details

```yaml
# /srv/nas/config/services/smb.yaml
smb:
  workgroup: WORKGROUP
  server_string: "GNAS File Server"
  netbios_name: gnas-nas
  
  # Global security settings
  security: user                      # user | share (deprecated) | ads (AD domain)
  encrypt_passwords: true
  server_signing: mandatory           # disabled | auto | mandatory
  smb_encrypt: desired                # Transport encryption (SMB 3.1.1)
  
  # Protocol versions
  server_min_protocol: SMB2_10        # Minimum SMB 2.1 (Win7+)
  server_max_protocol: SMB3_11        # Maximum SMB 3.1.1
  
  # Performance optimization
  socket_options: "TCP_NODELAY IPTOS_LOWDELAY SO_RCVBUF=131072 SO_SNDBUF=131072"
  read_raw: yes
  write_raw: yes
  strict_allocate: yes
  aio_read_size: 1
  aio_write_size: 1
  
  # macOS compatibility
  vfs_objects:
    - catia                          # macOS special character mapping
    - fruit                          # macOS SMB extensions
  fruit:aapl: true
  fruit:nfs_aces: false              # Do not transfer NFS ACEs
  
  # Share definitions
  shares:
    - name: media
      path: /mnt/nas/data/media
      comment: "Media Library (Read Only)"
      read_only: true
      guest_ok: false
      browseable: true
      vfs_objects:
        - recycle                    # Recycle bin
      recycle:
        repository: .recycle/%U      # Per-user recycle bin
        keeptree: yes
        versions: yes
        maxsize: 0                   # No file size limit
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

#### 5.4.2 NAS Permission Model Mapping

```
GNAS Permission Layer     SMB/NFS               POSIX/Linux
─────────────────────────────────────────────────────────────
NasDataLevel (L0-L4)  →  File/dir label        → xattr (user.nas_level)
NAbility (Capability) →  Token Capability      → N/A (API-side verification)
RBAC (Roles)          →  SMB Group Mapping     → Linux Group
ACL (File-level)      →  SMB ACL / NFSv4 ACL   → POSIX ACL (getfacl/setfacl)
User Quota            →  SMB quota             → Linux Quota (xfs_quota / btrfs qgroup)

Permission decision flow:
  SMB request → Samba authenticates user → query file POSIX ACL → execute I/O
                    │
                    ▼
           If NasToken integration is configured:
           Samba VFS Module → check NasDataLevel → publish Audit Log
```

#### 5.4.3 User Quota Management

```csharp
/// <summary>
/// Storage quota definition. Supports per-user and per-share granularity quota control.
/// </summary>
public record StorageQuota
{
    /// <summary>Quota target: user:{username} | share:{shareName} | group:{groupName}</summary>
    public string TargetId { get; init; }

    /// <summary>Quota type</summary>
    public QuotaType Type { get; init; }  // User | Share | Group

    /// <summary>Hard limit (bytes), null = unlimited</summary>
    public long? HardLimitBytes { get; init; }

    /// <summary>Soft limit (bytes), grace period starts counting after exceeding</summary>
    public long? SoftLimitBytes { get; init; }

    /// <summary>Soft limit grace period (seconds), default 7 days</summary>
    public long GracePeriodSeconds { get; init; } = 604800;

    /// <summary>File count hard limit, null = unlimited</summary>
    public long? HardLimitInodes { get; init; }

    /// <summary>Current usage (populated on query)</summary>
    public long? UsedBytes { get; init; }
    public long? UsedInodes { get; init; }

    /// <summary>Usage percentage (0-100)</summary>
    public double UsedPercent => HardLimitBytes.HasValue && HardLimitBytes.Value > 0
        ? (double)(UsedBytes ?? 0) / HardLimitBytes.Value * 100
        : 0;
}

public enum QuotaType { User, Share, Group }
```

**Quota implementation methods:**

| Filesystem | Quota Mechanism | Command |
|----------|----------|------|
| **ext4/XFS** | Linux Quota | `xfs_quota -x -c 'limit bsoft=900G bhard=1T user1' /mnt` |
| **Btrfs** | qgroup | `btrfs qgroup limit 1T /mnt/nas/data/home/user1` |
| **ZFS** | ZFS Quota | `zfs set quota=1T pool-main/home/user1` |

**Quota alert thresholds:**
- 80% → Info level, notify user
- 90% → Warning level, notify user + administrator
- 95% → Warning level, begin rejecting new writes (soft limit reached)
- 100% → Error level, forcibly reject writes (hard limit reached)

#### 5.4.4 Recycle Bin Mechanism

```
Recycle bin configuration (similar to Synology #recycle):
─────────────────────────────────────
· Independently enabled/disabled per share
· Deleted files moved to .recycle/{username}/ instead of direct deletion
· Retention policy:
  - By days: retain 30 days → auto-clean
  - By capacity: recycle bin > 5% of share capacity → clean oldest files
  - By file count: exceed 10000 files → clean oldest files
· Exclusion rules: *.tmp, *.temp, ~$*, .DS_Store, Thumbs.db
· Managed via CLI:
  gnas recycle list <share>         # View recycle bin contents
  gnas recycle restore <id>         # Restore specified file
  gnas recycle empty <share>        # Empty recycle bin
  gnas recycle config <share>       # Configure recycle bin policy
```

#### 5.4.5 NFS Configuration Details

```yaml
# /srv/nas/config/services/nfs.yaml
nfs:
  # NFS version support
  versions:
    - 3       # Compatible with older clients
    - 4.0     # Stateful protocol
    - 4.1     # pNFS parallel access
    - 4.2     # Server-side Copy, Sparse Files
  
  # Concurrency settings
  nfsd_threads: 16                    # NFS service thread count
  nfsd_grace_period: 90               # Lock recovery grace period (seconds)
  
  # Export definitions
  exports:
    - path: /mnt/nas/data/media
      clients:
        - network: 192.168.1.0/24
          options:
            - ro                        # Read-only
            - sync                      # Synchronous writes
            - no_subtree_check          # Don't check sub-tree
            - all_squash                # Map all clients to anonymous user
            - anonuid=1000
            - anongid=1000
        - network: 10.0.0.0/8
          options: [ro, sync, no_subtree_check]
    
    - path: /mnt/nas/data/documents
      clients:
        - network: 192.168.1.0/24
          options:
            - rw                        # Read-write
            - async                     # Asynchronous writes (performance priority)
            - no_subtree_check
            - sec=krb5p                 # Kerberos encryption + integrity
```

### 5.5 Data Protection and Backup Strategy

#### 5.5.1 Snapshot System

```
Snapshot hierarchy:
─────────────────────────────────────────────
┌─────────────────────────────────────────────────────────┐
│  File-level snapshots (Btrfs / ZFS / ReFS)              │
│  · Instant creation, COW mechanism, only stores delta   │
│  · Users can self-restore via SMB snapshot entry        │
│  · gnas snapshot create <dataset>                       │
│  · gnas snapshot list <dataset>                         │
│  · gnas snapshot restore <dataset> <snapshot-id>        │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  Snapshot scheduling policy (independently configured   │
│  per dataset)                                            │
│                                                         │
│  Dataset      │ Frequency    │ Retention      │ Notes   │
│  ────────────┼──────────────┼────────────────┼─────────│
│  documents   │ Every 15min  │ 24h:48, 30d:30 │ High    │
│              │              │                │ freq.  │
│  media       │ Daily        │ 7d:7, 4w:4     │ Low     │
│              │              │                │ freq.  │
│  photos      │ Hourly       │ 24h:24, 30d:30 │ Medium  │
│              │              │                │ freq.  │
│  appdata     │ Every 6h     │ 7d:28, 4w:4    │ Low-med │
│  home/*      │ Hourly       │ 24h:24, 7d:7   │ Medium  │
│              │              │                │ freq.  │
│  downloads   │ No snapshots │ —              │ No      │
│              │              │                │ retent. │
│                                                         │
│  Snapshot naming: gnas-{dataset}-{yyyyMMdd-HHmmss}      │
│  Auto-cleanup: old snapshots exceeding retention policy │
│    are automatically deleted                             │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  SMB Previous Versions Integration                      │
│                                                         │
│  Samba vfs_shadow_copy2 module exposes snapshots as     │
│  shadow copies:                                         │
│  vfs_objects = shadow_copy2                             │
│  shadow:snapdir = /mnt/nas/.snapshots/{dataset}         │
│  shadow:sort = desc                                     │
│  shadow:format = gnas-{dataset}-%Y%m%d-%H%M%S           │
│                                                         │
│  → SMB clients supporting this protocol can browse and  │
│    restore previous versions                             │
│  → macOS Time Machine can use SMB share as backup       │
│    target                                                │
└─────────────────────────────────────────────────────────┘
```

#### 5.5.2 Backup System

```
┌─────────────────────────────────────────────────────────┐
│              Three-layer Backup Architecture            │
│                                                         │
│  Layer 1: Local snapshots (instant recovery)            │
│  ├── Advantage: second-level create/restore, zero       │
│  │   additional storage                                 │
│  └── Disadvantage: cannot protect against hardware      │
│      failure/disaster                                   │
│                                                         │
│  Layer 2: Local backup (fast recovery)                  │
│  ├── Target: external USB drive / second storage pool   │
│  │   / dedicated backup disk                            │
│  ├── Tools: rsync / btrfs send / zfs send              │
│  ├── Frequency: daily (incremental) + weekly (full)     │
│  └── Retention: 14 day incremental + 4 week full +     │
│      12 month full                                       │
│                                                         │
│  Layer 3: Offsite/cloud backup (disaster recovery)      │
│  ├── Target: S3 / Backblaze B2 / remote GNAS node       │
│  ├── Tools: rclone / restic / borgbackup                 │
│  ├── Encryption: AES-256-GCM, client-side encrypted     │
│      before upload                                       │
│  ├── Frequency: daily (encrypted incremental)           │
│  └── Retention: 7 day incremental + 4 week full +      │
│      12 month full                                       │
└─────────────────────────────────────────────────────────┘

Backup task definition:
```csharp
public record BackupTask
{
    public string TaskId { get; init; }
    public string Name { get; init; }

    // Source and target
    public string SourcePath { get; init; }        // /mnt/nas/data/documents
    public BackupTarget Target { get; init; }

    // Schedule
    public string CronExpression { get; init; }    // "0 2 * * *" = daily at 02:00
    public bool Enabled { get; init; } = true;

    // Policy
    public BackupMethod Method { get; init; }      // Incremental | Full | Mirror
    public int RetentionDays { get; init; } = 30;
    public int RetentionCount { get; init; } = 10;
    public bool Compression { get; init; } = true;
    public bool Encryption { get; init; } = true;  // Offsite backup requires encryption

    // Exclusions
    public string[] ExcludePatterns { get; init; } // ["*.tmp", "Thumbs.db", "@eaDir"]
}

public record BackupTarget
{
    public BackupTargetType Type { get; init; }    // Local | RemoteNas | S3 | B2 | WebDAV
    public string ConnectionString { get; init; }  // Connection string or remote path
    public string BucketOrPath { get; init; }      // Target path
    public string? AccessKey { get; init; }        // Encrypted storage, not stored in plaintext
    public string? SecretKey { get; init; }        // Encrypted storage, not stored in plaintext
}

public enum BackupTargetType { Local, RemoteNas, S3, B2, WebDAV, SFTP }
public enum BackupMethod { Incremental, Full, Mirror }
```

Backup verification:
```
· After each backup task completes → verify checksum
· Weekly → automatic restore test (restore to temp directory → verify file integrity)
· Verification failure → Warning alert
· 3 consecutive verification failures → Critical alert
```

#### 5.5.3 Disaster Recovery Process

```
┌─────────────────────────────────────────────────────────┐
│  New System Recovery Process                            │
│                                                         │
│  1. Install GNAS system (ISO / script)                  │
│  2. Run recovery wizard: gnas recovery start            │
│  3. Select recovery source:                             │
│     · Local backup disk → auto-mount → scan backup      │
│       directory                                         │
│     · Cloud storage → enter credentials → list          │
│       available backups                                 │
│     · Remote GNAS → enter address + credentials         │
│  4. Restore system config: /srv/nas/config/ → restore   │
│     YAML + SQLite                                       │
│  5. Restore storage pool config: ZFS/btrfs pool         │
│     definition → re-import pool                         │
│     (ZFS: zpool import, Btrfs: direct mount)           │
│  6. Restore data: rsync / restic restore → target path  │
│  7. Restore Agents: regenerate compose.yml + start      │
│     containers                                          │
│  8. Verify restoration: auto checksum verification +    │
│     service health check                                │
│  9. Completion notice: "System restored to {source}     │
│     at {timestamp}"                                     │
└─────────────────────────────────────────────────────────┘
```

---

## 6. Security and Identity Layer

This layer draws inspiration from the distributed security design concepts of HarmonyOS, not copying the original model but deeply adapting it for NAS multi-user, multi-device, and multi-Agent scenarios.

### 6.1 HarmonyOS Concepts → NAS Adaptation Mapping

```
HarmonyOS Concept        →    GNAS Adaptation

Access Token             →    NasToken (JWT with embedded capabilities)
ATM (Token Manager)      →    NasTokenManager (issue/verify/revoke/rotate)
Capability               →    NAbility (fine-grained capability atoms)
HUKS (Key Management)    →    NasKeyStore (TPM + software fallback)
Data Level (S0-S4)       →    NasDataLevel (file/directory level data classification)
Device Certification     →    DeviceTrust (device trust chain)
```

### 6.2 Architecture Diagram

```
┌═══════════════════════════════════════════════════════════════┐
║           SECURITY & IDENTITY LAYER                          ║
║                                                                ║
║  ┌───────────────────────────────────────────────────────────┐ ║
║  │                  ┌──────────────────────┐                  │ ║
║  │                  │   Identity Service   │                  │ ║
║  │                  └──────────┬───────────┘                  │ ║
║  │                             │                               │ ║
║  │    ┌────────────────────────┼────────────────────────┐     │ ║
║  │    │                        │                        │     │ ║
║  │    ▼                        ▼                        ▼     │ ║
║  │ ┌──────────┐    ┌──────────────────┐    ┌──────────────────┐│ ║
║  │ │ Local    │    │ Federated        │    │ Device/Agent     ││ ║
║  │ │ Identity │    │ Identity         │    │ Identity         ││ ║
║  │ │          │    │                  │    │                  ││ ║
║  │ │· Username│    │· LDAP/AD domain │    │· Agent Token     ││ ║
║  │ │· Password│    │· OAuth2/OIDC    │    │· Device Cert     ││ ║
║  │ │· Biometric│   │· Third-party    │    │· Service Account ││ ║
║  │ │· 2FA     │    │  login          │    │· API Key         ││ ║
║  │ │          │    │· SAML enterprise│    │                  ││ ║
║  │ │          │    │  SSO            │    │                  ││ ║
║  │ └─────┬────┘    └────────┬─────────┘    └────────┬─────────┘│ ║
║  │       │                  │                       │          │ ║
║  │       └──────────────────┼───────────────────────┘          │ ║
║  │                          │                                   │ ║
║  │                          ▼                                   │ ║
║  │  ┌─────────────────────────────────────────────────────────┐│ ║
║  │  │            Access Token Manager (ATM)                   ││ ║
║  │  │                                                         ││ ║
║  │  │  · JWT token issue/verify/revoke                       ││ ║
║  │  │  · Capability-Embedded Token                           ││ ║
║  │  │  · Token hierarchy: user token > session token >       ││ ║
║  │  │    operation token                                     ││ ║
║  │  │  · Automatic Agent token rotation                      ││ ║
║  │  │  · Cross-device token sync (multi-NAS cluster)         ││ ║
║  │  └──────────────────────────┬──────────────────────────────┘│ ║
║  │                             │                                │ ║
║  │                             ▼                                │ ║
║  │  ┌─────────────────────────────────────────────────────────┐│ ║
║  │  │            Permission Engine                            ││ ║
║  │  │                                                         ││ ║
║  │  │   ┌─────────────┐  ┌─────────────┐  ┌──────────────┐   ││ ║
║  │  │   │ Capability  │  │   RBAC     │  │  ACL Engine  │   ││ ║
║  │  │   │  Engine     │  │  Engine    │  │  (File-level) │   ││ ║
║  │  │   └──────┬──────┘  └─────┬───────┘  └──────┬───────┘   ││ ║
║  │  │          └───────────────┼─────────────────┘            ││ ║
║  │  │                          │                              ││ ║
║  │  │                          ▼                              ││ ║
║  │  │              Unified Policy Decision Point              ││ ║
║  │  └─────────────────────────────────────────────────────────┘│ ║
║  │                                                                ║
║  │  ┌─────────────────────────────────────────────────────────┐  │ ║
║  │  │              NasKeyStore (Key Storage)                   │  │ ║
║  │  │  · TPM/Secure Enclave integration    · Shared encryption │  │ ║
║  │  │    key management                                        │  │ ║
║  │  │  · Agent Secret secure injection    · TLS certificate    │  │ ║
║  │  │    management                                           │  │ ║
║  │  └─────────────────────────────────────────────────────────┘  │ ║
║  └──────────────────────────────────────────────────────────────────┘ ║
╚═════════════════════════════════════════════════════════════════════════╝
```

### 6.3 NAbility Capability Model

```
Capability naming convention: <domain>:<resource>:<action>[:<scope>]

Example hierarchy:
  storage:*:*                    ← Full storage control (Admin)
  storage:pool:main:*            ← Main storage pool full control
  storage:pool:main:read         ← Main storage pool read-only
  storage:share:media:*          ← media share full control
  storage:share:media:read       ← media share read-only
  storage:snapshot:*             ← Snapshot management

  share:smb:*:*                  ← SMB service full control
  share:smb:config:write         ← SMB config modification
  share:nfs:export:read          ← NFS export view

  agent:*:*                      ← Agent full control (Admin)
  agent:lifecycle:deploy          ← Agent deploy permission
  agent:lifecycle:start_stop      ← Agent start/stop permission
  agent:token:issue               ← Issue Agent token
  agent:config:write              ← Modify Agent config

  admin:user:*                    ← User management
  admin:network:*                 ← Network management
  admin:audit:read                ← Audit log view

  data:level:public               ← Access L0 public data
  data:level:internal             ← Access L1 internal data
  data:level:personal             ← Access L2 personal data
  data:level:sensitive            ← Access L3 sensitive data
  data:level:system               ← Access L4 system data
```

### 6.4 NasToken Structure

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

### 6.5 NasDataLevel Data Classification

```
Level │ Name        │ Label Color │ Permission Rules
──────┼────────────┼────────┼────────────────────────────────
  L0  │ Public      │ Green       │ Anonymous readable, no auth required
  L1  │ Internal    │ Blue        │ Any authenticated user can read
  L2  │ Personal    │ Yellow      │ Owner + explicitly authorized only
  L3  │ Sensitive   │ Orange      │ Explicit authorization + audit trail + encrypted storage
  L4  │ System      │ Red         │ Admin only + mandatory audit + hardware encryption
```

### 6.6 Agent Authorization Flow

```
User deploys OpenClaw Agent
        │
        ▼
  ┌─────────────────────────────────────────┐
  │  1. User specifies capabilities required by Agent:│
  │     - storage:share:media:read          │
  │     - share:smb:access                  │
  │     - data:level:internal               │
  │     - agent:lifecycle:start_stop (self-manage) │
  └──────────────────┬──────────────────────┘
                     │
                     ▼
  ┌─────────────────────────────────────────┐
  │  2. ATM issues Agent Token:             │
  │     - token_type: "agent"               │
  │     - capabilities: [specified capability list]│
  │     - delegation_chain: [admin, alice]  │
  │     - exp: 24h (auto-renew)              │
  │     - device_binding: nas-host-id       │
  └──────────────────┬──────────────────────┘
                     │
                     ▼
  ┌─────────────────────────────────────────┐
  │  3. Token injected into container:       │
  │     - Environment variable: NAS_TOKEN=<jwt>     │
  │     - Or Secret file: /run/secrets/           │
  │     - API endpoint: NAS_API_ENDPOINT          │
  └──────────────────┬──────────────────────┘
                     │
                     ▼
  ┌─────────────────────────────────────────┐
  │  4. Agent carries Token on each API call:    │
  │     Header: Authorization: Bearer <jwt> │
  │     → Permission Engine parses capabilities   │
  │     → Capability matches? Allow : 403        │
  └─────────────────────────────────────────┘
```

---

## 7. Service Bus Container Layer

This layer is GNAS's core innovation — a Linux application-level service manager that uniformly manages all service processes in the NAS (native services + Docker containers).

```
┌═══════════════════════════════════════════════════════════════┐
║              SERVICE BUS CONTAINER                           ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │            Service Registry                               │ ║
║  │  Registers metadata for all services: name/type/version/   │ ║
║  │  dependencies/port/health check                            │ ║
║  └──────────────────────────────────────────────────────────┘ ║
║                                                                ║
║  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐          ║
║  │  Supervisor   │ │  IPC Bus     │ │  Health      │          ║
║  │               │ │              │ │  Monitor     │          ║
║  │               │ │              │ │              │          ║
║  │ · Start/Stop  │ │ · Event Bus  │ │ · Heartbeat  │          ║
║  │ · Restart     │ │ · Command    │ │ · Liveness   │          ║
║  │   Policy      │ │   Channel    │ │   Probe      │          ║
║  │ · Dependency  │ │ · Data Flow  │ │ · Readiness  │          ║
║  │   Ordering    │ │ · Broadcast/ │ │   Probe      │          ║
║  │ · Graceful    │ │   Unicast    │ │ · Auto-      │          ║
║  │   Shutdown    │ │              │ │   Recovery   │          ║
║  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘          ║
║         │                │                │                  ║
║         └────────────────┼────────────────┘                  ║
║                          │                                   ║
║  ┌───────────────────────┴────────────────────────────────┐  ║
║  │                Service Hosts                                                │  ║
║  │                                                         │  ║
║  │  ┌──────────────────────┐  ┌──────────────────────────┐ │  ║
║  │  │  Native Service Host │  │  Container Service Host  │ │  ║
║  │  │                               │  │                                    │ │  ║
║  │  │                      │  │                          │ │  ║
║  │  │  · smb-daemon        │  │  · openclaw-agent        │ │  ║
║  │  │  · nfs-server        │  │  · home-assistant        │ │  ║
║  │  │  · ftp-server        │  │  · plex-media-server     │ │  ║
║  │  │  · nginx             │  │  · nextcloud             │ │  ║
║  │  │  · .NET Modules      │  │  · immich                │ │  ║
║  │  │                      │  │                          │ │  ║
║  │  │  Management:                          │  │  Management:                             │ │  ║
║  │  │  · Direct process         │  │  · docker compose        │ │  ║
║  │  │  · systemd           │  │  · Docker API            │ │  ║
║  │  │                      │  │  · containerd            │ │  ║
║  │  └──────────────────────┘  └──────────────────────────┘ │  ║
║  └─────────────────────────────────────────────────────────┘  ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │                Event Bus                                  │ ║
║  │                                                           │ ║
║  │  Topic examples:                                          │ ║
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

### 7.1 Service Definition Model

```csharp
/// <summary>
/// Service definition. Describes a service/process/container managed by Service Bus.
/// </summary>
public record ServiceDefinition
{
    /// <summary>Service unique identifier (e.g. "smb-daemon")</summary>
    public string ServiceId { get; init; }

    /// <summary>Service display name (e.g. "SMB/CIFS File Sharing")</summary>
    public string DisplayName { get; init; }

    /// <summary>Service type</summary>
    public ServiceType Type { get; init; }  // Native | Container | Module

    /// <summary>List of other service IDs this depends on</summary>
    public string[] DependsOn { get; init; }  // ["network", "storage-pool-main"]

    /// <summary>Capabilities required by this service</summary>
    public string[] RequiredCapabilities { get; init; }

    /// <summary>Startup policy</summary>
    public ServiceStartup Startup { get; init; }  // Automatic | Manual | Disabled

    /// <summary>Restart policy</summary>
    public RestartPolicy RestartPolicy { get; init; }  // Always | OnFailure | Never | ExponentialBackoff

    /// <summary>Native process: executable path</summary>
    public string Executable { get; init; }

    /// <summary>Container process: compose.yml path</summary>
    public string ComposeFile { get; init; }

    /// <summary>Health check configuration</summary>
    public HealthCheckConfig HealthCheck { get; init; }

    /// <summary>Resource quota</summary>
    public ResourceQuota Quota { get; init; }
}

public enum ServiceType
{
    Native,     // Native OS process
    Container,  // Docker container
    Module      // .NET Module (in-process)
}

public record HealthCheckConfig
{
    public HealthCheckType Type { get; init; }  // HttpGet | TcpConnect | ExecCommand | Grpc
    public string Endpoint { get; init; }       // Check endpoint/command
    public int IntervalSeconds { get; init; }   // Check interval
    public int TimeoutSeconds { get; init; }    // Timeout
    public int Retries { get; init; }           // Failure retry count
    public int StartPeriodSeconds { get; init; }// Startup grace period
}

public record ResourceQuota
{
    public double? CpuLimit { get; init; }     // CPU core limit
    public long? MemoryLimitBytes { get; init; }// Memory limit
    public int? IoWeight { get; init; }         // IO weight (1-1000)
}
```

### 7.2 Inter-Service Communication Patterns

```
There are two modes of inter-service communication:

1. Event Bus (Publish/Subscribe):
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

2. gRPC (Request/Response):
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

## 8. Docker/Agent Integration Layer

```
┌═══════════════════════════════════════════════════════════════┐
║        DOCKER & AGENT INTEGRATION LAYER                      ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │              Agent Manager                               │ ║
║  │                                                           │ ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐   │ ║
║  │  │ Agent Catalog│  │ Token Broker │  │ Compose        │   │ ║
║  │  │              │  │              │  │ Generator      │   │ ║
║  │  │              │  │              │  │                │   │ ║
║  │  │ · Available │  │ · Issue      │  │                │   │ ║
║  │  │   Agent     │  │   Agent      │  │ · Generate     │   │ ║
║  │  │   templates │  │   Token      │  │   compose      │   │ ║
║  │  │ · Version   │  │ · Capability │  │ · Auto-mount   │   │ ║
║  │  │   mgmt      │  │   scope      │  │   volumes      │   │ ║
║  │  │ · Dependency│  │ · Time       │  │ · Network      │   │ ║
║  │  │   check     │  │   control    │  │   policy       │   │ ║
║  │  │ · Ratings   │  │ · Revoke/    │  │ · Env var      │   │ ║
║  │  │   /reviews  │  │   renew      │  │   injection    │   │ ║
║  │  └──────┬───────┘  └──────┬───────┘  └────────┬───────┘   │ ║
║  └─────────┼─────────────────┼───────────────────┼────────────┘ ║
║            │                 │                   │               ║
║            ▼                 ▼                   ▼               ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │               Agent Runtime Environment                   │ ║
║  │                                                           │ ║
║  │   ┌─────────────────────────────────────────────────┐    │ ║
║  │   │           Docker Engine (Official, unmodified)   │    │ ║
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
║  │              Agent ↔ NAS Interaction Channel              │ ║
║  │                                                           │ ║
║  │  ┌──────────────────┐  ┌──────────────────┐              │ ║
║  │  │ Filesystem I/O   │  │  NAS REST/gRPC   │              │ ║
║  │  │ (Volume Mount)   │  │  API             │              │ ║
║  │  │                  │  │                  │              │ ║
║  │  │ · Direct file    │  │ · Call NAS       │              │ ║
║  │  │   read/write     │  │   services       │              │ ║
║  │  │ · POSIX          │  │ · Query system   │              │ ║
║  │  │   permissions    │  │   status         │              │ ║
║  │  │ · Shared folder  │  │ · Manage         │              │ ║
║  │  │   access         │  │   shares/users   │              │ ║
║  │  │ · Snapshot data  │  │ · Trigger backup │              │ ║
║  │  │   access         │  │   tasks          │              │ ║
║  │  └──────────────────┘  └──────────────────┘              │ ║
║  │                                                           │ ║
║  │  ┌──────────────────┐                                    │ ║
║  │  │  Polling / Query  │                                    │ ║
║  │  │  (REST API)       │                                    │ ║
║  │  │                  │                                    │ ║
║  │  │ · Query system   │                                    │ ║
║  │  │   status         │                                    │ ║
║  │  │ · Fetch logs     │                                    │ ║
║  │  │ · Periodic poll  │                                    │ ║
║  │  └──────────────────┘                                    │ ║
║  └──────────────────────────────────────────────────────────┘ ║
╚═══════════════════════════════════════════════════════════════╝
```

### 8.1 Agent Deployment Process (End-to-End)

```
User browses Agent Catalog
        │
        ▼
  ┌──────────────────────────────────────────────────┐
  │  Select OpenClaw Agent, configure:               │
  │  · Accessible shared folders: media, documents   │
  │  · Required API capabilities: storage:read,      │
  │    share:access                                   │
  │  · Network mode: bridge, port: 8080              │
  │  · Resource limits: CPU 1 core, memory 512MB     │
  └──────────────────────┬───────────────────────────┘
                         │
                         ▼
  ┌──────────────────────────────────────────────────┐
  │  Agent Manager orchestration:                    │
  │                                                  │
  │  1. Generate Agent Token (NAS Token)             │
  │  2. Generate docker-compose.yml:                 │
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
  │  3. Write to /srv/nas/agents/openclaw/          │
  │  4. Register with Service Bus                    │
  │  5. docker compose up -d                         │
  └──────────────────────┬───────────────────────────┘
                         │
                         ▼
  ┌──────────────────────────────────────────────────┐
  │  Service Bus takes over Agent lifecycle:         │
  │  · Monitor container status (Docker events +     │
  │    Healthcheck)                                   │
  │  · Auto-renew Token before expiration            │
  │  · Auto-restart on crash (RestartPolicy)         │
  │  · Resource usage monitoring & alerts            │
  └──────────────────────────────────────────────────┘
```

---

## 9. Platform Abstraction Layer

```
┌═══════════════════════════════════════════════════════════┐
║      PLATFORM ABSTRACTION LAYER (.NET)                   ║
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
║  │  Linux x64 Impl    │  Shared Linux Core │  ARM Impl   │║
║  │  (linux-x64)       │                    │  (arm64)    │║
║  ├────────────────────┼────────────────────┼─────────────┤║
║  │  · systemd         │                    │  · systemd  │║
║  │  · udev (device)   │  · WMI (device     │  · udev     │║
║  │  · /proc, /sys     │    enumeration)    │  · /proc    │║
║  │  · mdadm, LVM      │  · diskpart        │  · mdadm    │║
║  │  · iptables/nft    │  · Storage Spaces  │  · nft      │║
║  │  · samba, nfs-     │  · SMB Server      │  · samba    │║
║  │    kernel-server   │    (Win built-in)   │  · nfs      │║
║  │  · Docker CE       │  · Docker Desktop  │  · Docker CE│║
║  │  · ext4/XFS/Btrfs  │    / Podman        │  · ext4/XFS │║
║  │                    │  · NTFS/ReFS       │            │║
║  └────────────────────┴────────────────────┴─────────────┘║
║                                                            ║
║  ┌──────────────────────────────────────────────────────┐ ║
║  │          .NET Runtime Identifiers (RID)               │ ║
║  │                                                       │ ║
║  │  Target Frameworks: net10.0                            │ ║
║  │  RIDs: linux-x64 | linux-arm64                       │ ║
║  │                                                       │ ║
║  │  Platform implementation automatically selected via   │ ║
║  │  dependency injection (DI):                           │ ║
║  │                                                       │ ║
║  │  if (!OperatingSystem.IsLinux())                      │ ║
║  │      throw new PlatformNotSupportedException();       │ ║
║  │  services.Add<IDiskManager, LinuxDiskManager>();      │ ║
║  │                                                       │ ║
║  │  if (RuntimeInformation.ProcessArchitecture ==        │ ║
║  │      Architecture.Arm64)                              │ ║
║  │      services.Add<IHardwareOptimizer, ArmOptimizer>();│ ║
║  └──────────────────────────────────────────────────────┘ ║
╚═══════════════════════════════════════════════════════════╝
```

### 9.1 Platform Interface Definitions

```csharp
/// <summary>
/// Disk management abstraction.
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
/// Filesystem abstraction.
/// </summary>
public interface IFileSystem
{
    Task MountAsync(string device, string mountPoint, string fsType, CancellationToken ct);
    Task UnmountAsync(string mountPoint, CancellationToken ct);
    Task FormatAsync(string device, string fsType, CancellationToken ct);
    Task<FsInfo> GetFilesystemInfoAsync(string mountPoint, CancellationToken ct);
}

/// <summary>
/// Process/service management abstraction.
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
/// Network management abstraction.
/// </summary>
public interface INetworkManager
{
    Task<IReadOnlyList<NetInterface>> ListInterfacesAsync(CancellationToken ct);
    Task ConfigureInterfaceAsync(string name, NetConfig config, CancellationToken ct);
    Task AddFirewallRuleAsync(FirewallRule rule, CancellationToken ct);
    Task RemoveFirewallRuleAsync(string ruleId, CancellationToken ct);
}

/// <summary>
/// User account abstraction.
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

## 10. Logging and Observability Layer

### 10.1 Log System Panorama

```
┌═══════════════════════════════════════════════════════════════┐
║            OBSERVABILITY & LOGGING LAYER                     ║
║                                                                ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │                  Log Producers                            │ ║
║  │                                                           │ ║
║  │  ┌───────────┐  ┌───────────┐  ┌──────────┐             │ ║
║  │  │ CLI Tool │  │ .NET API  │  │ Modules  │             │ ║
║  │  │ (client)  │  │ (gateway  │  │ (business │             │ ║
║  │  │           │  │  logs)    │  │  logs)    │             │ ║
║  │  └─────┬─────┘  └─────┬─────┘  └────┬─────┘             │ ║
║  │        │              │             │                    │ ║
║  │  ┌─────┴──────────────┴─────────────┴────────────────┐   │ ║
║  │  │             Agent container logs (stdout/stderr)   │   │ ║
║  │  └───────────────────────────────────────────────────┘   │ ║
║  └──────────────────────────────┬───────────────────────────┘ ║
║                                 │                              ║
║  ┌──────────────────────────────┴───────────────────────────┐ ║
║  │              Collection Pipeline                         │ ║
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
║  │               Log Classifier                               │ ║
║  │                                                             │ ║
║  │   System Log  ────→ System runtime logs (INFO/WARN/ERROR) │ ║
║  │   Audit Log   ────→ Audit logs (security events,           │ ║
║  │                     tamper-proof)                          │ ║
║  │   Access Log  ────→ Access logs (file access, API calls)   │ ║
║  │   Agent Log   ────→ Agent runtime logs (container stdout/  │ ║
║  │                     status)                                 │ ║
║  │   Trace Log   ────→ Distributed trace                      │ ║
║  │                     (cross-service call chain)              │ ║
║  │   Metric Log  ────→ Metric data (CPU/memory/disk/network) │ ║
║  └───────────────────────────────┬─────────────────────────────┘ ║
║                                  │                               ║
║  ┌───────────────────────────────┴─────────────────────────────┐ ║
║  │                Storage Engines                               │ ║
║  │                                                              │ ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │ ║
║  │  │  File Store  │  │  SQLite/     │  │  Loki        │       │ ║
║  │  │  (rotating)  │  │  PostgreSQL  │  │  (embedded)  │       │ ║
║  │  │ /var/log/nas │  │  (structured │  │  container   │       │ ║
║  │  │ system/*.log │  │  queries)    │  │  logs        │       │ ║
║  │  │ agent/*.log  │  │ metrics.db   │  │  aggregated  │       │ ║
║  │  │              │  │ audit.db     │  │  search      │       │ ║
║  │  │              │  │              │  │  tag index   │       │ ║
║  │  └──────────────┘  └──────────────┘  └──────────────┘       │ ║
║  │                                                              │ ║
║  │  ┌──────────────────────┐                                    │ ║
║  │  │  Audit Vault         │                                    │ ║
║  │  │  (tamper-proof)      │                                    │ ║
║  │  │  · Audit chain       │                                    │ ║
║  │  │    storage           │                                    │ ║
║  │  │  · Integrity         │                                    │ ║
║  │  │    verification      │                                    │ ║
║  │  └──────────────────────┘                                    │ ║
║  └──────────────────────────────────────────────────────────────┘ ║
║                                                                   ║
║  ┌──────────────────────────────────────────────────────────────┐ ║
║  │                Log Services                                  │ ║
║  │                                                              │ ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │ ║
║  │  │ Log Query    │  │ Alert Engine │  │ Retention    │       │ ║
║  │  │ Service      │  │              │  │ Manager      │       │ ║
║  │  │              │  │              │  │              │       │ ║
║  │  │ · Full-text  │  │ · Rule       │  │ · Auto-      │       │ ║
║  │  │   search     │  │   evaluation │  │   archive    │       │ ║
║  │  │ · Time range │  │ · Threshold  │  │ · Auto-      │       │ ║
║  │  │ · Tag filter │  │   trigger    │  │   delete     │       │ ║
║  │  │ · Correlated │  │ · Aggregate  │  │ · Storage    │       │ ║
║  │  │   query      │  │   alert      │  │   quota      │       │ ║
║  │  │              │  │ · Silence    │  │              │       │ ║
║  │  │              │  │   rules      │  │              │       │ ║
║  │  └──────────────┘  └──────────────┘  └──────────────┘       │ ║
║  └──────────────────────────────────────────────────────────────┘ ║
║                                                                   ║
║  ┌──────────────────────────────────────────────────────────────┐ ║
║  │               Visualization                                  │ ║
║  │                                                              │ ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │ ║
║  │  │ Log Viewer   │  │ Dashboard    │  │ Alert        │       │ ║
║  │  │ (real-time + │  │ (system +    │  │ Notifications│       │ ║
║  │  │  history)    │  │  Agent)      │  │              │       │ ║
║  │  └──────────────┘  └──────────────┘  └──────────────┘       │ ║
║  └──────────────────────────────────────────────────────────────┘ ║
╚══════════════════════════════════════════════════════════════════╝
```

### 10.2 Unified Log Structure

```csharp
/// <summary>
/// Unified log entry, all log types use this structure.
/// </summary>
public record LogEntry
{
    // === Basic fields ===
    public string LogId { get; init; }             // UUID v7 (time-ordered)
    public DateTimeOffset Timestamp { get; init; }
    public LogCategory Category { get; init; }     // System | Audit | Access | Agent | Trace | Metric
    public LogLevel Level { get; init; }           // Trace | Debug | Info | Warn | Error | Fatal

    // === Source identification ===
    public string SourceComponent { get; init; }   // "StorageModule", "SmbService", "OpenClawAgent"
    public string SourceLayer { get; init; }       // "API", "ServiceBus", "Module", "Agent", "OS"
    public string HostName { get; init; }
    public string HostArch { get; init; }          // x64, arm64

    // === Business context ===
    public string UserId { get; init; }            // Associated user (required for audit/access)
    public string AgentId { get; init; }           // Associated Agent
    public string ServiceId { get; init; }         // Associated service
    public string TraceId { get; init; }           // Distributed trace
    public string SpanId { get; init; }            // Call span

    // === Content ===
    public string Message { get; init; }
    public string Template { get; init; }          // "User {UserId} accessed {FilePath}"
    public Dictionary<string, object> Properties { get; init; }
    public string[] Tags { get; init; }            // ["security", "permission-denied"]

    // === Audit-specific ===
    public AuditDetail Audit { get; init; }

    // === Metric-specific ===
    public MetricData Metric { get; init; }
}

/// <summary>
/// Audit log extension: records every permission decision and sensitive operation.
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
    public string BeforeState { get; init; }       // State before change (JSON)
    public string AfterState { get; init; }        // State after change (JSON)

    // === Tamper-proof (audit chain) ===
    public string PreviousHash { get; init; }      // SHA-256 of previous audit log
    public string CurrentHash { get; init; }       // SHA-256 of this log entry
    public string ChainSignature { get; init; }    // HMAC-SHA256(CurrentHash, ChainKey)
}

/// <summary>
/// Metric log extension.
/// </summary>
public record MetricData
{
    public string MetricName { get; init; }        // "cpu.usage", "disk.iops", "memory.available"
    public double Value { get; init; }
    public string Unit { get; init; }              // "percent", "bytes", "iops", "mbps"
    public Dictionary<string, string> Dimensions { get; init; } // {"disk":"sda", "pool":"main"}
}

/// <summary>
/// Log category enum.
/// </summary>
public enum LogCategory
{
    System,    // System runtime logs
    Audit,     // Audit logs (tamper-proof)
    Access,    // Access logs (file + API)
    Agent,     // Agent/container runtime logs
    Trace,     // Distributed trace
    Metric     // Metric data
}
```

### 10.3 Audit Tamper-Proof Chain

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Audit Chain                                     │
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
│  Verification rules:                                                     │
│  1. Traverse the chain, verify each CurrentHash = H(PrevHash +          │
│     Content...)                                                          │
│  2. Verify ChainSignature matches                                        │
│  3. Any broken link → alert "Audit log may have been tampered with"     │
│  4. Audit chain regularly exported to external storage (cloud/           │
│     external drive)                                                      │
└─────────────────────────────────────────────────────────────────────────┘
```

### 10.4 Alert Engine

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Alert Engine                                  │
│                                                                          │
│  Alert levels:                                                           │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ Critical — Disk failure/RAID degraded/Audit chain broken/          │ │
│  │            Intrusion detected                                       │ │
│  │  Notification: Email + Webhook + System alert                      │ │
│  │                                                                     │ │
│  │ Warning — Disk >90%/High memory/Token expiring soon/Multiple       │ │
│  │           failed logins                                             │ │
│  │  Notification: Email + UI prompt                                    │ │
│  │                                                                     │ │
│  │ Info — Service restart/Agent deploy/Storage pool expansion/         │ │
│  │        OTA update                                                   │ │
│  │  Notification: UI event stream + log                                │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                                                          │
│  Silence rules:                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ · Maintenance mode: when manually entering maintenance mode,        │ │
│  │   suppress all non-Critical alerts                                  │ │
│  │ · Time window: backup window (02:00-06:00) suppress IO-related     │ │
│  │   alerts                                                            │ │
│  │ · Dependency suppression: when network is unreachable, suppress     │ │
│  │   downstream service connection failure alerts                      │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

### 10.5 Log Retention Policy

```
Log Type     │ Storage     │ Hot     │ Warm    │ Cold    │ Notes
             │ Location    │ Storage │ Storage │ Storage │
─────────────┼─────────────┼─────────┼─────────┼─────────┼───────────
System       │ File + Loki │ 7 days  │ 30 days │ Delete  │ Auto-rotate
Audit        │ Audit Vault │ 90 days │ 1 year  │ Forever │ Cannot be
             │             │         │         │(external)│ deleted
Access       │ SQLite/DB   │ 30 days │ 90 days │ 180 days│ Configurable
Agent        │ Loki        │ 7 days  │ 30 days │ Delete  │ Per Agent
Trace        │ Loki        │ 3 days  │ 7 days  │ Delete  │ Sampled
             │             │         │         │         │ storage
Metric       │ SQLite/DB   │ 30 days │ 90 days │ 365 days│ Downsampled

Storage quota:
· Default total quota: 2% of NAS total capacity (minimum 5GB, maximum 50GB)
· Audit logs: not subject to quota management, independent storage pool
· Reaching 80% quota → Info alert
· Reaching 95% quota → Warning alert + auto-clean oldest cold data
· Reaching 100% quota → Critical alert + force cleanup

Metric downsampling:
Raw (10s) → 1min aggregate → 5min aggregate → 1h aggregate → 1d aggregate
Retain 7 days   30 days        90 days         365 days      Forever
```

### 10.6 Agent Log Collection Pipeline

```
  ┌───────────────────────────────────────────────────────────────┐
  │  OpenClaw Agent Container                                     │
  │  stdout ──► Application logs                                  │
  │  stderr ──► Error logs                                        │
  │  /var/log/agent/ ──► Structured log files (JSON Lines)        │
  │  NAS API ──► Report critical events via API                   │
  └────────────────────────────┬──────────────────────────────────┘
                               │
                    Docker Log Driver (json-file)
                               │
                               ▼
  ┌───────────────────────────────────────────────────────────────┐
  │  Agent Log Collector (.NET BackgroundService)                  │
  │  1. Docker Events monitoring: container start/stop/die/health │
  │  2. Docker Logs API fetch: docker logs --since {timestamp}   │
  │  3. Volume Mount read: /mnt/nas/agents/{agent}/logs/*.jsonl  │
  │  4. NAS API receive: Agent pushes via /api/agent/logs         │
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

## 11. Data Flow Diagrams

### 11.1 Complete Data Flow for User Operations

```
┌──────────┐    HTTPS     ┌──────────────┐    gRPC     ┌──────────────┐
│ CLI Tool │ ◄──────────► │  API Gateway │ ◄─────────► │  .NET Module │
│ (gnas)   │              │  (Kestrel)   │             │  (e.g. Agent)│
└──────────┘              └──────┬───────┘             └──────┬───────┘
                                 │                            │
                          ┌──────┴────────────┐      ┌────────┴────────┐
                          │  NasToken         │      │  Business Logic │
                          │  Verification     │      │  Processing     │
                          │  NAbility         │      │  · Token Gen    │
                          │  Permission Check │      │  · Compose Gen  │
                          └───────────────────┘      └────────┬────────┘
                                                              │
                                              ┌───────────────┼───────────┐
                                              │               │           │
                                        ┌─────┴─────┐  ┌──────┴──────┐ ┌─┴──────────┐
                                        │ Storage   │  │ Service Bus │ │ Docker API  │
                                        │ Config    │  │ Register    │ │ compose up  │
                                        │ SQLite/DB │  │ Service     │ │            │
                                        └───────────┘  └──────┬──────┘ └──────┬──────┘
                                                              │               │
                                                              ▼               ▼
                                                       ┌──────────┐  ┌────────────┐
                                                       │ Service  │  │ Container  │
                                                       │ Monitor  │  │ Running    │
                                                       │ Health   │  │ OpenClaw   │
                                                       │ Check    │  │            │
                                                       └──────────┘  └─────┬──────┘
                                                                           │
                                                                     ┌─────┴──────┐
                                                                     │ NAS API    │
                                                                     │ (Agent     │
                                                                     │  calls)    │
                                                                     └────────────┘
```

### 11.2 Agent ↔ NAS Interaction Data Flow

```
                      ┌─────────────────┐
                      │   OpenClaw Agent │
                      │   Container      │
                      └────────┬────────┘
                               │
              ┌────────────────┼────────────────┐
              │                │
     Volume Mount        NAS API (REST/gRPC)
     (File I/O)          │
              │                │
              ▼                ▼
      ┌───────────┐    ┌───────────────┐
      │ /mnt/nas/ │    │ NAS API       │
      │  media/   │    │ · Query       │
      │  documents│    │   shares      │
      │           │    │ · Read        │
      │           │    │   config      │
      │ Permission│    │ · Trigger     │
      │ Check:    │    │   backup      │
      │ POSIX ACL │    │ Permission    │
      └───────────┘    │ Check:        │
                       │ NasToken      │
                       └───────────────┘
```

---

## 12. Technology Selection Overview

| Layer | Technology | Description |
|------|------|------|
| **Interaction** | .NET 10 Console Application (gnas CLI) | Linux CLI tool, supports TUI + batch processing |
| **API Gateway** | ASP.NET Core Minimal API + gRPC | REST external (CLI/third-party), gRPC internal |
| **Embedded Dashboard** | Pure static HTML + Vanilla JS (optional) | Read-only monitoring panel, no framework dependencies |
| **Internal IPC** | gRPC + Unix Sockets / Named Pipes | High-performance inter-service communication |
| **Business Modules** | .NET 10 Class Libraries + AssemblyLoadContext | Hot module loading, sandbox isolation |
| **Config Storage** | SQLite (default) / PostgreSQL (cluster) | Lightweight but fully functional |
| **Declarative Config** | YAML + JSON Schema | Replaces traditional XML+SaltStack |
| **Container Runtime** | Docker Engine (official, unmodified) | Community standard, avoids vendor lock-in |
| **Container Orchestration** | docker compose (file generation) | One compose.yml per Agent |
| **Security Tokens** | JWT + NAbility (embedded capabilities) | HarmonyOS-inspired NAS security model |
| **Key Storage** | TPM 2.0 (preferred) / Software KeyStore (fallback) | Hardware security module integration |
| **File-Level Permissions** | POSIX ACL | Inherits Linux system permissions |
| **Data Classification** | NasDataLevel (L0-L4) | File/directory level labels |
| **Log SDK** | `Microsoft.Extensions.Logging` + Serilog | .NET standard logging infrastructure |
| **Distributed Tracing** | OpenTelemetry (.NET SDK) | OTLP protocol, industry standard |
| **Log Storage** | File rotation + embedded Loki + SQLite + Audit Vault | Separate storage by log type |
| **Audit Tamper-Proof** | Self-developed Audit Chain (SHA-256 + HMAC) | Blockchain concept, zero external dependencies |
| **Alert Notifications** | SMTP + Webhook + CLI output | Multi-channel notification |
| **Target Platform** | .NET 10 RID multi-target build | linux-x64, linux-arm64 |
| **Process Management** | Self-developed Service Bus + systemd adaptation | Unified Linux service management |

---

## 13. Deployment Architecture

### 13.1 Directory Structure

```
/srv/nas/                          # NAS data root (configurable)
├── config/
│   ├── nas.yaml                   # Main config file
│   ├── modules/                   # Module configs
│   │   ├── storage.yaml
│   │   ├── share.yaml
│   │   └── agent.yaml
│   ├── services/                  # Service definitions
│   │   ├── smb.yaml
│   │   ├── nfs.yaml
│   │   └── openclaw.yaml
│   └── alerts/                    # Alert rules
│       ├── disk.yaml
│       └── agent.yaml
├── agents/                        # Agent deployment directory
│   ├── openclaw/
│   │   ├── docker-compose.yml     # Generated orchestration file
│   │   ├── token.env              # Agent Token (600 permissions)
│   │   └── data/                  # Agent persistent data
│   ├── home-assistant/
│   │   ├── docker-compose.yml
│   │   └── data/
│   └── catalog/                   # Agent template directory
│       ├── openclaw.template.yaml
│       └── plex.template.yaml
├── data/                          # NAS shared data root
│   ├── media/                     # Media files
│   ├── documents/                 # Documents
│   └── backups/                   # Backups
├── logs/                          # Log storage
│   ├── system/                    # System logs (rotating)
│   ├── audit/                     # Audit logs (tamper-proof chain)
│   ├── access/                    # Access logs (SQLite)
│   └── agents/                    # Agent logs (Loki)
├── database/
│   ├── nas.db                     # Main config database (SQLite)
│   ├── metrics.db                 # Metrics time-series database
│   └── access.db                  # Access log database
└── keystore/
    ├── chain.key                  # Audit chain key
    ├── tls/                       # TLS certificates
    └── agent-secrets/             # Agent secrets (encrypted storage)
```

### 13.2 Docker Compose (Self-Deployment Reference)

```yaml
# docker-compose.yml — GNAS can also be containerized
version: '3.8'

services:
  gnas-core:
    image: gnas/core:latest
    container_name: gnas-core
    restart: unless-stopped
    network_mode: host
    privileged: true              # Requires hardware and Docker socket access
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

## 14. System Installation and Initialization Guide

### 14.1 Installation Methods

| Method | Scenario | Description |
|------|----------|------|
| **ISO Image Install** | x64 bare metal | Based on Debian 12 Live ISO, includes Debian Installer and all GNAS dependencies |
| **Script Install** | Existing Linux system | `curl -fsSL https://get.gnas.io | bash` one-click install |
| **Docker Self-Deploy** | Dev/test/lightweight | Run GNAS Core containerized, mount Docker Socket |

```
Installation method decision tree:
─────────────────
  Bare metal?
    ├── Yes → ISO image install (recommended)
    │        · Download gnas-debian12-{version}-amd64.iso
    │        · Create bootable USB with balenaEtcher/Rufus
    │        · Boot from USB → enter installation wizard
    │
    └── No → Docker environment already available?
              ├── Yes → Docker Compose deploy (simplest)
              │        · wget https://get.gnas.io/docker-compose.yml
              │        · docker compose up -d
              │
              └── No → Script install
                       · curl -fsSL https://get.gnas.io | bash
                       · Auto-detect platform → install .NET Runtime +
                         dependencies → deploy GNAS
```

### 14.2 ISO Installation Process

The ISO is generated by `eng/iso/build.sh` inside a privileged Debian 12 build container. The build pipeline first
publishes the GNAS API and CLI as `linux-x64` self-contained applications, then writes them via `live-build` into
the Live root filesystem, enabling Debian Installer, Docker Compose v2, and `gnas.service`.
Build artifacts and their SHA-256 checksum files are located in `artifacts/iso/`; the
`GNAS Debian ISO` workflow in GitHub Actions can be triggered manually and is automatically built at Release time.

```
┌─────────────────────────────────────────────────────────┐
│  GNAS Installer (TUI Setup Wizard)                     │
│                                                         │
│  Step 1: Language and Timezone Selection                │
│  ┌───────────────────────────────────────────────────┐ │
│  │  Language: [English ▾]                            │ │
│  │  Timezone: [Asia/Shanghai ▾]                      │ │
│  │  Keyboard: [US English ▾]                         │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Step 2: Network Configuration                         │
│  ┌───────────────────────────────────────────────────┐ │
│  │  Interface: eth0 [✓] Connected                    │ │
│  │  IP Config:  (●) DHCP  ( ) Static                 │ │
│  │  Hostname:   [gnas-nas_____________]              │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Step 3: Select System Disk                             │
│  ┌───────────────────────────────────────────────────┐ │
│  │  [sda] Samsung SSD 256GB — (●) System disk       │ │
│  │  [sdb] WD Red 4TB       — ( ) Data disk          │ │
│  │  [sdc] WD Red 4TB       — ( ) Data disk          │ │
│  │                                                    │ │
│  │  ⚠ System disk will be formatted, all data will   │ │
│  │    be lost                                         │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Step 4: Create Administrator Account                  │
│  ┌───────────────────────────────────────────────────┐ │
│  │  Username:     [admin____________]                │ │
│  │  Password:     [****************]                 │ │
│  │  Confirm:      [****************]                 │ │
│  │  Email:        [admin@example.com]                │ │
│  │                                                    │ │
│  │  Password strength: ████████████ Strong           │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Step 5: Confirm Installation                          │
│  ┌───────────────────────────────────────────────────┐ │
│  │  System disk: /dev/sda (Samsung SSD 256GB)        │ │
│  │  Hostname:    gnas-nas                            │ │
│  │  Admin:       admin                               │ │
│  │  Timezone:    Asia/Shanghai                       │ │
│  │                                                    │ │
│  │  After installation, visit http://gnas-nas:5000   │ │
│  │                                                    │ │
│  │  [ Start Install ]  [ Go Back ]                   │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 14.3 First Boot Wizard (Onboarding Wizard)

The first boot guide after system installation can be completed through both CLI and the embedded Dashboard:

```
After first boot, CLI automatically enters guided mode:

┌─────────────────────────────────────────────────────────┐
│  Welcome to GNAS v1.0.0!                                │
│                                                         │
│  It looks like this is your first time running GNAS.    │
│  Let's set up your NAS system.                          │
│                                                         │
│  Wizard Progress:                                       │
│  ┌───────────────────────────────────────────────────┐ │
│  │ 1. Network Initialization    [✓] Completed        │ │
│  │ 2. Storage Pool Creation     [→] In Progress     │ │
│  │ 3. Shared Folder Creation    [ ] Pending         │ │
│  │ 4. User Account Creation     [ ] Pending         │ │
│  │ 5. Basic Services Enable     [ ] Pending         │ │
│  │ 6. Complete                  [ ] Pending         │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘

Step 2 — Storage Pool Creation Wizard:
─────────────────────────────

$ gnas setup pool create

  The following available disks were found:

  [1] /dev/sdb  WD Red 4TB     (Free)      ● SATA
  [2] /dev/sdc  WD Red 4TB     (Free)      ● SATA
  [3] /dev/sdd  Seagate IronWolf 8TB (Free) ● SATA

  Please select disks to use (comma-separated, e.g., "1,2,3"): 1,2

  2 disks selected. Recommended RAID level: RAID 1 (Mirror)

  Please select RAID level:
  [1] RAID 1 (Recommended) — 4TB available, 1 disk fault tolerance
  [2] RAID 0 — 8TB available, no fault tolerance

  Select: 1

  Please select filesystem:
  [1] Btrfs (Recommended) — CoW, compression, snapshots
  [2] ext4 — Traditional, stable
  [3] XFS — Large file optimization

  Select: 1

  Creating... [████████████████████] 100%
  Storage pool "pool-main" created successfully

Step 3 — Create Default Shared Folders:
─────────────────────────────

  Create recommended shared folder structure for storage pool "pool-main"?

  pool-main/
  ├── data/media/        Media library
  ├── data/documents/    Documents
  ├── data/downloads/    Downloads
  ├── backup/            Backup target
  ├── appdata/           Agent data
  └── home/              User directories

  Create? [Y/n]: Y
  Created ✓

Step 4 — Optional: Create additional user accounts:
────────────────────────────────

  Create additional users? [y/N]: n

Step 5 — Enable Default Services:
───────────────────────

  The following services will run automatically at system startup:

  [✓] SMB/CIFS File Sharing (Port 445)
  [✓] NFS File Sharing (Port 2049)
  [ ] FTP File Sharing (Port 21)
  WebDAV sharing is not yet available; will be enabled after full
  authentication and client compatibility layer implementation
  [ ] Agent Marketplace (Docker container support)

  Press Enter to confirm or modify selection.

Step 6 — Complete:
────────────────

  ┌─────────────────────────────────────────────────────┐
  │  GNAS Initialization Complete!                      │
  │                                                     │
  │  System Information:                                │
  │    Hostname:   gnas-nas                             │
  │    Address:    http://gnas-nas:5000 (Management API)│
  │    Dashboard:  http://gnas-nas:5000/dashboard       │
  │    Storage:    pool-main (4TB, RAID 1, Btrfs)       │
  │    Shares:     media, documents, downloads          │
  │                                                     │
  │  Run 'gnas' to enter interactive TUI management     │
  │  Run 'gnas help' to view all available commands     │
  └─────────────────────────────────────────────────────┘
```

### 14.4 Configuration Initialization Details

On first boot, GNAS automatically generates initial configuration:

```yaml
# /srv/nas/config/nas.yaml — Auto-generated main config file
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
    # Self-signed certificate auto-generated on first startup

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

## 15. UPS Integration (Uninterruptible Power Supply)

The NAS system must support UPS to prevent data corruption from unexpected power outages.

### 15.1 NUT (Network UPS Tools) Integration

```
┌─────────────────────────────────────────────────────────┐
│  UPS Integration Architecture                           │
│                                                         │
│  ┌──────────┐     USB/Serial     ┌──────────────┐      │
│  │  UPS     │ ◄───────────────► │  GNAS NAS     │      │
│  │  Device  │                    │  (NUT Client  │      │
│  │  (APC/   │                    │   + Server)   │      │
│  │  Eaton/  │                    │               │      │
│  │  Cyber-  │                    │  · upsd       │      │
│  │  Power)  │                    │  · upsmon     │      │
│  └──────────┘                    └──────┬─────────┘      │
│                                         │                │
│                           ┌─────────────┴──────────┐    │
│                           │                        │    │
│                     ┌─────┴─────┐          ┌──────┴──┐ │
│                     │ Slave NAS │          │ Other    │ │
│                     │ (NUT      │          │ Devices  │ │
│                     │  Client)  │          │ (NUT     │ │
│                     │           │          │  Client) │ │
│                     └───────────┘          └─────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 15.2 Power Failure Handling Strategy

```
┌─────────────────────────────────────────────────────────┐
│  UPS Event Handling Process                             │
│                                                         │
│  Mains Power Lost (ONBATT)                              │
│        │                                                │
│        ▼                                                │
│  ┌──────────────────────────────────────────────┐      │
│  │ Phase 1: Immediate Response (0 seconds)       │      │
│  │ · Publish system.power.onbattery event        │      │
│  │ · All services notified: prepare degrade/     │      │
│  │   pause non-critical tasks                    │      │
│  │ · Dashboard / TUI displays battery status     │      │
│  └──────────────────┬───────────────────────────┘      │
│                     │                                   │
│        ┌────────────┴────────────┐                      │
│        │                         │                      │
│   Battery > 50%              Battery < 50%              │
│        │                         │                      │
│        ▼                         ▼                      │
│  Continue running         ┌──────────────────────┐    │
│  Publish periodic status  │ Phase 2: Safe Mode   │    │
│  (every 30s)              │ · Pause non-essential│    │
│                            │   services           │    │
│                            │ · Stop file indexing │    │
│                            │ · Stop Scrub         │    │
│                            │ · Stop backup tasks  │    │
│                            │ · Force sync         │    │
│                            │   filesystem         │    │
│                            │ · Warning alert      │    │
│                            └──────────┬───────────┘    │
│                                       │                │
│                              Battery < 20%             │
│                                       │                │
│                                       ▼                │
│                            ┌──────────────────────┐    │
│                            │ Phase 3: Prepare      │    │
│                            │ Shutdown              │    │
│                            │ · Stop all Agent      │    │
│                            │   containers          │    │
│                            │ · Unmount file shares │    │
│                            │   (SMB)               │    │
│                            │ · Stop all non-core   │    │
│                            │   services            │    │
│                            │ · Write audit log     │    │
│                            │ · sync + unmount      │    │
│                            │   filesystems         │    │
│                            │ · Critical alert      │    │
│                            └──────────┬───────────┘    │
│                                       │                │
│                            Battery < 5% or 2 minutes   │
│                                       │                │
│                                       ▼                │
│                            ┌──────────────────────┐    │
│                            │ Phase 4: Emergency    │    │
│                            │ Shutdown              │    │
│                            │ · systemctl poweroff  │    │
│                            │   (or shutdown /s)    │    │
│                            │ · Wait for UPS        │    │
│                            │   battery depletion   │    │
│                            └──────────────────────┘    │
│                                                         │
│  Power Restored (ONLINE)                                │
│        │                                                │
│        ▼                                                │
│  ┌──────────────────────────────────────────────┐      │
│  │ Recovery Process                              │      │
│  │ · Publish system.power.online event           │      │
│  │ · If system has shut down: BIOS setting       │      │
│  │   "Restore on AC"                             │      │
│  │ · After boot: filesystem check → service      │      │
│  │   recovery                                     │      │
│  │ · Info level notification: "Power restored"   │      │
│  └──────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────┘
```

### 15.3 UPS Configuration

```yaml
# /srv/nas/config/ups.yaml
ups:
  enabled: true
  driver: usbhid-ups              # NUT driver name
  device: /dev/usb/hiddev0
  
  # Battery thresholds
  battery:
    warning_level: 50             # Low battery warning (%)
    safe_mode_level: 50           # Enter safe mode (%)
    shutdown_level: 20            # Prepare shutdown (%)
    emergency_level: 5            # Emergency shutdown (%)
    
  # Shutdown delay (time for slave devices)
  shutdown_delay_seconds: 120
  
  # Notifications
  notify_on_events:
    - onbatt                       # Switched to battery power
    - lowbatt                      # Battery low
    - online                       # Power restored
    
  # Slave devices (optional)
  slaves:
    - hostname: gnas-backup
      port: 3493
    - network: 192.168.1.0/24
      port: 3493
```

### 15.4 CLI Commands

```bash
gnas ups status                  # UPS status (charge/load/remaining time)
gnas ups list                    # List connected UPS devices
gnas ups test                    # Trigger UPS self-test
gnas ups config set             # Configure UPS parameters
```

---

## 16. Security Enhancement Design

### 16.1 Brute Force Protection (Fail2Ban Integration)

```yaml
# /srv/nas/config/security/fail2ban.yaml
brute_force_protection:
  enabled: true
  
  jails:
    - name: api-auth
      filter: "Failed login attempt from <HOST>"
      source: /srv/nas/logs/access/auth.log
      max_retries: 5
      find_time_seconds: 300      # Within 5 minutes
      ban_time_seconds: 900       # Ban 15 minutes
      
    - name: smb-auth
      filter: "NT_STATUS_WRONG_PASSWORD from <HOST>"
      source: /srv/nas/logs/system/smb-auth.log
      max_retries: 3
      find_time_seconds: 60
      ban_time_seconds: 1800      # Ban 30 minutes
      
    - name: ssh-brute
      filter: "Failed password for .* from <HOST>"
      source: /var/log/auth.log
      max_retries: 5
      find_time_seconds: 300
      ban_time_seconds: 3600      # Ban 60 minutes

  # Ban escalation strategy
  recidive:
    enabled: true
    watch_jail: api-auth
    max_retries: 3                # After being banned 3 times
    ban_time_seconds: 86400       # Ban 24 hours
    
  # Whitelist
  whitelist:
    - 127.0.0.1
    - 192.168.1.0/24              # Internal network not banned
    - 10.0.0.0/8
```

### 16.2 API Rate Limiting Strategy

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
      message: "Too many login attempts, please try again later"
      
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
  
  # Distributed rate limiting (multi-node scenario)
  distributed:
    enabled: false
    redis: "redis://localhost:6379"
```

---

## Comparison with OMV Original Architecture

| Dimension | OMV Original | GNAS New Architecture |
|------|-----------|-------------|
| **Backend Language** | PHP | .NET 10 (C#) |
| **Web Service** | Nginx + PHP-FPM | Kestrel (ASP.NET Core built-in) |
| **Config Storage** | XML (config.xml) | SQLite + YAML declarative config |
| **Config Management** | SaltStack (masterless) | Self-developed Service Bus (event-driven) |
| **Client** | Web UI (Angular/ExtJS) | Desktop CLI Tool (gnas) + optional Web Dashboard |
| **Docker** | No native support (OMV-Extras plugin) | First-class citizen, deep integration |
| **Agent** | None | Agent Catalog + Token + Compose |
| **Target Platform** | Debian Only | Debian 12 x64 ISO + Linux ARM64 applications |
| **Permission Model** | Traditional Linux ACL | NAbility capability model + RBAC + ACL |
| **Service Management** | systemd | Service Bus + systemd |
| **IPC** | File/socket (implicit) | gRPC + Event Bus (explicit) |
| **Logging** | syslog (dispersed) | Unified 6-category logs + audit chain |
| **Observability** | None | OpenTelemetry + Loki + Dashboard |
| **Installation** | ISO only | ISO + Script + Docker multi-method |
| **UPS Support** | No native support | NUT deep integration + graded power-off strategy |
| **Audit** | syslog | Tamper-proof audit chain |
| **Snapshot Backup** | None | Btrfs/ZFS snapshots + 3-tier backup system |

---

## Architecture Decision Records (ADR)

### ADR-001: Choosing .NET 10 over Go/Rust

| Item | Content |
|----|------|
| **Status** | ✅ Decided |
| **Context** | NAS system involves extensive system calls, file operations, network protocol handling |
| **Decision** | Use .NET 10 (C# 14) |
| **Rationale** | 1. Mature Linux x64/ARM64 support 2. ASP.NET Core provides complete API/middleware ecosystem 3. Native gRPC support 4. Author team's tech stack is primarily .NET 5. Hot reload (AssemblyLoadContext) supports modularity |
| **Alternatives** | Go (good concurrency but weak generics ecosystem), Rust (extreme performance but low development efficiency) |

### ADR-002: Default Storage Using SQLite over PostgreSQL

| Item | Content |
|----|------|
| **Status** | ✅ Decided |
| **Context** | NAS system needs an embedded configuration storage |
| **Decision** | SQLite as default storage, PostgreSQL as optional replacement for cluster mode |
| **Rationale** | 1. Zero maintenance (no separate database process needed) 2. Small data volume (config + audit ~ few hundred MB) 3. Simple single-file backup/restore 4. Supports JSON queries 5. NAS typically runs as single node |
| **Trade-off** | Switch to PostgreSQL for multi-node cluster |

### ADR-003: Default Filesystem Choosing Btrfs over ZFS

| Item | Content |
|----|------|
| **Status** | ✅ Decided |
| **Context** | CoW filesystem is critical for snapshots/compression/self-healing |
| **Decision** | Btrfs as default recommendation, ZFS as advanced alternative |
| **Rationale** | 1. Btrfs built into Linux mainline kernel (no DKMS) 2. More flexible disk add/remove 3. Lower memory footprint 4. RAID 5/6 largely stable (kernel 5.15+) |
| **Trade-off** | ZFS provides more mature data integrity but requires DKMS and higher memory overhead. Advanced users can choose via `gnas pool create --fs zfs` |

### ADR-004: Docker Compose over Kubernetes

| Item | Content |
|----|------|
| **Status** | ✅ Decided |
| **Context** | Need container orchestration to manage Agents |
| **Decision** | Use docker compose (one compose file per Agent) |
| **Rationale** | 1. NAS single-node scenario doesn't need K8s complexity 2. TrueNAS SCALE's lesson migrating from K8s to Compose 3. Docker Compose is community standard 4. Declarative + easy to generate and modify |
| **Trade-off** | Does not support multi-node Agent orchestration, but this is not a core requirement for NAS scenarios |

### ADR-005: CLI as the Primary Management Interface

| Item | Content |
|----|------|
| **Status** | ✅ Decided |
| **Context** | Choice of NAS management interface |
| **Decision** | Desktop CLI Tool as primary management interface, Web Dashboard only as read-only monitoring panel |
| **Rationale** | 1. CLI is scripting-friendly for all operations 2. Pipeline-first design compatible with Unix philosophy 3. TUI provides sufficient interactive experience 4. Reduces web security attack surface 5. Avoids maintaining complex Web frontend |
| **Trade-off** | Provide optional Web Dashboard for non-technical users' view-only scenarios |

### ADR-006: Self-Developed Lightweight Audit Chain over Blockchain

| Item | Content |
|----|------|
| **Status** | ✅ Decided |
| **Context** | Audit logs need tamper-proofing |
| **Decision** | Self-developed SHA-256 chained hash + HMAC signature |
| **Rationale** | 1. Blockchain introduces unnecessary complexity and dependencies 2. Single-node NAS doesn't need distributed consensus 3. SHA-256 + HMAC is sufficient for tamper-proofing 4. Regular export to external storage as additional insurance |
| **Trade-off** | Does not provide multi-node audit consensus; advanced security needs can integrate with external SIEM |

### ADR-007: NAbility Capability Model Inspired by HarmonyOS

| Item | Content |
|----|------|
| **Status** | ✅ Decided |
| **Context** | NAS needs fine-grained permission control |
| **Decision** | Design `domain:resource:action:scope` four-level capability naming system |
| **Rationale** | 1. More granular than RBAC (precise to specific shared folders and operations) 2. Better readability than pure ACL (structured strings) 3. Wildcard matching supports flexible authorization levels 4. Bound with NasToken embedded capabilities for self-contained authentication |

---

## Changelog

| Version | Date | Changes |
|------|------|---------|
| **v2.1** | 2026-07-25 | Tech stack upgrade: .NET 9 → .NET 10 (C# 14) |
| **v2.0** | 2026-07-25 | Added: §5.3 Storage Pool Management, §5.4 Share Protocol, §5.5 Data Protection, §4.3 gRPC Proto, §14 Installation Initialization, §15 UPS, §16 Security Enhancement, ADR |
| **v1.0** | 2026-07-24 | Initial version: Complete architecture design across 13 sections |

---

> **Document Version**: Architecture v2.1  
> **Updated**: 2026-07-25  
> **Related Document**: [GNAS Implementation Prompts](gnas-implementation-prompts.md)

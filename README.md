<p align="center">
  <img src="https://img.shields.io/badge/platform-Linux%20x64%20%7C%20ARM64-blue" alt="Platform">
  <img src="https://img.shields.io/badge/runtime-.NET%2010-512BD4?logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/badge/ubuntu-24.04%20runner-brightgreen?logo=githubactions" alt="CI">
  <a href="https://github.com/GeneralLibrary/gnas/actions/workflows/ci.yml"><img src="https://github.com/GeneralLibrary/gnas/actions/workflows/ci.yml/badge.svg" alt="GNAS CI"></a>
  <img src="https://img.shields.io/badge/docker-compose%20v2-2496ED?logo=docker" alt="Docker Compose v2">
  <img src="https://img.shields.io/github/license/GeneralLibrary/gnas" alt="License">
</p>

<p align="center">
  <h1 align="center">GNAS — General NAS</h1>
  <p align="center"><strong>A modern, security-first Linux NAS operating system.</strong></p>
  <p align="center">Built with .NET 10 · Container-native · Fully observable</p>
</p>

---

## What is GNAS?

GNAS is an open-source NAS (Network Attached Storage) operating system designed for **home labs**, **SMB/studios**, and **edge deployments**. It runs bare-metal via a Debian 12 ISO or inside Docker for evaluation, exposing every management surface through a unified REST/gRPC API and a fast terminal CLI.

Unlike traditional NAS software, GNAS treats **Docker containers as first-class citizens** — deploy, supervise, and audit agents with the same security model that governs native services (SMB, NFS, rsync backups, snapshots).

<p align="center"><em>Inspired by HarmonyOS distributed security · Patterns from Unraid, TrueNAS SCALE, and Synology DSM</em></p>

---

## Features

<table>
<tr>
<td width="50%">

### Storage & File Systems
- Disk discovery, partitioning, RAID creation (mdadm)
- ext4 / XFS / Btrfs / ZFS formatting
- SMART monitoring and disk health prediction
- Storage quotas per share with enforcement

### File Sharing
- **SMB** — Samba with automatic `smbpasswd` sync
- **NFS** — `/etc/exports` generation and `exportfs` reload
- **FTP** — vsftpd config generation
- Recycle bin with time-based retention policies

### Data Protection
- Snapshot scheduling (btrfs / LVM thin)
- Rsync backup engine with cron scheduling
- Cloud backup (rclone-compatible targets)
- Point-in-time restore with dry-run support

</td>
<td width="50%">

### Security Model
- **NasToken** — capability-based access tokens (ATL3)
- **NAbility** — fine-grained permissions (`storage:share:media:read`)
- **Data classification levels** — internal, confidential, public
- ACL-enforced file operations
- Immutable audit chain (HMAC-chained SQLite vault)

### Container Orchestration
- Agent Catalog — YAML-based app templates
- Token Broker — automatic Agent token issuance
- Compose Generator — hardened `docker-compose.yml` with `read_only`, `no-new-privileges`, `cap_drop: ALL`
- Unified lifecycle management (native + container services)

### Observability
- Live host uptime, CPU/load, memory/swap, OOM, disk I/O, TCP, and per-interface traffic metrics
- SMART temperature/health, filesystem growth projection, RAID state, and NAS protocol sessions
- systemd service uptime/restarts and Docker CPU, memory, network, and block-I/O metrics
- Five-stage log pipeline (parse → filter → classify → enrich → dispatch)
- Loki integration for Agent log aggregation
- Dimension-aware alert engine with recovery notifications via system log, SMTP, and Webhook
- Prometheus `/metrics` endpoint
- Full TraceId propagation across all layers

</td>
</tr>
</table>

### CLI & API

| Interface | Description |
|-----------|-------------|
| `gnas` CLI | Interactive TUI dashboard + batch/JSON mode — pipeline-friendly |
| REST API | ASP.NET Core controllers at `http://localhost:5000/api/` |
| gRPC | High-performance IPC for storage, share, agent, and audit services |
| Web Dashboard | Optional static dashboard served at `/dashboard` |

---

## Quick Start

### Prerequisites

| Dependency | Minimum |
|------------|---------|
| Operating System | Linux x64 or ARM64 (kernel ≥ 5.15) |
| Runtime | .NET 10 SDK or Runtime |
| Docker | Docker Engine 24+ with Compose v2 |
| Permissions | Access to `/srv/nas`, Docker socket, and block devices |

### Docker Compose (Evaluation)

```bash
git clone https://github.com/GeneralLibrary/gnas.git
cd gnas
docker compose up -d --build
```

```bash
# Verify the API is alive
curl http://localhost:5000/api/health
```

```json
{"status":"ok"}
```

> **Note:** The Docker Compose file uses the host network and PID namespaces, mounts host procfs/sysfs read-only,
> mounts `/srv/nas` as the data root, and binds the Docker socket so monitoring describes the NAS host rather
> than the GNAS container. For NFS kernel-server support, use the bare-metal ISO installation.

```bash
# Stop
docker compose down
```

### Debian 12 ISO (Bare-Metal Installation)

Build a bootable hybrid ISO image (Legacy BIOS + UEFI):

```bash
VERSION=1.0.0 bash eng/iso/build.sh
```

Artifacts are written to `artifacts/iso/`:

```
gnas-debian12-1.0.0-amd64.iso
gnas-debian12-1.0.0-amd64.iso.sha256
```

Write to USB, boot, and follow the Debian installer:

```bash
sha256sum --check gnas-debian12-1.0.0-amd64.iso.sha256
sudo dd if=gnas-debian12-1.0.0-amd64.iso of=/dev/sdX bs=4M status=progress conv=fsync
```

GNAS starts automatically via `gnas.service` after installation, listening on `http://0.0.0.0:5000`.

> Pre-built ISOs are also available from the [GNAS Debian ISO](https://github.com/GeneralLibrary/gnas/actions/workflows/iso.yml) GitHub Actions workflow.

---

## CLI Usage

```bash
# System status dashboard (TUI)
gnas

# Health and metrics
gnas status
gnas status --watch --interval 5

# Historical metric query
curl -H "Authorization: Bearer $GNAS_TOKEN" \
  "http://localhost:5000/api/metrics/history?metric=system.cpu.usage.percent&limit=100"

# Disk management
gnas disk list --output json
gnas disk format /dev/sdb --fs btrfs --label nas-pool

# Create and manage shares
gnas share create media --path /srv/nas/media --protocols smb,nfs
gnas share list --output table

# Deploy an Agent container
gnas agent deploy nginx-basic \
  --image nginx:alpine \
  --agent-id web-nginx \
  --volume /srv/nas/www:/usr/share/nginx/html:ro

# Backup workflows
gnas backup task set media-backup \
  --source /srv/nas/media \
  --target /srv/nas/backup/media \
  --cron interval:60
gnas backup task run media-backup

# Restore with dry-run
gnas recovery start /srv/nas/media \
  --source /srv/nas/backup/media \
  --mode rsync --dry-run --confirm

# File operations
gnas file write /srv/nas/demo/hello.txt --content "hello gnas" --overwrite
gnas file read /srv/nas/demo/hello.txt

# Audit chain verification
gnas audit verify --output json

# Remote server
gnas --server http://192.168.1.100:5000 --token "$GNAS_TOKEN" service list
```

---

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  PRESENTATION       gnas CLI (TUI + Batch)  │  Web Dashboard │
├──────────────────────────────────────────────────────────────┤
│  API GATEWAY        REST (ASP.NET Core)  │  gRPC (IPC)       │
├──────────────────────────────────────────────────────────────┤
│  APPLICATION        Storage │ Share │ Net │ Agent │ Backup   │
│  MODULES            Update  │ Modules Host                    │
├──────────────────────────────────────────────────────────────┤
│  SECURITY           NasToken · NAbility · DataLevel · Audit  │
├──────────────────────────────────────────────────────────────┤
│  SERVICE BUS        Registry · Supervisor · Health · Events   │
├──────────────────────────────────────────────────────────────┤
│  AGENT INTEGRATION  Catalog · Token Broker · Compose Gen     │
├──────────────────────────────────────────────────────────────┤
│  PLATFORM           IDiskMgr · IFS · INetMgr · IProcMgr      │
│  ABSTRACTION        Linux x64  │  Linux ARM64                 │
├──────────────────────────────────────────────────────────────┤
│  OPERATING SYSTEM   Debian 12  │  Compatible Linux Distros    │
└──────────────────────────────────────────────────────────────┘
       │                                                      │
       ▼                                                      ▼
┌──────────────────┐                              ┌──────────────────────┐
│  OBSERVABILITY   │                              │  CROSS-CUTTING       │
│  LogPipeline     │                              │  Trace Propagation   │
│  AuditChain      │                              │  Security Audit      │
│  AlertEngine     │                              └──────────────────────┘
└──────────────────┘
```

| Layer | Project | Responsibility |
|-------|---------|----------------|
| **Core** | `GNAS.Core` | Models, abstractions, SQLite, configuration |
| **Platform** | `GNAS.Platform` | Linux disk, filesystem, process, network, user management |
| **Security** | `GNAS.Security` | Token issuance, identity, capabilities, key storage |
| **Service Bus** | `GNAS.ServiceBus` | Service registry, supervisor, event bus, health checks |
| **Modules** | `GNAS.Modules.*` | Storage, Share (SMB/NFS/FTP), Network, Agent, Backup, Update |
| **Agent** | `GNAS.Agent` | Agent catalog, token broker, Compose generator, log collector |
| **Observability** | `GNAS.Observability` | Log pipeline, audit chain, alert engine, Serilog, Prometheus |
| **API** | `GNAS.Api` | REST controllers, gRPC services, middleware (auth, rate-limit, audit, idempotency) |
| **CLI** | `GNAS.Cli` | Interactive TUI, batch commands, Spectre.Console rendering |

For a detailed architectural breakdown, see [docs/gnas-architecture.md](docs/gnas-architecture.md).

---

## Development

```bash
# Clone and restore
git clone https://github.com/GeneralLibrary/gnas.git
cd gnas
dotnet restore GNAS.slnx

# Build (warnings as errors)
dotnet build GNAS.slnx -c Release -warnaserror:CS

# Run all tests
dotnet test GNAS.slnx -c Release

# Run only unit tests (no Docker required)
dotnet test GNAS.slnx -c Release --filter "Category!=Integration"

# Run integration suite (requires Docker)
dotnet test tests/GNAS.Tests.Integration -c Release --filter "Category=Integration"
```

### Service Registration

```csharp
// Typical startup order in Program.cs
services.AddGnasCore();
services.AddPlatformServices();
services.AddGnasSecurity(configuration);
services.AddServiceBus();
services.AddModuleHost();
services.AddAgentServices();
services.AddObservability(configuration);
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `GNAS_DATA_ROOT` | `/srv/nas` | Data root directory |
| `GNAS_CONFIG_PATH` | `/srv/nas/config/nas.yaml` | Configuration file path |
| `ASPNETCORE_URLS` | `http://0.0.0.0:5000` | API listen address |
| `ASPNETCORE_ENVIRONMENT` | `Production` | ASP.NET environment |
| `GNAS_TOKEN` | — | CLI authentication token |
| `GNAS_API_ENDPOINT` | `http://host.docker.internal:5000` | Agent API endpoint |

Monitoring settings are read from `nas.yaml`: `monitoring:interval_seconds` (default `5`),
`monitoring:history_interval_seconds` (default `60`), `monitoring:smart_interval_seconds` (default `60`),
`monitoring:retention_days` (default `30`),
and `monitoring:services` (the systemd units whose uptime and restart count are tracked).
Alert delivery uses `alerts:smtp:*` and `alerts:webhook:urls`.

---

## Project Structure

```
GNAS.slnx
├── src/
│   ├── GNAS.Core/                  Core models, abstractions, database, configuration
│   ├── GNAS.Platform/              Linux platform implementations
│   ├── GNAS.Security/              NasToken, identity, permissions, key store
│   ├── GNAS.ServiceBus/            Service registry, supervisor, event bus, health
│   ├── GNAS.Agent/                 Agent catalog, token broker, Compose generator
│   ├── GNAS.Modules/               Module host and base class
│   ├── GNAS.Modules.Storage/       Disk, RAID, filesystem modules
│   ├── GNAS.Modules.Share/         SMB, NFS, FTP, recycle bin, quotas
│   ├── GNAS.Modules.Network/       Network and firewall configuration
│   ├── GNAS.Modules.Agent/         Agent orchestration module
│   ├── GNAS.Modules.Backup/        Snapshots, rsync, cloud backup
│   ├── GNAS.Modules.Update/        OTA updates and version checks
│   ├── GNAS.Observability/         Logging, audit chain, alerts, Serilog
│   ├── GNAS.Api/                   ASP.NET Core REST/gRPC gateway
│   └── GNAS.Cli/                   Command line tool and TUI
├── tests/
│   └── GNAS.Tests.Integration/     Integration and E2E tests
├── eng/iso/                        Debian ISO build scripts
├── docs/                           Architecture and design documentation
├── docker-compose.yml              Reference Compose deployment
├── docker-compose.test.yml         E2E test Compose file
└── Dockerfile                      Multi-stage container build
```

---

## Roadmap

| Milestone | Focus |
|-----------|-------|
| **v1.0** (current) | Core platform, security model, service bus, storage/share/backup modules, Agent orchestration, CLI + API |
| **v1.1** | Web management UI, multi-node clustering, LDAP integration |
| **v1.2** | Kubernetes Agent runtime, S3-compatible object storage, deduplication |
| **v2.0** | Distributed NAS fabric, cross-site replication, plugin marketplace |

---

## Contributing

Contributions are welcome. Before submitting a PR, please:

1. Open an issue to discuss the proposed change.
2. Ensure `dotnet build GNAS.slnx -c Release -warnaserror:CS` passes with zero warnings.
3. Run `dotnet test GNAS.slnx -c Release` and verify all tests pass.
4. Follow the existing code style and XML documentation conventions.

---

## License

GNAS is open-source software. See [LICENSE](LICENSE) for details.

---

<p align="center">
  <sub>Built with ❤️ for the home lab community.</sub>
</p>

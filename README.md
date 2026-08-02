<p align="center">
  <img src="https://img.shields.io/badge/platform-Linux%20x64%20%7C%20ARM64-blue" alt="Platform">
  <img src="https://img.shields.io/badge/runtime-.NET%2010-512BD4?logo=dotnet" alt=".NET 10">
  <a href="https://github.com/GeneralLibrary/fortos/actions/workflows/ci.yml"><img src="https://github.com/GeneralLibrary/fortos/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/GeneralLibrary/fortos/actions/workflows/iso.yml"><img src="https://github.com/GeneralLibrary/fortos/actions/workflows/iso.yml/badge.svg" alt="FortOS Debian ISO"></a>
  <img src="https://img.shields.io/github/license/GeneralLibrary/fortos" alt="License">
</p>

# FortOS

A modern, security-first **Linux NAS operating system**, built with .NET 10. Runs bare-metal via a Debian 12 ISO or inside Docker for evaluation. All management surfaces expose a unified REST/gRPC API and a terminal CLI; Docker containers are first-class citizens alongside native services (SMB, NFS, rsync, snapshots).

## Features

- **Storage**: disk discovery, RAID (mdadm), ext4/XFS/Btrfs/ZFS, SMART monitoring, per-share quotas
- **Sharing**: SMB, NFS, FTP, recycle bin with retention policies
- **Protection**: snapshots (btrfs/LVM thin), rsync backup, cloud backup, point-in-time restore
- **Security**: NasToken capability tokens, fine-grained NAbility permissions, data classification, HMAC-chained audit
- **Containers**: Agent Catalog, Token Broker, hardened Compose generation
- **Observability**: metrics (host/SMART/RAID/Docker), 5-stage log pipeline with Loki, alert engine, Prometheus `/metrics`

## CLI examples

```bash
fortos status --watch --interval 5                    # TUI dashboard & live metrics
fortos disk list --output json                        # disk inventory
fortos share create media --path /srv/nas/media --protocols smb,nfs
fortos agent deploy nginx-basic --image nginx:alpine  # container agent
fortos backup task set media-backup --source /srv/nas/media --cron interval:60
fortos audit verify --output json
```

REST: `http://localhost:5000/api/` · gRPC (HTTP/2): port `5001` · Web dashboard: `/dashboard`

## License

Open-source under the [LICENSE](LICENSE) file.

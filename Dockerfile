# GNAS API 网关容器镜像
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# 安装 NAS 工具链：共享守护进程（samba/nfs/vsftpd）、磁盘管理（smartmontools/mdadm/parted）、
# 文件系统工具（ext4/xfs/btrfs）与基础系统工具，保证平台层命令在容器内可用。
RUN apt-get update && apt-get install -y --no-install-recommends \
        samba \
        vsftpd \
        smartmontools \
        mdadm \
        parted \
        e2fsprogs \
        xfsprogs \
        btrfs-progs \
        util-linux \
        rsync \
        nftables \
        iptables \
        iproute2 \
        nut-client \
        rclone \
    && rm -rf /var/lib/apt/lists/*

# API 端口与容器模式共享协议端口（SMB 445/139、FTP 21）。
EXPOSE 5000 5001 445 139 21

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/GNAS.Api/GNAS.Api.csproj
RUN dotnet publish src/GNAS.Api/GNAS.Api.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GNAS.Api.dll"]

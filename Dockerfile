# FortOS API Gateway Container Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Install NAS toolchain: share daemons (samba/nfs/vsftpd), disk management (smartmontools/mdadm/parted),
# filesystem tools (ext4/xfs/btrfs) and basic system tools, ensuring platform-level commands are available inside the container.
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

# API port and container mode shared protocol ports (SMB 445/139, FTP 21).
EXPOSE 5000 5001 445 139 21

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/FortOS.Api/FortOS.Api.csproj
RUN dotnet publish src/FortOS.Api/FortOS.Api.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FortOS.Api.dll"]

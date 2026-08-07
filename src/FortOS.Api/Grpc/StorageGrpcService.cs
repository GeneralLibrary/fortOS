using Grpc.Core;
using FortOS.Core;
using FortOS.Modules.Storage;
using Proto = FortOS.Proto;

namespace FortOS.Api.Grpc;

/// <summary>Storage gRPC service.</summary>
public sealed class StorageGrpcService : Proto.StorageService.StorageServiceBase
{
    private readonly StorageModule storage;
    private readonly IDiskManager disks;

    /// <summary>Initializes the storage gRPC service.</summary>
    public StorageGrpcService(StorageModule storage, IDiskManager disks)
    {
        this.storage = storage;
        this.disks = disks;
    }

    /// <inheritdoc />
    public override async Task<Proto.DiskInfo> GetDisk(Proto.GetDiskRequest request, ServerCallContext context) => ToProto(await storage.GetDiskDetailAsync(request.DiskPath, context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override async Task<Proto.StorageOperationResult> CreatePartition(Proto.CreatePartitionRequest request, ServerCallContext context)
    {
        // Modifying the partition table is a destructive operation: consistent with the HTTP side, explicit confirmation is required to prevent accidentally erasing data.
        if (!request.Confirm)
        {
            return new Proto.StorageOperationResult { Success = false, Message = "Creating a partition modifies the disk partition table; explicit confirmation is required.", ErrorCode = Proto.ErrorCode.InvalidArgument };
        }

        var result = await storage.CreatePartitionAsync(request.DiskPath, new PartitionSpec { Name = request.Name, FileSystem = request.Filesystem, StartBytes = request.StartBytes, SizeBytes = request.SizeBytes }, context.CancellationToken).ConfigureAwait(false);
        return new Proto.StorageOperationResult { Success = result.Success, ResourceId = result.PartitionPath ?? string.Empty, Message = result.Message ?? string.Empty, ErrorCode = result.Success ? Proto.ErrorCode.Ok : Proto.ErrorCode.InvalidArgument };
    }

    /// <inheritdoc />
    public override async Task<Proto.StorageOperationResult> CreateRaid(Proto.CreateRaidRequest request, ServerCallContext context)
    {
        // Creating a RAID erases data on the selected disks: consistent with the HTTP side, explicit confirmation is required.
        if (!request.Confirm)
        {
            return new Proto.StorageOperationResult { Success = false, Message = "Creating a RAID array erases disk data; explicit confirmation is required.", ErrorCode = Proto.ErrorCode.InvalidArgument };
        }

        var result = await storage.CreateRaidAsync(ToCore(request.Level), request.DiskPaths.ToArray(), context.CancellationToken).ConfigureAwait(false);
        return new Proto.StorageOperationResult { Success = result.Success, ResourceId = result.PoolId ?? string.Empty, Message = result.Message ?? string.Empty, ErrorCode = result.Success ? Proto.ErrorCode.Ok : Proto.ErrorCode.RaidCreateFailed };
    }

    /// <inheritdoc />
    public override async Task<Proto.SmartData> GetSmartData(Proto.GetSmartDataRequest request, ServerCallContext context)
    {
        var smart = await disks.GetSmartDataAsync(request.DiskPath, context.CancellationToken).ConfigureAwait(false);
        return new Proto.SmartData { DiskPath = smart.DiskPath, Health = smart.Health, TemperatureCelsius = smart.TemperatureCelsius ?? 0, RawJson = smart.RawJson ?? string.Empty };
    }

    /// <inheritdoc />
    public override async Task WatchRaidRebuild(Proto.RaidRebuildRequest request, IServerStreamWriter<Proto.RebuildProgress> responseStream, ServerCallContext context)
    {
        // mdadm rebuild progress is surfaced through /proc/mdstat parsing in ListRaidsAsync,
        // not as events, so poll the RAID snapshot instead of subscribing to a topic nobody publishes.
        var poolId = request.PoolId.TrimEnd('/');
        double lastPercent = 0;
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var raids = await storage.ListRaidsAsync(context.CancellationToken).ConfigureAwait(false);
            var raid = raids.FirstOrDefault(r =>
                string.Equals(r.Name, poolId, StringComparison.OrdinalIgnoreCase)
                || string.Equals($"/dev/{r.Name}", poolId, StringComparison.OrdinalIgnoreCase));
            if (raid is not null)
            {
                if (raid.ProgressPercent is { } percent)
                {
                    lastPercent = percent;
                }

                // When the array is still present but idle (rebuild finished), ProgressPercent
                // is null; report the last observed value so a client never sees 100 → 0.
                await responseStream.WriteAsync(new Proto.RebuildProgress
                {
                    PoolId = request.PoolId,
                    PercentComplete = raid.ProgressPercent ?? lastPercent,
                }, context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                // The array disappeared from /proc/mdstat (rebuild finished or array detached):
                // keep reporting the last observed progress instead of regressing to 0.
                await responseStream.WriteAsync(new Proto.RebuildProgress
                {
                    PoolId = request.PoolId,
                    PercentComplete = lastPercent,
                }, context.CancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken).ConfigureAwait(false);
        }
    }

    private static Proto.DiskInfo ToProto(DiskInfo disk) => new() { Path = disk.Path, Model = disk.Model, Serial = disk.Serial, SizeBytes = disk.SizeBytes, InterfaceType = disk.InterfaceType, IsSsd = disk.IsSsd, SmartStatus = disk.SmartStatus, TemperatureCelsius = disk.TemperatureCelsius, UsedPercent = disk.UsedPercent };
    private static RaidLevel ToCore(Proto.RaidLevel level) => level switch { Proto.RaidLevel._0 => RaidLevel.Raid0, Proto.RaidLevel._1 => RaidLevel.Raid1, Proto.RaidLevel._5 => RaidLevel.Raid5, Proto.RaidLevel._6 => RaidLevel.Raid6, Proto.RaidLevel._10 => RaidLevel.Raid10, _ => RaidLevel.Unknown };
    private static Proto.PageInfo Page(int count) => new() { TotalCount = count, HasMore = false };
}

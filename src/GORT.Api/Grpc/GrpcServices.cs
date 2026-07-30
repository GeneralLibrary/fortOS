using System.Threading.Channels;
using Google.Protobuf;
using Grpc.Core;
using GORT.Core;
using GORT.Modules.Agent;
using GORT.Modules.Share;
using GORT.Modules.Storage;
using GORT.Observability.Logging;
using Proto = GORT.Proto;
using CoreLogEntry = GORT.Core.LogEntry;
using ProtoLogEntry = GORT.Proto.LogEntry;
using static GORT.Api.Grpc.GrpcMappings;

namespace GORT.Api.Grpc;

/// <summary>Storage gRPC service.</summary>
public sealed class StorageGrpcService : Proto.StorageService.StorageServiceBase
{
    private readonly StorageModule storage;
    private readonly IDiskManager disks;
    private readonly IEventBus events;

    /// <summary>Initializes the storage gRPC service.</summary>
    public StorageGrpcService(StorageModule storage, IDiskManager disks, IEventBus events)
    {
        this.storage = storage;
        this.disks = disks;
        this.events = events;
    }

    /// <inheritdoc />
    public override async Task<Proto.ListDisksResponse> ListDisks(Proto.ListDisksRequest request, ServerCallContext context)
    {
        var list = await storage.ListDisksAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new Proto.ListDisksResponse { Page = Page(list.Count) };
        response.Disks.AddRange(list.Select(ToProto));
        return response;
    }

    /// <inheritdoc />
    public override async Task<Proto.DiskInfo> GetDisk(Proto.GetDiskRequest request, ServerCallContext context) => ToProto(await storage.GetDiskDetailAsync(request.DiskPath, context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override async Task<Proto.StorageOperationResult> CreatePartition(Proto.CreatePartitionRequest request, ServerCallContext context)
    {
        var result = await storage.CreatePartitionAsync(request.DiskPath, new PartitionSpec { Name = request.Name, FileSystem = request.Filesystem, StartBytes = request.StartBytes, SizeBytes = request.SizeBytes }, context.CancellationToken).ConfigureAwait(false);
        return new Proto.StorageOperationResult { Success = result.Success, ResourceId = result.PartitionPath ?? string.Empty, Message = result.Message ?? string.Empty, ErrorCode = result.Success ? Proto.ErrorCode.Ok : Proto.ErrorCode.InvalidArgument };
    }

    /// <inheritdoc />
    public override async Task<Proto.StorageOperationResult> CreateRaid(Proto.CreateRaidRequest request, ServerCallContext context)
    {
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
    public override Task WatchRaidRebuild(Proto.RaidRebuildRequest request, IServerStreamWriter<Proto.RebuildProgress> responseStream, ServerCallContext context)
        => StreamEventsAsync(events, $"storage.raid.{request.PoolId}.*", async e => await responseStream.WriteAsync(new Proto.RebuildProgress { PoolId = request.PoolId, PercentComplete = 0 }, context.CancellationToken).ConfigureAwait(false), context.CancellationToken);

    private static Proto.DiskInfo ToProto(DiskInfo disk) => new() { Path = disk.Path, Model = disk.Model, Serial = disk.Serial, SizeBytes = disk.SizeBytes, InterfaceType = disk.InterfaceType, IsSsd = disk.IsSsd, SmartStatus = disk.SmartStatus, TemperatureCelsius = disk.TemperatureCelsius, UsedPercent = disk.UsedPercent };
    private static RaidLevel ToCore(Proto.RaidLevel level) => level switch { Proto.RaidLevel._0 => RaidLevel.Raid0, Proto.RaidLevel._1 => RaidLevel.Raid1, Proto.RaidLevel._5 => RaidLevel.Raid5, Proto.RaidLevel._6 => RaidLevel.Raid6, Proto.RaidLevel._10 => RaidLevel.Raid10, _ => RaidLevel.Unknown };
    private static Proto.PageInfo Page(int count) => new() { TotalCount = count, HasMore = false };
}

/// <summary>Share gRPC service.</summary>
public sealed class ShareGrpcService : Proto.ShareService.ShareServiceBase
{
    private readonly ShareModule shares;
    private readonly IEventBus events;

    /// <summary>Initializes the share gRPC service.</summary>
    public ShareGrpcService(ShareModule shares, IEventBus events) { this.shares = shares; this.events = events; }

    /// <inheritdoc />
    public override async Task<Proto.ShareResult> CreateShare(Proto.CreateShareRequest request, ServerCallContext context)
    {
        var created = await shares.CreateShareAsync(ToCore(request.Share), context.CancellationToken).ConfigureAwait(false);
        return new Proto.ShareResult { Success = true, ShareId = created.ShareId, Message = "created", ErrorCode = Proto.ErrorCode.Ok };
    }

    /// <inheritdoc />
    public override async Task<Proto.ListSharesResponse> ListShares(Proto.ListSharesRequest request, ServerCallContext context)
    {
        var list = await shares.ListSharesAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new Proto.ListSharesResponse { Page = new Proto.PageInfo { TotalCount = list.Count } };
        response.Shares.AddRange(list.Select(ToProto));
        return response;
    }

    /// <inheritdoc />
    public override async Task<Proto.ShareResult> DeleteShare(Proto.DeleteShareRequest request, ServerCallContext context)
    {
        await shares.DeleteShareAsync(request.ShareId, context.CancellationToken).ConfigureAwait(false);
        return new Proto.ShareResult { Success = true, ShareId = request.ShareId, Message = "deleted", ErrorCode = Proto.ErrorCode.Ok };
    }

    /// <inheritdoc />
    public override Task GetConnectedClients(Proto.ClientsRequest request, IServerStreamWriter<Proto.ConnectedClient> responseStream, ServerCallContext context)
        => StreamEventsAsync(events, $"share.{request.ShareId}.client.*", async e => await responseStream.WriteAsync(new Proto.ConnectedClient { ClientId = e.EventId.ToString(), ClientIp = string.Empty, Protocol = "unknown", ConnectedAtUnix = e.Timestamp.ToUnixTimeSeconds() }, context.CancellationToken).ConfigureAwait(false), context.CancellationToken);

    private static ShareDefinition ToCore(Proto.ShareDefinition share) => new() { ShareId = share.ShareId, Name = share.Name, Path = share.Path, Description = share.Description, ReadOnly = share.ReadOnly, Protocols = share.Protocols.Select(p => p.ToString().Replace("ShareProtocol", string.Empty).ToLowerInvariant()).ToArray() };
    private static Proto.ShareDefinition ToProto(ShareDefinition share)
    {
        var result = new Proto.ShareDefinition { ShareId = share.ShareId, Name = share.Name, Path = share.Path, Description = share.Description ?? string.Empty, ReadOnly = share.ReadOnly };
        result.Protocols.AddRange(share.Protocols.Select(ToProtoProtocol));
        return result;
    }
    private static Proto.ShareProtocol ToProtoProtocol(string value) => value.ToLowerInvariant() switch { "smb" => Proto.ShareProtocol.Smb, "nfs" => Proto.ShareProtocol.Nfs, "ftp" => Proto.ShareProtocol.Ftp, "sftp" => Proto.ShareProtocol.Sftp, "webdav" => Proto.ShareProtocol.Webdav, _ => Proto.ShareProtocol.Unknown };
}

/// <summary>Agent gRPC service.</summary>
public sealed class AgentGrpcService : Proto.AgentService.AgentServiceBase
{
    private readonly AgentModule agents;
    private readonly IAgentCatalog catalog;
    private readonly MemoryLogStore logs;

    /// <summary>Initializes the Agent gRPC service.</summary>
    public AgentGrpcService(AgentModule agents, IAgentCatalog catalog, MemoryLogStore logs) { this.agents = agents; this.catalog = catalog; this.logs = logs; }

    /// <inheritdoc />
    public override async Task<Proto.TemplateListResponse> ListAgentTemplates(Proto.TemplateListRequest request, ServerCallContext context)
    {
        var templates = await catalog.ListTemplatesAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new Proto.TemplateListResponse { Page = new Proto.PageInfo { TotalCount = templates.Count } };
        response.Templates.AddRange(templates.Select(ToProto));
        return response;
    }

    /// <inheritdoc />
    public override async Task<Proto.DeployAgentResponse> DeployAgent(Proto.DeployAgentRequest request, ServerCallContext context)
    {
        var ownerToken = ExtractBearerToken(context.RequestHeaders)
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing Bearer token."));
        var service = await agents.DeployAgentAsync(request.TemplateId, ToCore(request.Config), ownerToken, context.CancellationToken).ConfigureAwait(false);
        return new Proto.DeployAgentResponse { Success = true, AgentId = request.Config.AgentId, ComposeFilePath = service.ComposeFile ?? string.Empty, ErrorCode = Proto.ErrorCode.Ok };
    }

    /// <inheritdoc />
    public override async Task<Proto.AgentActionResult> StartAgent(Proto.AgentActionRequest request, ServerCallContext context) { await agents.StartAgentAsync(request.AgentId, context.CancellationToken).ConfigureAwait(false); return Ok(request.AgentId, "started"); }
    /// <inheritdoc />
    public override async Task<Proto.AgentActionResult> StopAgent(Proto.AgentActionRequest request, ServerCallContext context) { await agents.StopAgentAsync(request.AgentId, context.CancellationToken).ConfigureAwait(false); return Ok(request.AgentId, "stopped"); }
    /// <inheritdoc />
    public override async Task<Proto.AgentActionResult> RemoveAgent(Proto.AgentActionRequest request, ServerCallContext context) { await agents.RemoveAgentAsync(request.AgentId, context.CancellationToken).ConfigureAwait(false); return Ok(request.AgentId, "removed"); }

    /// <inheritdoc />
    public override async Task GetAgentLogs(Proto.AgentLogRequest request, IServerStreamWriter<ProtoLogEntry> responseStream, ServerCallContext context)
    {
        var entries = await logs.QueryAsync(new LogQuery { AgentId = request.AgentId, Limit = request.TailLines > 0 ? request.TailLines : 100 }, context.CancellationToken).ConfigureAwait(false);
        foreach (var entry in entries.OrderBy(e => e.Timestamp)) await responseStream.WriteAsync(GrpcMappings.ToProto(entry), context.CancellationToken).ConfigureAwait(false);
        while (request.Follow && !context.CancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken).ConfigureAwait(false);
        }
    }

    private static AgentConfig ToCore(Proto.AgentConfig config) => new() { AgentId = config.AgentId, DisplayName = config.DisplayName, ImageName = config.ImageName, Capabilities = config.Capabilities.ToArray(), VolumeMapping = config.VolumeMapping.Select(v => new VolumeMapping { HostPath = v.HostPath, ContainerPath = v.ContainerPath, ReadOnly = v.ReadOnly }).ToArray(), PortMapping = config.PortMapping.Select(p => new PortMapping { HostPort = p.HostPort, ContainerPort = p.ContainerPort, Protocol = p.Protocol }).ToArray(), ResourceQuota = config.ResourceQuota is null ? null : new ResourceQuota { CpuLimit = config.ResourceQuota.CpuLimit, MemoryLimitBytes = config.ResourceQuota.MemoryLimitBytes, IoWeight = config.ResourceQuota.IoWeight } };
    private static Proto.AgentTemplate ToProto(AgentTemplate template)
    {
        var result = new Proto.AgentTemplate { Id = template.Id, Name = template.Name, Version = template.Version, Description = template.Description ?? string.Empty, ComposeTemplate = template.ComposeTemplate };
        result.CapabilitiesRequired.AddRange(template.CapabilitiesRequired);
        result.Parameters.AddRange(template.Parameters.Select(p => new Proto.AgentTemplateParameter { Name = p.Name, Type = p.Type, Required = p.Required, DefaultValue = p.Default ?? string.Empty }));
        return result;
    }
    private static Proto.AgentActionResult Ok(string agentId, string message) => new() { Success = true, AgentId = agentId, Message = message, ErrorCode = Proto.ErrorCode.Ok };

    private static string? ExtractBearerToken(Metadata headers)
    {
        var value = headers.FirstOrDefault(h => string.Equals(h.Key, "authorization", StringComparison.OrdinalIgnoreCase))?.Value;
        if (value is null || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value[7..].Trim();
    }
}

/// <summary>Service bus gRPC service.</summary>
public sealed class ServiceBusGrpcService : Proto.ServiceBusService.ServiceBusServiceBase
{
    private readonly IServiceSupervisor supervisor;
    private readonly IEventBus events;

    /// <summary>Initializes the service bus gRPC service.</summary>
    public ServiceBusGrpcService(IServiceSupervisor supervisor, IEventBus events) { this.supervisor = supervisor; this.events = events; }

    /// <inheritdoc />
    public override async Task<Proto.ListServicesResponse> ListServices(Proto.ListServicesRequest request, ServerCallContext context)
    {
        var list = await supervisor.ListStatusesAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new Proto.ListServicesResponse { Page = new Proto.PageInfo { TotalCount = list.Count } };
        response.Services.AddRange(list.Select(s => new Proto.ServiceStatusInfo { ServiceId = s.ServiceId, Status = s.Status.ToString(), Type = s.Type.ToString(), Pid = s.Pid ?? 0, CpuPercent = s.CpuPercent, MemoryBytes = s.MemoryBytes, UptimeSeconds = (long)s.Uptime.TotalSeconds }));
        return response;
    }

    /// <inheritdoc />
    public override async Task<Proto.ServiceActionResult> StartService(Proto.ServiceActionRequest request, ServerCallContext context) { await supervisor.StartAsync(request.ServiceId, context.CancellationToken).ConfigureAwait(false); return ServiceOk(request.ServiceId, "started"); }
    /// <inheritdoc />
    public override async Task<Proto.ServiceActionResult> StopService(Proto.ServiceActionRequest request, ServerCallContext context) { await supervisor.StopAsync(request.ServiceId, context.CancellationToken).ConfigureAwait(false); return ServiceOk(request.ServiceId, "stopped"); }
    /// <inheritdoc />
    public override async Task<Proto.ServiceActionResult> RestartService(Proto.ServiceActionRequest request, ServerCallContext context) { await supervisor.RestartAsync(request.ServiceId, context.CancellationToken).ConfigureAwait(false); return ServiceOk(request.ServiceId, "restarted"); }

    /// <inheritdoc />
    public override Task WatchServiceEvents(Proto.ServiceWatchRequest request, IServerStreamWriter<Proto.ServiceEvent> responseStream, ServerCallContext context)
        => StreamEventsAsync(events, string.IsNullOrWhiteSpace(request.ServiceId) ? "service.*" : $"service.{request.ServiceId}.*", async e => await responseStream.WriteAsync(new Proto.ServiceEvent { EventId = e.EventId.ToString(), ServiceId = request.ServiceId, EventType = e.Type, Message = e.DataJson, TimestampUnix = e.Timestamp.ToUnixTimeSeconds() }, context.CancellationToken).ConfigureAwait(false), context.CancellationToken);

    private static Proto.ServiceActionResult ServiceOk(string serviceId, string message) => new() { Success = true, ServiceId = serviceId, Message = message, ErrorCode = Proto.ErrorCode.Ok };
}

/// <summary>Audit gRPC service.</summary>
public sealed class AuditGrpcService : Proto.AuditService.AuditServiceBase
{
    private readonly MemoryLogStore logs;
    private readonly IAuditChain chain;

    /// <summary>Initializes the audit gRPC service.</summary>
    public AuditGrpcService(MemoryLogStore logs, IAuditChain chain) { this.logs = logs; this.chain = chain; }

    /// <inheritdoc />
    public override async Task<Proto.LogQueryResponse> QueryLogs(Proto.LogQueryRequest request, ServerCallContext context)
    {
        var entries = await logs.QueryAsync(ToCore(request), context.CancellationToken).ConfigureAwait(false);
        var response = new Proto.LogQueryResponse { TotalCount = entries.Count, HasMore = false };
        response.Entries.AddRange(entries.Select(ToProto));
        return response;
    }

    /// <inheritdoc />
    public override async Task StreamLogs(Proto.LogQueryRequest request, IServerStreamWriter<ProtoLogEntry> responseStream, ServerCallContext context)
    {
        var query = ToCore(request) with { From = DateTimeOffset.UtcNow };
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var entries = await logs.QueryAsync(query, context.CancellationToken).ConfigureAwait(false);
            foreach (var entry in entries.OrderBy(e => e.Timestamp))
            {
                query = query with { From = entry.Timestamp.AddTicks(1) };
                await responseStream.WriteAsync(GrpcMappings.ToProto(entry), context.CancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task<Proto.VerifyChainResponse> VerifyChain(Proto.VerifyChainRequest request, ServerCallContext context)
    {
        var result = await chain.VerifyIntegrityAsync(request.FromUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(request.FromUnix) : null, request.ToUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(request.ToUnix) : null, context.CancellationToken).ConfigureAwait(false);
        return new Proto.VerifyChainResponse { Valid = result.IsValid, TotalEntries = result.TotalEntries, InvalidEntries = result.IsValid ? 0 : 1, FirstBrokenAt = result.BrokenAtSequence?.ToString() ?? string.Empty, Message = result.Message ?? string.Empty };
    }

    /// <inheritdoc />
    public override async Task ExportAuditChain(Proto.ExportRequest request, IServerStreamWriter<Proto.ExportChunk> responseStream, ServerCallContext context)
    {
        await responseStream.WriteAsync(new Proto.ExportChunk { Content = ByteString.Empty, Sequence = 0, Last = true }, context.CancellationToken).ConfigureAwait(false);
    }
}

internal static class GrpcMappings
{
    public static LogQuery ToCore(Proto.LogQueryRequest request) => new()
    {
        Category = Enum.TryParse<LogCategory>(request.Category, true, out var category) ? category : null,
        MinLevel = Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(request.MinLevel, true, out var level) ? level : null,
        From = request.FromUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(request.FromUnix) : null,
        To = request.ToUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(request.ToUnix) : null,
        SearchText = string.IsNullOrWhiteSpace(request.SearchText) ? null : request.SearchText,
        Tags = request.Tags.ToArray(),
        Limit = request.Limit > 0 ? request.Limit : 100,
        Offset = Math.Max(0, request.Offset),
        ServiceId = string.IsNullOrWhiteSpace(request.ServiceId) ? null : request.ServiceId,
        AgentId = string.IsNullOrWhiteSpace(request.AgentId) ? null : request.AgentId,
        TraceId = string.IsNullOrWhiteSpace(request.TraceId) ? null : request.TraceId,
    };

    public static ProtoLogEntry ToProto(CoreLogEntry entry)
    {
        var result = new ProtoLogEntry { LogId = entry.LogId, TimestampUnix = entry.Timestamp.ToUnixTimeSeconds(), Category = entry.Category.ToString(), Level = entry.Level.ToString(), SourceComponent = entry.SourceComponent, Message = entry.Message, TraceId = entry.TraceId ?? string.Empty, UserId = entry.UserId ?? string.Empty, AgentId = entry.AgentId ?? string.Empty };
        foreach (var property in entry.Properties) result.Properties[property.Key] = property.Value?.ToString() ?? string.Empty;
        result.Tags.AddRange(entry.Tags);
        return result;
    }

    public static async Task StreamEventsAsync(IEventBus eventBus, string topic, Func<EventEnvelope, Task> write, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<EventEnvelope>();
        using var subscription = eventBus.Subscribe(topic, (e, token) => channel.Writer.WriteAsync(e, token).AsTask());
        await foreach (var envelope in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await write(envelope).ConfigureAwait(false);
        }
    }
}

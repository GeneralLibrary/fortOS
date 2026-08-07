using Grpc.Core;
using FortOS.Core;
using FortOS.ServiceBus;
using static FortOS.Api.Grpc.GrpcMappings;
using Proto = FortOS.Proto;

namespace FortOS.Api.Grpc;

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

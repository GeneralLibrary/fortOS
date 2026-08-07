using Grpc.Core;
using FortOS.Core;
using FortOS.Modules.Agent;
using FortOS.Observability.Logging;
using static FortOS.Api.Grpc.GrpcMappings;
using Proto = FortOS.Proto;
using ProtoLogEntry = FortOS.Proto.LogEntry;

namespace FortOS.Api.Grpc;

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
        var ordered = entries.OrderBy(e => e.Timestamp).ToArray();
        foreach (var entry in ordered) await responseStream.WriteAsync(GrpcMappings.ToProto(entry), context.CancellationToken).ConfigureAwait(false);

        // follow mode: poll for new logs and push them (previously the loop only delayed and never pushed any log, so the streaming semantics were effectively dead).
        var from = ordered.Length > 0 ? ordered[^1].Timestamp : DateTimeOffset.UtcNow;
        while (request.Follow && !context.CancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken).ConfigureAwait(false);
            var fresh = await logs.QueryAsync(new LogQuery { AgentId = request.AgentId, From = from.AddTicks(1), Limit = 100 }, context.CancellationToken).ConfigureAwait(false);
            foreach (var entry in fresh.OrderBy(e => e.Timestamp))
            {
                await responseStream.WriteAsync(GrpcMappings.ToProto(entry), context.CancellationToken).ConfigureAwait(false);
                from = entry.Timestamp;
            }
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

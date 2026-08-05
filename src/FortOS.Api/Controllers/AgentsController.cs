using System.Collections.Concurrent;
using System.Text.Json;
using FortOS.Agent.Collector;
using FortOS.Core;
using FortOS.Modules.Agent;
using FortOS.Observability.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FortOS.Api.Controllers;

/// <summary>Agent controller.</summary>
[Route("api/agents")]
public sealed class AgentsController : FortOSControllerBase
{
    /// <summary>In-process deployment task states; agent pulls can take minutes, so deploys run in the background.</summary>
    private static readonly ConcurrentDictionary<string, AgentDeploymentStatus> Deployments = new(StringComparer.OrdinalIgnoreCase);

    private readonly AgentModule agents;

    /// <summary>Initializes the Agent controller.</summary>
    public AgentsController(AgentModule agents) => this.agents = agents;

    /// <summary>Agent deployment entry compatible with legacy protocol.</summary>
    [HttpPost]
    public Task<ServiceDefinition> DeployLegacy([FromBody] LegacyDeployAgentRequest request, CancellationToken ct)
        => agents.DeployAgentAsync(request.Template, BuildLegacyConfig(request), OwnerToken, ct);

    /// <summary>
    /// Deploy agent asynchronously: the image pull and compose bring-up run in the
    /// background so large agents do not time out the request; poll the deploy status
    /// endpoint (or GET /api/agents) until the service appears.
    /// </summary>
    [HttpPost("deploy")]
    public async Task<object> Deploy([FromBody] DeployAgentRequest request, [FromServices] IProcessManager process, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Config);
        var agentId = request.Config.AgentId;
        var ownerToken = OwnerToken; // capture inside the request context for the background task
        var status = new AgentDeploymentStatus("deploying", null, DateTimeOffset.UtcNow, Stage: "queued", Message: "准备部署…");
        Deployments[agentId] = status;
        _ = Task.Run(async () =>
        {
            try
            {
                // Stage 1: image availability. If already pulled locally the deploy is near-instant.
                status = status with { Stage = "pulling", Message = $"检查镜像 {request.Config.ImageName}…" };
                Deployments[agentId] = status;
                var imageExists = await ImageExistsAsync(process, request.Config.ImageName).ConfigureAwait(false);
                if (!imageExists)
                {
                    status = status with { Message = $"拉取镜像 {request.Config.ImageName}…(首次可能需要 10-30 分钟,取决于网络)" };
                    Deployments[agentId] = status;
                }

                // Stage 2: compose generation + container start (preflight pulls the image if missing).
                status = status with { Stage = "deploying", Message = "生成配置并启动容器…" };
                Deployments[agentId] = status;
                var service = await agents.DeployAgentAsync(request.TemplateId, request.Config, ownerToken, CancellationToken.None).ConfigureAwait(false);
                Deployments[agentId] = status with { Status = "success", Stage = "success", Message = "部署完成", ServiceId = service.ServiceId, FinishedAt = DateTimeOffset.UtcNow };
            }
            catch (OperationCanceledException)
            {
                Deployments[agentId] = status with { Status = "failed", Stage = "failed", Error = "Deployment was cancelled.", FinishedAt = DateTimeOffset.UtcNow };
            }
            catch (Exception ex)
            {
                Deployments[agentId] = status with { Status = "failed", Stage = "failed", Error = ex.Message, FinishedAt = DateTimeOffset.UtcNow };
            }
        }, CancellationToken.None);
        return Accepted(new { agentId, status = status.Status });
    }

    private static async Task<bool> ImageExistsAsync(IProcessManager process, string imageName)
    {
        var result = await process.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "docker",
            Arguments = $"image inspect {QuoteShell(imageName)}",
            TimeoutSeconds = 30,
        }, CancellationToken.None).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static string QuoteShell(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>Query the status of an asynchronous agent deployment.</summary>
    [HttpGet("{id}/deploy-status")]
    public object DeployStatus(string id)
    {
        var agentId = id.StartsWith("agent-", StringComparison.OrdinalIgnoreCase) ? id[6..] : id;
        return Deployments.TryGetValue(agentId, out var status)
            ? status
            : new AgentDeploymentStatus("unknown", null, null);
    }

    /// <summary>List agents.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ServiceDefinition>> List(CancellationToken ct) => agents.ListAgentsAsync(ct);

    /// <summary>Start agent.</summary>
    [HttpPost("{id}/start")]
    public async Task<object> Start(string id, CancellationToken ct) { await agents.StartAgentAsync(id, ct).ConfigureAwait(false); return new { success = true, agentId = id }; }

    /// <summary>Stop agent.</summary>
    [HttpPost("{id}/stop")]
    public async Task<object> Stop(string id, CancellationToken ct) { await agents.StopAgentAsync(id, ct).ConfigureAwait(false); return new { success = true, agentId = id }; }

    /// <summary>Delete agent.</summary>
    [HttpDelete("{id}")]
    public async Task<object> Delete(string id, CancellationToken ct) { await agents.RemoveAgentAsync(id, ct).ConfigureAwait(false); return new { success = true, agentId = id }; }

    /// <summary>Query agent logs.</summary>
    [HttpGet("{id}/logs")]
    public Task<IReadOnlyList<LogEntry>> Logs(string id, [FromServices] MemoryLogStore logs, CancellationToken ct, [FromQuery] int tail = 100)
        => logs.QueryAsync(new LogQuery { AgentId = id, Limit = tail }, ct);

    /// <summary>
    /// External access info for a deployed agent: published ports, environment
    /// variable names to wire chat channels / clients, and integration notes.
    /// </summary>
    [HttpGet("{id}/access")]
    public async Task<object> Access(string id, [FromServices] AgentModule agents, [FromServices] IConfiguration configuration, CancellationToken ct)
    {
        var info = await agents.GetAgentAccessAsync(id, ct).ConfigureAwait(false);
        var publicHost = configuration.GetValue("agent:public_host", string.Empty);
        var urls = string.IsNullOrWhiteSpace(publicHost)
            ? info.Ports.Select(p => new { name = $":{p.HostPort}", url = (string?)null }).ToArray()
            : info.Ports.Select(p => new { name = $"http://{publicHost}:{p.HostPort}", url = (string?)$"http://{publicHost}:{p.HostPort}" }).ToArray();
        return new
        {
            info.AgentId,
            info.TemplateId,
            info.DisplayName,
            info.ImageName,
            info.Ports,
            info.Env,
            info.AccessNotes,
            urls,
        };
    }

    /// <summary>List agent template catalog.</summary>
    [HttpGet("catalog")]
    public Task<IReadOnlyList<AgentTemplate>> Catalog([FromServices] IAgentCatalog catalog, CancellationToken ct) => catalog.ListTemplatesAsync(ct);

    /// <summary>Search agent templates.</summary>
    [HttpGet("catalog/search")]
    public Task<IReadOnlyList<AgentTemplate>> SearchCatalog([FromServices] IAgentCatalog catalog, [FromQuery] string query, CancellationToken ct)
        => catalog.SearchTemplatesAsync(query, ct);

    /// <summary>Install agent template.</summary>
    [HttpPost("catalog/install")]
    public Task<AgentTemplate> InstallCatalog([FromServices] IAgentCatalog catalog, [FromBody] InstallAgentTemplateRequest request, CancellationToken ct)
        => catalog.InstallTemplateAsync(request.Source, ct);

    /// <summary>Update agent template.</summary>
    [HttpPost("catalog/{templateId}/update")]
    public Task<AgentTemplate> UpdateCatalog([FromServices] IAgentCatalog catalog, string templateId, CancellationToken ct)
        => catalog.UpdateTemplateAsync(templateId, ct);

    private static AgentConfig BuildLegacyConfig(LegacyDeployAgentRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Template);
        var parameters = request.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var agentId = Read(parameters, "agentId", "agent-id", "id") ?? $"agent-{Guid.CreateVersion7():N}"[..14];
        var displayName = Read(parameters, "displayName", "display-name", "name") ?? agentId;
        var imageName = Read(parameters, "image", "imageName", "image-name")
            ?? throw new ArgumentException("Legacy deploy request must provide the image parameter.", nameof(request));
        var capabilities = SplitCsv(Read(parameters, "capabilities", "caps"));
        var volumes = SplitCsv(Read(parameters, "volumes", "volume"))
            .Select(ParseVolume)
            .ToArray();
        var ports = SplitCsv(Read(parameters, "ports", "port"))
            .Select(ParsePort)
            .ToArray();
        return new AgentConfig
        {
            AgentId = agentId,
            DisplayName = displayName,
            ImageName = imageName,
            Capabilities = capabilities,
            VolumeMapping = volumes,
            PortMapping = ports,
        };
    }

    private static string? Read(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string[] SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static VolumeMapping ParseVolume(string value)
    {
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3)
        {
            throw new ArgumentException("Volume mapping format should be host:container[:ro|rw].", nameof(value));
        }

        return new VolumeMapping
        {
            HostPath = parts[0],
            ContainerPath = parts[1],
            ReadOnly = parts.Length == 3 && string.Equals(parts[2], "ro", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static PortMapping ParsePort(string value)
    {
        var protocol = "tcp";
        var pair = value;
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            protocol = value[(slash + 1)..];
            pair = value[..slash];
        }

        var numbers = pair.Split(':', StringSplitOptions.TrimEntries);
        if (numbers.Length != 2
            || !int.TryParse(numbers[0], out var host)
            || !int.TryParse(numbers[1], out var container))
        {
            throw new ArgumentException("Port mapping format should be host:container[/tcp|udp].", nameof(value));
        }

        return new PortMapping { HostPort = host, ContainerPort = container, Protocol = protocol };
    }
}

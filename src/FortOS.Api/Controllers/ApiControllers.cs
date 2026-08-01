using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using FortOS.Agent.Collector;
using FortOS.Api.Authorization;
using FortOS.Api.Configuration;
using FortOS.Platform;
using Microsoft.AspNetCore.Authorization;
using FortOS.Core;
using FortOS.Modules.Agent;
using FortOS.Modules.Backup.Services;
using FortOS.Modules.Share;
using FortOS.Modules.Share.Services;
using FortOS.Modules.Storage;
using FortOS.Observability.Logging;
using FortOS.Security.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortOS.Api.Controllers;

/// <summary>API controller base class.</summary>
[ApiController]
public abstract class FortOSControllerBase : ControllerBase
{
    /// <summary>Current trace identifier.</summary>
    protected string? TraceId => HttpContext.Items["X-Trace-Id"]?.ToString();

    /// <summary>Current request token.</summary>
    protected string OwnerToken => Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? Request.Headers.Authorization.ToString()[7..].Trim()
        : Request.Headers["X-Nas-Token"].ToString();
}

/// <summary>Health check controller.</summary>
[Route("api/health")]
public sealed class HealthController : FortOSControllerBase
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    /// <summary>Returns API health status.</summary>
    [AllowAnonymous]
    [HttpGet]
    public object Get() => new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString(), uptime = DateTimeOffset.UtcNow - StartedAt, traceId = TraceId };
}

/// <summary>Disk controller.</summary>
[Route("api/disks")]
public sealed class DisksController : FortOSControllerBase
{
    private readonly StorageModule storage;

    /// <summary>Initializes the disk controller.</summary>
    public DisksController(StorageModule storage) => this.storage = storage;

    /// <summary>List disks.</summary>
    [HttpGet]
    public Task<IReadOnlyList<DiskInfo>> List(CancellationToken ct) => storage.ListDisksAsync(ct);

    /// <summary>Get disk by query parameters.</summary>
    [HttpGet("detail")]
    public Task<DiskInfo> GetByQuery([FromQuery] string path, CancellationToken ct) => storage.GetDiskDetailAsync(path, ct);

    /// <summary>Get disk by encoded path.</summary>
    [HttpGet("{encodedPath}")]
    public Task<DiskInfo> Get(string encodedPath, CancellationToken ct) => storage.GetDiskDetailAsync(DecodePath(encodedPath), ct);

    /// <summary>Execute SMART check.</summary>
    [HttpPost("smart-check")]
    public async Task<SmartData> Smart([FromBody] PathRequest request, [FromServices] IDiskManager disks, CancellationToken ct) => await disks.GetSmartDataAsync(request.Path, ct).ConfigureAwait(false);

    /// <summary>List active MD RAID arrays.</summary>
    [HttpGet("raids")]
    public Task<IReadOnlyList<RaidMetrics>> Raids(CancellationToken ct) => storage.ListRaidsAsync(ct);

    /// <summary>
    /// Whether the RAID tooling (mdadm) is installed on this host. The dashboard
    /// uses this to guide the user through installation when it is missing.
    /// </summary>
    [HttpGet("raid-capability")]
    public object RaidCapability() => new
    {
        available = PlatformCapabilities.SupportsHardwareRaid,
        tool = "mdadm",
    };

    /// <summary>
    /// Create a RAID array from the selected disks. Destructive: <see cref="CreateRaidRequest.Confirm"/>
    /// must be true, otherwise the request is rejected.
    /// </summary>
    [HttpPost("raids")]
    public async Task<object> CreateRaid([FromBody] CreateRaidRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
        {
            throw new ArgumentException("Creating a RAID array erases disk data; explicit confirmation is required.", nameof(request));
        }

        return await storage.CreateRaidAsync(request.Level, request.DiskPaths, ct).ConfigureAwait(false);
    }

    private static string DecodePath(string value)
    {
        var url = Uri.UnescapeDataString(value);
        try
        {
            var padded = url.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return url;
        }
    }
}

/// <summary>Share controller.</summary>
[Route("api/shares")]
public sealed class SharesController : FortOSControllerBase
{
    private readonly ShareModule shares;

    /// <summary>Initializes the share controller.</summary>
    public SharesController(ShareModule shares) => this.shares = shares;

    /// <summary>List shares.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ShareDefinition>> List(CancellationToken ct) => shares.ListSharesAsync(ct);

    /// <summary>Create share.</summary>
    [HttpPost]
    public Task<ShareDefinition> Create([FromBody] ShareDefinition share, CancellationToken ct) => shares.CreateShareAsync(share, ct);

    /// <summary>Delete share.</summary>
    [HttpDelete("{id}")]
    public async Task<object> Delete(string id, CancellationToken ct)
    {
        await shares.DeleteShareAsync(id, ct).ConfigureAwait(false);
        return new { success = true, shareId = id };
    }
}

/// <summary>Snapshot controller.</summary>
[Route("api/snapshots")]
public sealed class SnapshotsController : FortOSControllerBase
{
    /// <summary>Create snapshot.</summary>
    [HttpPost]
    public Task<CommandResult> Create([FromBody] SnapshotRequest request, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).CreateSnapshotAsync(request.Target, request.Name ?? Guid.CreateVersion7().ToString(), ct);

    /// <summary>List snapshots.</summary>
    [HttpGet]
    public Task<CommandResult> List([FromQuery] string target, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).ListSnapshotsAsync(target, ct);

    /// <summary>Restore snapshot.</summary>
    [HttpPost("{id}/restore")]
    public Task<CommandResult> Restore(string id, [FromBody] RestoreSnapshotRequest request, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).RestoreSnapshotAsync(id, request.Target, ct);
}

/// <summary>Recycle bin controller.</summary>
[Route("api/recycle")]
public sealed class RecycleController : FortOSControllerBase
{
    /// <summary>List recycle bin contents.</summary>
    [HttpGet("{share}")]
    public object List(string share) => Directory.Exists(Path.Combine(share, ".recycle"))
        ? Directory.EnumerateFiles(Path.Combine(share, ".recycle"), "*", SearchOption.AllDirectories).Select(f => new { id = Convert.ToBase64String(Encoding.UTF8.GetBytes(f)), path = f, size = new FileInfo(f).Length })
        : Array.Empty<object>();

    /// <summary>Restore recycle bin file (compatible with old routes).</summary>
    [HttpPost("restore/{id}")]
    public object RestoreLegacy(string id, [FromBody] RestoreRecycleRequest? request)
    {
        // Legacy route carries no share segment; derive the share root from the
        // ".recycle" marker inside the encoded source path, then apply the same
        // safety checks as the parameterized route.
        var share = InferShareRoot(DecodeRecyclePath(id));
        return RestoreCore(id, share, request?.TargetPath);
    }

    /// <summary>Restore recycle bin file.</summary>
    [HttpPost("{share}/restore/{id}")]
    public object Restore(string share, string id, [FromBody] RestoreRecycleRequest? request)
        => RestoreCore(id, Path.GetFullPath(share), request?.TargetPath);

    private static object RestoreCore(string id, string shareRoot, string? targetPath)
    {
        // Security: both source and destination are attacker-influenced strings, so every
        // restore is constrained to the share directory. All paths must be normalized via
        // Path.GetFullPath before the boundary check — otherwise a raw string prefix test
        // can be bypassed with ".." segments (e.g. "<share>/.recycle/../../etc/passwd").
        var source = Path.GetFullPath(DecodeRecyclePath(id));
        if (!IsPathUnderRoot(source, Path.Combine(shareRoot, ".recycle")))
        {
            throw new ArgumentException("Recycle bin item does not belong to the specified share path.", nameof(id));
        }

        if (!System.IO.File.Exists(source))
        {
            throw new FileNotFoundException("Recycle bin item no longer exists.", source);
        }

        var destination = string.IsNullOrWhiteSpace(targetPath) ? InferOriginalPath(source) : Path.GetFullPath(targetPath);
        if (!IsPathUnderRoot(destination, shareRoot))
        {
            throw new ArgumentException("Restore target must stay within the share directory.", nameof(targetPath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        System.IO.File.Move(source, destination, overwrite: true);
        return new { success = true };
    }

    /// <summary>Decodes a recycle-bin item id (base64 of the full source path).</summary>
    private static string DecodeRecyclePath(string id)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(id));
        }
        catch (FormatException)
        {
            throw new ArgumentException("Recycle bin item id is not a valid reference.", nameof(id));
        }
    }

    /// <summary>Returns whether <paramref name="path"/> is <paramref name="root"/> itself or nested inside it.</summary>
    /// <remarks>
    /// Both inputs are normalized with <see cref="Path.GetFullPath"/> before comparison so
    /// that ".." segments or redundant separators cannot smuggle a path outside the root.
    /// </remarks>
    private static bool IsPathUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        if (string.Equals(fullPath, fullRoot, StringComparison.Ordinal))
        {
            return true;
        }

        var boundary = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(boundary, StringComparison.Ordinal);
    }

    /// <summary>Empty recycle bin.</summary>
    [HttpDelete("empty")]
    public object EmptyRecycle([FromQuery] string share, [FromQuery] int retentionDays = 0)
        => new { deleted = new RecycleBinService().Cleanup(share, retentionDays) };

    /// <summary>Empty recycle bin by share path.</summary>
    [HttpDelete("{share}/empty")]
    public object EmptyRecycleByRoute(string share, [FromQuery] int retentionDays = 0)
        => new { deleted = new RecycleBinService().Cleanup(share, retentionDays) };

    /// <summary>Extracts the share root from a recycle bin path (the part before "/.recycle/").</summary>
    private static string InferShareRoot(string recyclePath)
    {
        var marker = $"{Path.DirectorySeparatorChar}.recycle{Path.DirectorySeparatorChar}";
        var index = recyclePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
        {
            throw new ArgumentException("Invalid recycle bin path format, missing share root.", nameof(recyclePath));
        }

        return recyclePath[..index];
    }

    private static string InferOriginalPath(string recyclePath)
    {
        var marker = $"{Path.DirectorySeparatorChar}.recycle{Path.DirectorySeparatorChar}";
        var index = recyclePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
        {
            throw new ArgumentException("Invalid recycle bin path format, cannot infer original path.", nameof(recyclePath));
        }

        var root = recyclePath[..index];
        var rest = recyclePath[(index + marker.Length)..];
        var slashIndex = rest.IndexOf(Path.DirectorySeparatorChar);
        if (slashIndex < 0 || slashIndex + 1 >= rest.Length)
        {
            throw new ArgumentException("Invalid recycle bin path format, missing original relative path.", nameof(recyclePath));
        }

        return Path.Combine(root, rest[(slashIndex + 1)..]);
    }
}

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

/// <summary>Agent push log controller.</summary>
[Route("api/agent/logs")]
public sealed class AgentLogsController : FortOSControllerBase
{
    /// <summary>Receive agent push logs.</summary>
    [HttpPost]
    public async Task<object> Push([FromBody] LogEntry[] entries, [FromServices] AgentLogCollector collector, CancellationToken ct)
    {
        foreach (var entry in entries) await collector.PushAsync(entry, ct).ConfigureAwait(false);
        return new { accepted = entries.Length };
    }
}

/// <summary>Service controller.</summary>
[Route("api/services")]
public sealed class ServicesController : FortOSControllerBase
{
    /// <summary>List service statuses.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ServiceStatusInfo>> List([FromServices] IServiceSupervisor supervisor, CancellationToken ct) => supervisor.ListStatusesAsync(ct);

    /// <summary>Start service.</summary>
    [HttpPost("{id}/start")]
    public async Task<object> Start(string id, [FromServices] IServiceSupervisor supervisor, CancellationToken ct) { await supervisor.StartAsync(id, ct).ConfigureAwait(false); return new { success = true, serviceId = id }; }

    /// <summary>Stop service.</summary>
    [HttpPost("{id}/stop")]
    public async Task<object> Stop(string id, [FromServices] IServiceSupervisor supervisor, CancellationToken ct) { await supervisor.StopAsync(id, ct).ConfigureAwait(false); return new { success = true, serviceId = id }; }
}

/// <summary>Log controller.</summary>
[Route("api/logs")]
public sealed class LogsController : FortOSControllerBase
{
    /// <summary>Query logs.</summary>
    [HttpGet]
    public Task<IReadOnlyList<LogEntry>> Query([FromServices] MemoryLogStore logs, [FromQuery] LogQuery query, CancellationToken ct) => logs.QueryAsync(query, ct);

    /// <summary>Stream logs via SSE.</summary>
    [HttpGet("stream")]
    public async Task Stream([FromServices] MemoryLogStore logs, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        var from = DateTimeOffset.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            var entries = await logs.QueryAsync(new LogQuery { From = from, Limit = 100 }, ct).ConfigureAwait(false);
            foreach (var entry in entries.OrderBy(e => e.Timestamp))
            {
                from = entry.Timestamp.AddTicks(1);
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(entry)}\n\n", ct).ConfigureAwait(false);
            }
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Audit controller.</summary>
[Route("api/audit")]
public sealed class AuditController : FortOSControllerBase
{
    /// <summary>Verify audit chain.</summary>
    [HttpGet("verify")]
    public Task<ChainVerificationResult> Verify([FromServices] IAuditChain chain, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
        => chain.VerifyIntegrityAsync(from, to, ct);
}

/// <summary>Metrics controller.</summary>
[Route("api/metrics")]
public sealed class MetricsController : FortOSControllerBase
{
    /// <summary>Return the legacy runtime and disk response retained for API compatibility.</summary>
    [HttpGet("current")]
    public async Task<object> Current([FromServices] IDiskManager disks, CancellationToken ct)
    {
        var diskList = await disks.ListDisksAsync(ct).ConfigureAwait(false);
        return new
        {
            gc = new
            {
                totalMemory = GC.GetTotalMemory(false),
                gen0 = GC.CollectionCount(0),
                gen1 = GC.CollectionCount(1),
                gen2 = GC.CollectionCount(2),
            },
            disks = diskList,
        };
    }

    /// <summary>Return the latest typed host, storage, network, service, and container snapshot.</summary>
    [HttpGet("system")]
    public Task<SystemMetricsSnapshot> System([FromServices] ISystemMetricsService metrics, CancellationToken ct)
        => metrics.GetCurrentAsync(ct);

    /// <summary>Return historical metrics.</summary>
    [HttpGet("history")]
    public Task<IReadOnlyList<MetricData>> History(
        [FromServices] ISystemMetricsService metrics,
        [FromQuery] string? metric,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
        => metrics.GetHistoryAsync(new SystemMetricHistoryQuery { MetricName = metric, From = from, Limit = limit }, ct);
}

/// <summary>Alert controller.</summary>
[Route("api/alerts")]
public sealed class AlertsController : FortOSControllerBase
{
    /// <summary>List active alerts.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ActiveAlert>> List([FromServices] IAlertEngine engine, CancellationToken ct) => engine.ListActiveAlertsAsync(ct);

    /// <summary>List alert rules.</summary>
    [HttpGet("rules")]
    public Task<IReadOnlyList<AlertRule>> Rules([FromServices] IAlertEngine engine, CancellationToken ct) => engine.ListRulesAsync(ct);

    /// <summary>Add alert rule.</summary>
    [HttpPost("rules")]
    public async Task<object> AddRule([FromBody] AlertRule rule, [FromServices] IAlertEngine engine, CancellationToken ct) { await engine.AddRuleAsync(rule, ct).ConfigureAwait(false); return new { success = true, ruleId = rule.RuleId }; }
}

/// <summary>UPS controller.</summary>
[Route("api/ups")]
public sealed class UpsController : FortOSControllerBase
{
    /// <summary>Get UPS status.</summary>
    [HttpGet("status")]
    public async Task<object> Status([FromServices] IProcessManager process, CancellationToken ct)
    {
        var result = await process.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "upsc", Arguments = "ups", TimeoutSeconds = 5 }, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? new { configured = true, raw = result.Stdout } : new { configured = false, message = "UPS not configured or upsc unavailable.", error = result.Stderr };
    }
}

/// <summary>Recovery controller.</summary>
[Route("api/recovery")]
public sealed class RecoveryController : FortOSControllerBase
{
    /// <summary>Start recovery process and execute immediately.</summary>
    [HttpPost("start")]
    public async Task<object> Start([FromBody] RecoveryRequest request, [FromServices] IEventBus bus, [FromServices] IProcessManager process, [FromServices] IFileSystem fileSystem, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        var mode = ResolveRecoveryMode(request);
        await bus.PublishAsync("system.recovery.started", "system.recovery.started", JsonSerializer.Serialize(request), ct).ConfigureAwait(false);
        var result = mode switch
        {
            "snapshot" => await RunSnapshotRecoveryAsync(request, process, fileSystem, ct).ConfigureAwait(false),
            "rsync" => await RunRsyncRecoveryAsync(request, process, ct).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unsupported recovery mode: {mode}", nameof(request)),
        };
        var success = result.ExitCode == 0;
        await bus.PublishAsync(
            success ? "system.recovery.completed" : "system.recovery.failed",
            success ? "system.recovery.completed" : "system.recovery.failed",
            JsonSerializer.Serialize(new { request.Target, mode, result.ExitCode, result.Stdout, result.Stderr }),
            ct).ConfigureAwait(false);
        return new
        {
            success,
            mode,
            request.Target,
            result.ExitCode,
            result.Stdout,
            result.Stderr
        };
    }

    private static string ResolveRecoveryMode(RecoveryRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Mode))
        {
            return request.Mode.Trim().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(request.SnapshotId) ? "rsync" : "snapshot";
    }

    private static Task<CommandResult> RunSnapshotRecoveryAsync(RecoveryRequest request, IProcessManager process, IFileSystem fileSystem, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SnapshotId))
        {
            throw new ArgumentException("Snapshot mode must provide snapshotId.", nameof(request));
        }

        var snapshots = new SnapshotService(process, fileSystem);
        return snapshots.RestoreSnapshotAsync(request.SnapshotId, request.Target, ct);
    }

    private static Task<CommandResult> RunRsyncRecoveryAsync(RecoveryRequest request, IProcessManager process, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Source))
        {
            throw new ArgumentException("Rsync mode must provide source.", nameof(request));
        }

        var rsync = new RsyncBackupService(process);
        return rsync.SyncAsync(request.Source, request.Target, request.DryRun, ct);
    }
}

/// <summary>Authentication controller.</summary>
[Route("api/auth")]
public sealed class AuthController : FortOSControllerBase
{
    /// <summary>Local login.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<object> Login([FromBody] LoginRequest request, [FromServices] IIdentityService identity, CancellationToken ct)
    {
        var result = await identity.AuthenticateLocalAsync(request.Username, request.Password, ct).ConfigureAwait(false);
        if (!result.Success) return Unauthorized(new { error = result.ErrorMessage, code = "LOGIN_FAILED", traceId = TraceId });
        if (!string.IsNullOrWhiteSpace(request.Totp))
        {
            var totp = await identity.AuthenticateTotpAsync(request.Username, request.Totp, ct).ConfigureAwait(false);
            if (!totp.Success) return Unauthorized(new { error = totp.ErrorMessage, code = "TOTP_FAILED", traceId = TraceId });
        }
        return new { token = result.NasToken, payload = result.TokenPayload };
    }

    /// <summary>
    /// Register local user.
    /// On first startup (no users yet), anonymous calls are allowed to create the first admin account;
    /// once users exist, <see cref="Middleware.NasTokenMiddleware"/> enforces token authentication, and user management capability is checked here.
    /// </summary>
    [BootstrapOnly]
    [HttpPost("register")]
    public async Task<object> Register([FromBody] RegisterRequest request, [FromServices] IIdentityService identity, CancellationToken ct)
    {
        if (HttpContext.Items["NasTokenPayload"] is NasTokenPayload payload && !payload.Capabilities.Satisfies("admin:user:create"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "User management permission required.", code = "FORBIDDEN", traceId = TraceId });
        }

        var result = await identity.CreateLocalUserAsync(request.Username, request.Password, request.DisplayName, request.Email, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage, code = "REGISTER_FAILED", traceId = TraceId });
        }

        return new { success = true, username = request.Username };
    }

    /// <summary>Refresh token.</summary>
    [HttpPost("refresh")]
    public async Task<object> Refresh([FromServices] ITokenManager tokens, CancellationToken ct)
    {
        var token = OwnerToken;
        return new { token = await tokens.RenewTokenAsync(token, ct).ConfigureAwait(false) };
    }
}

/// <summary>Configuration controller.</summary>
[Route("api/config")]
public sealed class ConfigController : FortOSControllerBase
{
    /// <summary>Return non-sensitive flat configuration.</summary>
    [HttpGet]
    public object Get([FromServices] IConfiguration configuration) => configuration.AsEnumerable()
        .Where(p => p.Value is not null && !ConfigMetaRegistry.IsSensitive(p.Key))
        .ToDictionary(p => p.Key, p => p.Value);

    /// <summary>
    /// Return metadata describing whitelisted, user-editable configuration:
    /// semantic categories, control types, options and validation hints.
    /// The dashboard renders its settings UI from this shape.
    /// </summary>
    [HttpGet("meta")]
    public object Meta() => new
    {
        categories = ConfigMetaRegistry.Categories,
        entries = ConfigMetaRegistry.Entries.Select(e => new
        {
            e.Key,
            e.Category,
            type = e.TypeName,
            e.Label,
            e.Description,
            e.Options,
            e.Min,
            e.Max,
            e.Step,
            e.DefaultValue,
            e.Order,
        }),
    };

    /// <summary>Write runtime configuration override value.</summary>
    [HttpPut("{key}")]
    public async Task<object> Put(string key, [FromBody] ConfigValue value, [FromServices] IDatabaseProvider database, CancellationToken ct)
    {
        if (ConfigMetaRegistry.IsSensitive(key)) throw new ArgumentException("Writing sensitive configuration through this endpoint is prohibited.", nameof(key));
        await database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Column names must match the schema created by DatabaseProvider migration 2
        // (config_key / value_ref / updated_at); the previous mismatch surfaced as 500.
        command.CommandText = "CREATE TABLE IF NOT EXISTS api_config(config_key TEXT PRIMARY KEY, value_ref TEXT NOT NULL, updated_at TEXT NOT NULL); INSERT OR REPLACE INTO api_config(config_key, value_ref, updated_at) VALUES($key, $value, $updated);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value.Value ?? string.Empty);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return new { success = true, key };
    }
}

/// <summary>Path request.</summary>
public sealed record PathRequest(string Path);
/// <summary>Create RAID request. <see cref="Confirm"/> acknowledges that disk data is erased.</summary>
public sealed record CreateRaidRequest(RaidLevel Level, string[] DiskPaths, bool Confirm);
/// <summary>Snapshot request.</summary>
public sealed record SnapshotRequest(string Target, string? Name);
/// <summary>Restore snapshot request.</summary>
public sealed record RestoreSnapshotRequest(string Target);
/// <summary>Restore recycle bin request.</summary>
public sealed record RestoreRecycleRequest(string TargetPath);
/// <summary>Deploy agent request.</summary>
public sealed record DeployAgentRequest(string TemplateId, AgentConfig Config);
/// <summary>Asynchronous agent deployment status.</summary>
public sealed record AgentDeploymentStatus(
    string Status,
    string? Error,
    DateTimeOffset? StartedAt,
    string? ServiceId = null,
    DateTimeOffset? FinishedAt = null,
    string Stage = "queued",
    string? Message = null);
/// <summary>Deploy request compatible with legacy CLI.</summary>
public sealed record LegacyDeployAgentRequest(string Template, Dictionary<string, string>? Parameters);
/// <summary>Install agent template request.</summary>
public sealed record InstallAgentTemplateRequest(string Source);
/// <summary>Recovery request.</summary>
public sealed record RecoveryRequest(string Target, string? Mode, string? Source, string? SnapshotId, bool DryRun = false);
/// <summary>Login request.</summary>
public sealed record LoginRequest(string Username, string Password, string? Totp);
/// <summary>Register user request.</summary>
public sealed record RegisterRequest(string Username, string Password, string? DisplayName, string? Email);
/// <summary>Config value.</summary>
public sealed record ConfigValue(string? Value);

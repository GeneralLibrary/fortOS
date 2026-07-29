using System.Text;
using System.Text.Json;
using GNAS.Agent.Collector;
using GNAS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using GNAS.Core;
using GNAS.Modules.Agent;
using GNAS.Modules.Backup.Services;
using GNAS.Modules.Share;
using GNAS.Modules.Share.Services;
using GNAS.Modules.Storage;
using GNAS.Observability.Logging;
using GNAS.Security.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GNAS.Api.Controllers;

/// <summary>API controller base class.</summary>
[ApiController]
public abstract class GnasControllerBase : ControllerBase
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
public sealed class HealthController : GnasControllerBase
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    /// <summary>Returns API health status.</summary>
    [AllowAnonymous]
    [HttpGet]
    public object Get() => new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString(), uptime = DateTimeOffset.UtcNow - StartedAt, traceId = TraceId };
}

/// <summary>Disk controller.</summary>
[Route("api/disks")]
public sealed class DisksController : GnasControllerBase
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
public sealed class SharesController : GnasControllerBase
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
public sealed class SnapshotsController : GnasControllerBase
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
public sealed class RecycleController : GnasControllerBase
{
    /// <summary>List recycle bin contents.</summary>
    [HttpGet("{share}")]
    public object List(string share) => Directory.Exists(Path.Combine(share, ".recycle"))
        ? Directory.EnumerateFiles(Path.Combine(share, ".recycle"), "*", SearchOption.AllDirectories).Select(f => new { id = Convert.ToBase64String(Encoding.UTF8.GetBytes(f)), path = f, size = new FileInfo(f).Length })
        : Array.Empty<object>();

    /// <summary>Restore recycle bin file (compatible with old routes).</summary>
    [HttpPost("restore/{id}")]
    public object RestoreLegacy(string id, [FromBody] RestoreRecycleRequest? request)
        => RestoreCore(id, request?.TargetPath);

    /// <summary>Restore recycle bin file.</summary>
    [HttpPost("{share}/restore/{id}")]
    public object Restore(string share, string id, [FromBody] RestoreRecycleRequest? request)
    {
        var source = Encoding.UTF8.GetString(Convert.FromBase64String(id));
        if (!source.StartsWith(Path.GetFullPath(share), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Recycle bin item does not belong to the specified share path.", nameof(id));
        }

        return RestoreCore(id, request?.TargetPath);
    }

    private static object RestoreCore(string id, string? targetPath)
    {
        var source = Encoding.UTF8.GetString(Convert.FromBase64String(id));
        var destination = string.IsNullOrWhiteSpace(targetPath) ? InferOriginalPath(source) : targetPath;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        System.IO.File.Move(source, destination, overwrite: true);
        return new { success = true };
    }

    /// <summary>Empty recycle bin.</summary>
    [HttpDelete("empty")]
    public object EmptyRecycle([FromQuery] string share, [FromQuery] int retentionDays = 0)
        => new { deleted = new RecycleBinService().Cleanup(share, retentionDays) };

    /// <summary>Empty recycle bin by share path.</summary>
    [HttpDelete("{share}/empty")]
    public object EmptyRecycleByRoute(string share, [FromQuery] int retentionDays = 0)
        => new { deleted = new RecycleBinService().Cleanup(share, retentionDays) };

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
public sealed class AgentsController : GnasControllerBase
{
    private readonly AgentModule agents;

    /// <summary>Initializes the Agent controller.</summary>
    public AgentsController(AgentModule agents) => this.agents = agents;

    /// <summary>Agent deployment entry compatible with legacy protocol.</summary>
    [HttpPost]
    public Task<ServiceDefinition> DeployLegacy([FromBody] LegacyDeployAgentRequest request, CancellationToken ct)
        => agents.DeployAgentAsync(request.Template, BuildLegacyConfig(request), OwnerToken, ct);

    /// <summary>Deploy agent.</summary>
    [HttpPost("deploy")]
    public Task<ServiceDefinition> Deploy([FromBody] DeployAgentRequest request, CancellationToken ct) => agents.DeployAgentAsync(request.TemplateId, request.Config, OwnerToken, ct);

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
public sealed class AgentLogsController : GnasControllerBase
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
public sealed class ServicesController : GnasControllerBase
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
public sealed class LogsController : GnasControllerBase
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
public sealed class AuditController : GnasControllerBase
{
    /// <summary>Verify audit chain.</summary>
    [HttpGet("verify")]
    public Task<ChainVerificationResult> Verify([FromServices] IAuditChain chain, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
        => chain.VerifyIntegrityAsync(from, to, ct);
}

/// <summary>Metrics controller.</summary>
[Route("api/metrics")]
public sealed class MetricsController : GnasControllerBase
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
public sealed class AlertsController : GnasControllerBase
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
public sealed class UpsController : GnasControllerBase
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
public sealed class RecoveryController : GnasControllerBase
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
public sealed class AuthController : GnasControllerBase
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
public sealed class ConfigController : GnasControllerBase
{
    /// <summary>Return non-sensitive flat configuration.</summary>
    [HttpGet]
    public object Get([FromServices] IConfiguration configuration) => configuration.AsEnumerable()
        .Where(p => p.Value is not null && !IsSensitive(p.Key))
        .ToDictionary(p => p.Key, p => p.Value);

    /// <summary>Write runtime configuration override value.</summary>
    [HttpPut("{key}")]
    public async Task<object> Put(string key, [FromBody] ConfigValue value, [FromServices] IDatabaseProvider database, CancellationToken ct)
    {
        if (IsSensitive(key)) throw new ArgumentException("Writing sensitive configuration through this endpoint is prohibited.", nameof(key));
        await database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS api_config(key TEXT PRIMARY KEY, value TEXT, updated_at TEXT); INSERT OR REPLACE INTO api_config(key, value, updated_at) VALUES($key, $value, $updated);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value.Value ?? string.Empty);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return new { success = true, key };
    }

    private static bool IsSensitive(string key) => key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("secret", StringComparison.OrdinalIgnoreCase) || key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("key", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Path request.</summary>
public sealed record PathRequest(string Path);
/// <summary>Snapshot request.</summary>
public sealed record SnapshotRequest(string Target, string? Name);
/// <summary>Restore snapshot request.</summary>
public sealed record RestoreSnapshotRequest(string Target);
/// <summary>Restore recycle bin request.</summary>
public sealed record RestoreRecycleRequest(string TargetPath);
/// <summary>Deploy agent request.</summary>
public sealed record DeployAgentRequest(string TemplateId, AgentConfig Config);
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

using System.Text;
using System.Text.Json;
using GNAS.Agent.Collector;
using GNAS.Core;
using GNAS.Modules.Agent;
using GNAS.Modules.Backup.Services;
using GNAS.Modules.Share;
using GNAS.Modules.Share.Services;
using GNAS.Modules.Storage;
using GNAS.Observability.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GNAS.Api.Controllers;

/// <summary>API 控制器基类。</summary>
[ApiController]
public abstract class GnasControllerBase : ControllerBase
{
    /// <summary>当前链路标识。</summary>
    protected string? TraceId => HttpContext.Items["X-Trace-Id"]?.ToString();

    /// <summary>当前请求令牌。</summary>
    protected string OwnerToken => Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? Request.Headers.Authorization.ToString()[7..].Trim()
        : Request.Headers["X-Nas-Token"].ToString();
}

/// <summary>健康检查控制器。</summary>
[Route("api/health")]
public sealed class HealthController : GnasControllerBase
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    /// <summary>返回 API 健康状态。</summary>
    [HttpGet]
    public object Get() => new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString(), uptime = DateTimeOffset.UtcNow - StartedAt, traceId = TraceId };
}

/// <summary>磁盘控制器。</summary>
[Route("api/disks")]
public sealed class DisksController : GnasControllerBase
{
    private readonly StorageModule storage;

    /// <summary>初始化磁盘控制器。</summary>
    public DisksController(StorageModule storage) => this.storage = storage;

    /// <summary>列出磁盘。</summary>
    [HttpGet]
    public Task<IReadOnlyList<DiskInfo>> List(CancellationToken ct) => storage.ListDisksAsync(ct);

    /// <summary>按查询参数获取磁盘。</summary>
    [HttpGet("detail")]
    public Task<DiskInfo> GetByQuery([FromQuery] string path, CancellationToken ct) => storage.GetDiskDetailAsync(path, ct);

    /// <summary>按编码路径获取磁盘。</summary>
    [HttpGet("{encodedPath}")]
    public Task<DiskInfo> Get(string encodedPath, CancellationToken ct) => storage.GetDiskDetailAsync(DecodePath(encodedPath), ct);

    /// <summary>执行 SMART 检查。</summary>
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

/// <summary>共享控制器。</summary>
[Route("api/shares")]
public sealed class SharesController : GnasControllerBase
{
    private readonly ShareModule shares;

    /// <summary>初始化共享控制器。</summary>
    public SharesController(ShareModule shares) => this.shares = shares;

    /// <summary>列出共享。</summary>
    [HttpGet]
    public Task<IReadOnlyList<ShareDefinition>> List(CancellationToken ct) => shares.ListSharesAsync(ct);

    /// <summary>创建共享。</summary>
    [HttpPost]
    public Task<ShareDefinition> Create([FromBody] ShareDefinition share, CancellationToken ct) => shares.CreateShareAsync(share, ct);

    /// <summary>删除共享。</summary>
    [HttpDelete("{id}")]
    public async Task<object> Delete(string id, CancellationToken ct)
    {
        await shares.DeleteShareAsync(id, ct).ConfigureAwait(false);
        return new { success = true, shareId = id };
    }
}

/// <summary>快照控制器。</summary>
[Route("api/snapshots")]
public sealed class SnapshotsController : GnasControllerBase
{
    /// <summary>创建快照。</summary>
    [HttpPost]
    public Task<CommandResult> Create([FromBody] SnapshotRequest request, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).CreateSnapshotAsync(request.Target, request.Name ?? Guid.CreateVersion7().ToString(), ct);

    /// <summary>列出快照。</summary>
    [HttpGet]
    public Task<CommandResult> List([FromQuery] string target, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).ListSnapshotsAsync(target, ct);

    /// <summary>恢复快照。</summary>
    [HttpPost("{id}/restore")]
    public Task<CommandResult> Restore(string id, [FromBody] RestoreSnapshotRequest request, [FromServices] IProcessManager process, [FromServices] IFileSystem fs, CancellationToken ct)
        => new SnapshotService(process, fs).RestoreSnapshotAsync(id, request.Target, ct);
}

/// <summary>回收站控制器。</summary>
[Route("api/recycle")]
public sealed class RecycleController : GnasControllerBase
{
    /// <summary>列出回收站内容。</summary>
    [HttpGet("{share}")]
    public object List(string share) => Directory.Exists(Path.Combine(share, ".recycle"))
        ? Directory.EnumerateFiles(Path.Combine(share, ".recycle"), "*", SearchOption.AllDirectories).Select(f => new { id = Convert.ToBase64String(Encoding.UTF8.GetBytes(f)), path = f, size = new FileInfo(f).Length })
        : Array.Empty<object>();

    /// <summary>恢复回收站文件。</summary>
    [HttpPost("restore/{id}")]
    public object Restore(string id, [FromBody] RestoreRecycleRequest request)
    {
        var source = Encoding.UTF8.GetString(Convert.FromBase64String(id));
        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetPath)!);
        System.IO.File.Move(source, request.TargetPath, overwrite: true);
        return new { success = true };
    }

    /// <summary>清空回收站。</summary>
    [HttpDelete("empty")]
    public object EmptyRecycle([FromQuery] string share, [FromQuery] int retentionDays = 0)
        => new { deleted = new RecycleBinService().Cleanup(share, retentionDays) };
}

/// <summary>Agent 控制器。</summary>
[Route("api/agents")]
public sealed class AgentsController : GnasControllerBase
{
    private readonly AgentModule agents;

    /// <summary>初始化 Agent 控制器。</summary>
    public AgentsController(AgentModule agents) => this.agents = agents;

    /// <summary>部署 Agent。</summary>
    [HttpPost("deploy")]
    public Task<ServiceDefinition> Deploy([FromBody] DeployAgentRequest request, CancellationToken ct) => agents.DeployAgentAsync(request.TemplateId, request.Config, OwnerToken, ct);

    /// <summary>列出 Agent。</summary>
    [HttpGet]
    public Task<IReadOnlyList<ServiceDefinition>> List(CancellationToken ct) => agents.ListAgentsAsync(ct);

    /// <summary>启动 Agent。</summary>
    [HttpPost("{id}/start")]
    public async Task<object> Start(string id, CancellationToken ct) { await agents.StartAgentAsync(id, ct).ConfigureAwait(false); return new { success = true, agentId = id }; }

    /// <summary>停止 Agent。</summary>
    [HttpPost("{id}/stop")]
    public async Task<object> Stop(string id, CancellationToken ct) { await agents.StopAgentAsync(id, ct).ConfigureAwait(false); return new { success = true, agentId = id }; }

    /// <summary>删除 Agent。</summary>
    [HttpDelete("{id}")]
    public async Task<object> Delete(string id, CancellationToken ct) { await agents.RemoveAgentAsync(id, ct).ConfigureAwait(false); return new { success = true, agentId = id }; }

    /// <summary>查询 Agent 日志。</summary>
    [HttpGet("{id}/logs")]
    public Task<IReadOnlyList<LogEntry>> Logs(string id, [FromServices] MemoryLogStore logs, CancellationToken ct, [FromQuery] int tail = 100)
        => logs.QueryAsync(new LogQuery { AgentId = id, Limit = tail }, ct);

    /// <summary>列出 Agent 模板目录。</summary>
    [HttpGet("catalog")]
    public Task<IReadOnlyList<AgentTemplate>> Catalog([FromServices] IAgentCatalog catalog, CancellationToken ct) => catalog.ListTemplatesAsync(ct);
}

/// <summary>Agent 推送日志控制器。</summary>
[Route("api/agent/logs")]
public sealed class AgentLogsController : GnasControllerBase
{
    /// <summary>接收 Agent 推送日志。</summary>
    [HttpPost]
    public async Task<object> Push([FromBody] LogEntry[] entries, [FromServices] AgentLogCollector collector, CancellationToken ct)
    {
        foreach (var entry in entries) await collector.PushAsync(entry, ct).ConfigureAwait(false);
        return new { accepted = entries.Length };
    }
}

/// <summary>服务控制器。</summary>
[Route("api/services")]
public sealed class ServicesController : GnasControllerBase
{
    /// <summary>列出服务状态。</summary>
    [HttpGet]
    public Task<IReadOnlyList<ServiceStatusInfo>> List([FromServices] IServiceSupervisor supervisor, CancellationToken ct) => supervisor.ListStatusesAsync(ct);

    /// <summary>启动服务。</summary>
    [HttpPost("{id}/start")]
    public async Task<object> Start(string id, [FromServices] IServiceSupervisor supervisor, CancellationToken ct) { await supervisor.StartAsync(id, ct).ConfigureAwait(false); return new { success = true, serviceId = id }; }

    /// <summary>停止服务。</summary>
    [HttpPost("{id}/stop")]
    public async Task<object> Stop(string id, [FromServices] IServiceSupervisor supervisor, CancellationToken ct) { await supervisor.StopAsync(id, ct).ConfigureAwait(false); return new { success = true, serviceId = id }; }
}

/// <summary>日志控制器。</summary>
[Route("api/logs")]
public sealed class LogsController : GnasControllerBase
{
    /// <summary>查询日志。</summary>
    [HttpGet]
    public Task<IReadOnlyList<LogEntry>> Query([FromServices] MemoryLogStore logs, [FromQuery] LogQuery query, CancellationToken ct) => logs.QueryAsync(query, ct);

    /// <summary>以 SSE 流式输出日志。</summary>
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

/// <summary>审计控制器。</summary>
[Route("api/audit")]
public sealed class AuditController : GnasControllerBase
{
    /// <summary>验证审计链。</summary>
    [HttpGet("verify")]
    public Task<ChainVerificationResult> Verify([FromServices] IAuditChain chain, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
        => chain.VerifyIntegrityAsync(from, to, ct);
}

/// <summary>指标控制器。</summary>
[Route("api/metrics")]
public sealed class MetricsController : GnasControllerBase
{
    /// <summary>返回当前指标。</summary>
    [HttpGet("current")]
    public async Task<object> Current([FromServices] IDiskManager disks, CancellationToken ct)
    {
        var diskList = await disks.ListDisksAsync(ct).ConfigureAwait(false);
        return new { gc = new { totalMemory = GC.GetTotalMemory(false), gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2) }, disks = diskList };
    }

    /// <summary>返回历史指标。</summary>
    [HttpGet("history")]
    public async Task<IReadOnlyList<object>> History([FromServices] IDatabaseProvider database, [FromQuery] string? metric, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        await database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT metric_name, value, unit, dimensions_json, timestamp FROM metrics WHERE ($metric IS NULL OR metric_name = $metric) ORDER BY timestamp DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$metric", (object?)metric ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) rows.Add(new { metricName = reader.GetString(0), value = reader.GetDouble(1), unit = reader.GetString(2), dimensions = reader.IsDBNull(3) ? null : reader.GetString(3), timestamp = reader.GetString(4) });
        return rows;
    }
}

/// <summary>告警控制器。</summary>
[Route("api/alerts")]
public sealed class AlertsController : GnasControllerBase
{
    /// <summary>列出活跃告警。</summary>
    [HttpGet]
    public Task<IReadOnlyList<ActiveAlert>> List([FromServices] IAlertEngine engine, CancellationToken ct) => engine.ListActiveAlertsAsync(ct);

    /// <summary>列出告警规则。</summary>
    [HttpGet("rules")]
    public Task<IReadOnlyList<AlertRule>> Rules([FromServices] IAlertEngine engine, CancellationToken ct) => engine.ListRulesAsync(ct);

    /// <summary>添加告警规则。</summary>
    [HttpPost("rules")]
    public async Task<object> AddRule([FromBody] AlertRule rule, [FromServices] IAlertEngine engine, CancellationToken ct) { await engine.AddRuleAsync(rule, ct).ConfigureAwait(false); return new { success = true, ruleId = rule.RuleId }; }
}

/// <summary>UPS 控制器。</summary>
[Route("api/ups")]
public sealed class UpsController : GnasControllerBase
{
    /// <summary>获取 UPS 状态。</summary>
    [HttpGet("status")]
    public async Task<object> Status([FromServices] IProcessManager process, CancellationToken ct)
    {
        var result = await process.ExecuteCommandAsync(new ProcessStartConfig { ExecutablePath = "upsc", Arguments = "ups", TimeoutSeconds = 5 }, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? new { configured = true, raw = result.Stdout } : new { configured = false, message = "未配置 UPS 或 upsc 不可用。", error = result.Stderr };
    }
}

/// <summary>恢复控制器。</summary>
[Route("api/recovery")]
public sealed class RecoveryController : GnasControllerBase
{
    /// <summary>启动系统恢复流程占位实现。</summary>
    [HttpPost("start")]
    public async Task<object> Start([FromBody] RecoveryRequest request, [FromServices] IEventBus bus, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        await bus.PublishAsync("system.recovery.started", "system.recovery.started", JsonSerializer.Serialize(request), ct).ConfigureAwait(false);
        return new { accepted = true, message = "恢复任务已记录；执行器将在后续阶段接管。" };
    }
}

/// <summary>认证控制器。</summary>
[Route("api/auth")]
public sealed class AuthController : GnasControllerBase
{
    /// <summary>本地登录。</summary>
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

    /// <summary>刷新令牌。</summary>
    [HttpPost("refresh")]
    public async Task<object> Refresh([FromServices] ITokenManager tokens, CancellationToken ct)
    {
        var token = OwnerToken;
        return new { token = await tokens.RenewTokenAsync(token, ct).ConfigureAwait(false) };
    }
}

/// <summary>配置控制器。</summary>
[Route("api/config")]
public sealed class ConfigController : GnasControllerBase
{
    /// <summary>返回非敏感扁平配置。</summary>
    [HttpGet]
    public object Get([FromServices] IConfiguration configuration) => configuration.AsEnumerable()
        .Where(p => p.Value is not null && !IsSensitive(p.Key))
        .ToDictionary(p => p.Key, p => p.Value);

    /// <summary>写入运行时配置覆盖值。</summary>
    [HttpPut("{key}")]
    public async Task<object> Put(string key, [FromBody] ConfigValue value, [FromServices] IDatabaseProvider database, CancellationToken ct)
    {
        if (IsSensitive(key)) throw new ArgumentException("禁止通过此端点写入敏感配置。", nameof(key));
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

/// <summary>路径请求。</summary>
public sealed record PathRequest(string Path);
/// <summary>快照请求。</summary>
public sealed record SnapshotRequest(string Target, string? Name);
/// <summary>恢复快照请求。</summary>
public sealed record RestoreSnapshotRequest(string Target);
/// <summary>恢复回收站请求。</summary>
public sealed record RestoreRecycleRequest(string TargetPath);
/// <summary>部署 Agent 请求。</summary>
public sealed record DeployAgentRequest(string TemplateId, AgentConfig Config);
/// <summary>恢复请求。</summary>
public sealed record RecoveryRequest(string Target, string? Mode);
/// <summary>登录请求。</summary>
public sealed record LoginRequest(string Username, string Password, string? Totp);
/// <summary>配置值。</summary>
public sealed record ConfigValue(string? Value);

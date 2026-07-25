using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using GNAS.Agent.Infrastructure;
using GNAS.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GNAS.Agent.Collector;

/// <summary>
/// 汇聚 Docker 事件、容器日志、卷文件日志与 API 推送日志。
/// </summary>
public sealed class AgentLogCollector : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Channel<LogEntry> _channel;
    private readonly ILogPipeline _logPipeline;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AgentLogCollector>? _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _containerLogSince = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _fileOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private bool _dockerUnavailableLogged;

    /// <summary>
    /// 初始化 Agent 日志采集器。
    /// </summary>
    public AgentLogCollector(ILogPipeline logPipeline, IEventBus eventBus, ILogger<AgentLogCollector>? logger = null)
    {
        _logPipeline = logPipeline;
        _eventBus = eventBus;
        _logger = logger;
        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>
    /// 接收 API 层推送的 Agent 日志。
    /// </summary>
    public ValueTask PushAsync(LogEntry entry, CancellationToken ct) => _channel.Writer.WriteAsync(Enrich(entry), ct);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new[]
        {
            Task.Run(() => RunDockerEventsAsync(stoppingToken), CancellationToken.None),
            Task.Run(() => RunDockerLogsAsync(stoppingToken), CancellationToken.None),
            Task.Run(() => RunVolumeTailAsync(stoppingToken), CancellationToken.None),
            Task.Run(() => FlushLoopAsync(stoppingToken), CancellationToken.None),
        };
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RunDockerEventsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!DockerExists())
            {
                await WarnDockerUnavailableAsync(ct).ConfigureAwait(false);
                await DelayAsync(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
                continue;
            }

            using var process = CreateDockerProcess("events --format {{json .}} --filter type=container");
            try
            {
                process.Start();
                while (!ct.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        await HandleDockerEventAsync(line, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await WarnDockerUnavailableAsync(ct).ConfigureAwait(false);
                _logger?.LogWarning(ex, "Docker events 采集不可用。");
            }
            finally
            {
                KillProcess(process);
            }

            await DelayAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
    }

    private async Task RunDockerLogsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (DockerExists())
            {
                foreach (var agentId in GetKnownAgentIds())
                {
                    await PullContainerLogsAsync(agentId, ct).ConfigureAwait(false);
                }
            }
            else
            {
                await WarnDockerUnavailableAsync(ct).ConfigureAwait(false);
            }

            await DelayAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }
    }

    private async Task RunVolumeTailAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var agentId in GetKnownAgentIds())
            {
                var logDir = Path.Combine(AgentPaths.AgentsRoot, agentId, "logs");
                if (!Directory.Exists(logDir))
                {
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(logDir, "*.jsonl"))
                {
                    await TailFileAsync(agentId, path, ct).ConfigureAwait(false);
                }
            }

            await DelayAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        var batch = new List<LogEntry>(1000);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!ct.IsCancellationRequested)
        {
            while (batch.Count < 1000 && _channel.Reader.TryRead(out var entry))
            {
                if (Remember(entry.LogId))
                {
                    batch.Add(entry);
                }
            }

            if (batch.Count > 0)
            {
                foreach (var entry in batch)
                {
                    await _logPipeline.ProcessAsync(entry, ct).ConfigureAwait(false);
                }

                batch.Clear();
                continue;
            }

            try
            {
                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task HandleDockerEventAsync(string json, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var action = GetString(root, "Action") ?? GetString(root, "status") ?? string.Empty;
        var (agentId, name) = ExtractAgentIdentity(root);
        if (agentId is null)
        {
            return;
        }

        var level = action.Equals("die", StringComparison.OrdinalIgnoreCase) && GetExitCode(root) != "0" ? LogLevel.Error : LogLevel.Information;
        await _channel.Writer.WriteAsync(new LogEntry
        {
            Category = LogCategory.Agent,
            Level = level,
            SourceComponent = "docker-events",
            AgentId = agentId,
            Message = $"Docker container {name ?? agentId} event: {action}",
            Properties = new Dictionary<string, object> { ["dockerEvent"] = json },
        }, ct).ConfigureAwait(false);

        var suffix = action switch
        {
            "start" => "started",
            "die" when level == LogLevel.Error => "crashed",
            "die" or "stop" or "kill" => "stopped",
            _ => null,
        };
        if (suffix is not null)
        {
            await _eventBus.PublishAsync($"agent.{agentId}.{suffix}", $"agent.{suffix}", JsonSerializer.Serialize(new { agentId, action }), ct).ConfigureAwait(false);
        }
    }

    private async Task PullContainerLogsAsync(string agentId, CancellationToken ct)
    {
        var since = _containerLogSince.GetOrAdd(agentId, DateTimeOffset.UtcNow.AddMinutes(-1));
        using var process = CreateDockerProcess($"logs --since {since:O} --timestamps gnas-{agentId}");
        try
        {
            process.Start();
            var text = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            _containerLogSince[agentId] = DateTimeOffset.UtcNow;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                await _channel.Writer.WriteAsync(ParseLogLine(line, agentId, "docker-logs"), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "容器日志采集失败: {AgentId}", agentId);
        }
        finally
        {
            KillProcess(process);
        }
    }

    private async Task TailFileAsync(string agentId, string path, CancellationToken ct)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var offset = _fileOffsets.GetValueOrDefault(path);
        if (offset > stream.Length)
        {
            offset = 0;
        }

        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, leaveOpen: true);
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            await _channel.Writer.WriteAsync(ParseLogLine(line, agentId, "agent-volume"), ct).ConfigureAwait(false);
        }

        _fileOffsets[path] = stream.Position;
    }

    private LogEntry ParseLogLine(string line, string agentId, string source)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<LogEntry>(line, JsonOptions);
            if (entry is not null)
            {
                return Enrich(entry with { AgentId = entry.AgentId ?? agentId, SourceComponent = string.IsNullOrWhiteSpace(entry.SourceComponent) ? source : entry.SourceComponent });
            }
        }
        catch (JsonException)
        {
        }

        return Enrich(new LogEntry
        {
            Category = LogCategory.Agent,
            Level = LogLevel.Information,
            SourceComponent = source,
            AgentId = agentId,
            Message = line,
        });
    }

    private LogEntry Enrich(LogEntry entry) => entry with
    {
        Category = LogCategory.Agent,
        TraceId = string.IsNullOrWhiteSpace(entry.TraceId) ? Activity.Current?.TraceId.ToString() : entry.TraceId,
    };

    private bool Remember(string logId)
    {
        lock (_seen)
        {
            if (!_seen.Add(logId))
            {
                return false;
            }

            _seenOrder.Enqueue(logId);
            while (_seenOrder.Count > 50_000)
            {
                _seen.Remove(_seenOrder.Dequeue());
            }

            return true;
        }
    }

    private async Task WarnDockerUnavailableAsync(CancellationToken ct)
    {
        if (_dockerUnavailableLogged)
        {
            return;
        }

        _dockerUnavailableLogged = true;
        await _logPipeline.ProcessAsync(new LogEntry
        {
            Category = LogCategory.System,
            Level = LogLevel.Warning,
            SourceComponent = "GNAS.Agent.AgentLogCollector",
            Message = "Docker CLI 不可用，Agent Docker 日志采集通道已降级。",
        }, ct).ConfigureAwait(false);
    }

    private static IEnumerable<string> GetKnownAgentIds()
    {
        if (!Directory.Exists(AgentPaths.AgentsRoot))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(AgentPaths.AgentsRoot))
        {
            var name = Path.GetFileName(directory);
            if (!string.Equals(name, "catalog", StringComparison.OrdinalIgnoreCase))
            {
                yield return name;
            }
        }
    }

    private static Process CreateDockerProcess(string arguments) => new()
    {
        StartInfo = new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        },
    };

    private static bool DockerExists()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return paths.Any(path => File.Exists(Path.Combine(path, "docker")));
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? GetExitCode(JsonElement root)
        => TryGetAttribute(root, "exitCode") ?? TryGetAttribute(root, "exit_code");

    private static (string? AgentId, string? Name) ExtractAgentIdentity(JsonElement root)
    {
        var name = TryGetAttribute(root, "name")?.TrimStart('/');
        var label = TryGetAttribute(root, "gnas.agent.id") ?? TryGetAttribute(root, "com.gnas.agent.id");
        if (!string.IsNullOrWhiteSpace(label))
        {
            return (label, name);
        }

        if (!string.IsNullOrWhiteSpace(name) && name.StartsWith("gnas-", StringComparison.OrdinalIgnoreCase))
        {
            return (name[5..], name);
        }

        return (null, name);
    }

    private static string? TryGetAttribute(JsonElement root, string key)
    {
        if (root.TryGetProperty("Actor", out var actor) && actor.TryGetProperty("Attributes", out var attributes) && attributes.TryGetProperty(key, out var value))
        {
            return value.GetString();
        }

        return null;
    }
}

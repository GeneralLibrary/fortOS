using System.Globalization;
using System.Runtime.Versioning;
using FortOS.Core;
using FortOS.Platform.Execution;
using Microsoft.Extensions.Logging;

namespace FortOS.Platform.Linux.Monitoring;

/// <summary>
/// Linux system collector backed by procfs, sysfs, systemd, SMART, and the Docker CLI.
/// Optional integrations are isolated so one unavailable subsystem cannot suppress host metrics.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSystemMetricsCollector : ISystemMetricsCollector
{
    private static readonly string[] DefaultServices =
    [
        "fortos.service", "docker.service", "containerd.service", "smbd.service",
        "nmbd.service", "nfs-server.service", "vsftpd.service", "ssh.service"
    ];

    private readonly IDiskManager _diskManager;
    private readonly IFortOSConfiguration _configuration;
    private readonly CommandExecutor _executor;
    private readonly ILogger<LinuxSystemMetricsCollector> _logger;
    private readonly SemaphoreSlim _collectionLock = new(1, 1);
    private readonly Dictionary<string, SmartData> _smart = new(StringComparer.Ordinal);
    private CpuCounters? _previousCpu;
    private IReadOnlyDictionary<string, DiskCounters>? _previousDisks;
    private IReadOnlyDictionary<string, NetworkCounters>? _previousNetworks;
    private TcpCounters? _previousTcp;
    private long? _previousOomKills;
    private DateTimeOffset? _previousCollectedAt;
    private DateTimeOffset _smartCollectedAt = DateTimeOffset.MinValue;

    /// <summary>Initialize the Linux system metrics collector.</summary>
    public LinuxSystemMetricsCollector(IDiskManager diskManager, IFortOSConfiguration configuration, ILogger<LinuxSystemMetricsCollector> logger)
    {
        _diskManager = diskManager;
        _configuration = configuration;
        _logger = logger;
        _executor = new CommandExecutor(logger);
    }

    /// <inheritdoc />
    public async Task<SystemMetricsSnapshot> CollectAsync(CancellationToken ct)
    {
        await _collectionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var diagnostics = new List<string>();
            var procRoot = ResolveRoot("monitoring:proc_root", "/host/proc", "/proc");
            var sysRoot = ResolveRoot("monitoring:sys_root", "/host/sys", "/sys");
            var uptimeText = await File.ReadAllTextAsync(Path.Combine(procRoot, "uptime"), ct).ConfigureAwait(false);
            var loadText = await File.ReadAllTextAsync(Path.Combine(procRoot, "loadavg"), ct).ConfigureAwait(false);
            var cpuText = await File.ReadAllTextAsync(Path.Combine(procRoot, "stat"), ct).ConfigureAwait(false);
            var memoryText = await File.ReadAllTextAsync(Path.Combine(procRoot, "meminfo"), ct).ConfigureAwait(false);
            var vmStatText = await File.ReadAllTextAsync(Path.Combine(procRoot, "vmstat"), ct).ConfigureAwait(false);
            var diskText = await File.ReadAllTextAsync(Path.Combine(procRoot, "diskstats"), ct).ConfigureAwait(false);
            var networkText = await File.ReadAllTextAsync(Path.Combine(procRoot, "net", "dev"), ct).ConfigureAwait(false);
            var tcpText = await File.ReadAllTextAsync(Path.Combine(procRoot, "net", "snmp"), ct).ConfigureAwait(false);
            var runtime = LinuxProcParsers.ParseRuntime(uptimeText, loadText, now);
            var cpuCounters = LinuxProcParsers.ParseCpuCounters(cpuText);
            var diskCounters = LinuxProcParsers.ParseDiskCounters(diskText);
            var networkCounters = LinuxProcParsers.ParseNetworkCounters(networkText);
            var tcpCounters = LinuxProcParsers.ParseTcpCounters(tcpText);
            var oomKills = LinuxProcParsers.ParseOomKillCount(vmStatText);
            var elapsed = Math.Max(0.001, (now - (_previousCollectedAt ?? now)).TotalSeconds);

            await RefreshSmartAsync(now, diagnostics, ct).ConfigureAwait(false);
            var fileSystems = CollectFileSystems(diagnostics);
            var raids = await CollectRaidAsync(procRoot, diagnostics, ct).ConfigureAwait(false);
            var services = await CollectServicesAsync(runtime.Uptime, diagnostics, ct).ConfigureAwait(false);
            var containers = await CollectContainersAsync(diagnostics, ct).ConfigureAwait(false);
            var memory = LinuxProcParsers.ParseMemory(memoryText) with
            {
                OomKillsSinceLastCollection = _previousOomKills is null ? 0 : Math.Max(0, oomKills - _previousOomKills.Value),
            };
            var snapshot = new SystemMetricsSnapshot
            {
                CollectedAt = now,
                Host = runtime,
                Cpu = LinuxProcParsers.CalculateCpuMetrics(_previousCpu, cpuCounters),
                Memory = memory,
                Disks = LinuxProcParsers.CalculateDiskMetrics(_previousDisks, diskCounters, elapsed, _smart),
                Networks = LinuxProcParsers.CalculateNetworkMetrics(_previousNetworks, networkCounters, elapsed, sysRoot),
                NetworkStack = LinuxProcParsers.CalculateNetworkStack(_previousTcp, tcpCounters, elapsed),
                ProtocolSessions = await CollectProtocolSessionsAsync(diagnostics, ct).ConfigureAwait(false),
                FileSystems = fileSystems,
                RaidArrays = raids,
                Services = services,
                Containers = containers,
                Diagnostics = diagnostics,
            };

            _previousCpu = cpuCounters;
            _previousDisks = diskCounters;
            _previousNetworks = networkCounters;
            _previousTcp = tcpCounters;
            _previousOomKills = oomKills;
            _previousCollectedAt = now;
            return snapshot;
        }
        finally
        {
            _collectionLock.Release();
        }
    }

    private async Task RefreshSmartAsync(DateTimeOffset now, ICollection<string> diagnostics, CancellationToken ct)
    {
        var interval = ReadPositiveInt("monitoring:smart_interval_seconds", 60);
        if (now - _smartCollectedAt < TimeSpan.FromSeconds(interval)) return;
        try
        {
            var disks = await _diskManager.ListDisksAsync(ct).ConfigureAwait(false);
            foreach (var disk in disks)
            {
                try
                {
                    var smart = await _diskManager.GetSmartDataAsync(disk.Path, ct).ConfigureAwait(false);
                    _smart[Path.GetFileName(disk.Path)] = smart;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _smart.Remove(Path.GetFileName(disk.Path));
                    AddDiagnostic(diagnostics, "smart", disk.Path, ex);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _smart.Clear();
            AddDiagnostic(diagnostics, "smart", null, ex);
        }
        finally
        {
            _smartCollectedAt = now;
        }
    }

    private static IReadOnlyList<FileSystemCapacityMetrics> CollectFileSystems(ICollection<string> diagnostics)
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Network)
                .Select(drive =>
                {
                    var total = drive.TotalSize;
                    var available = drive.AvailableFreeSpace;
                    var used = Math.Max(0, total - available);
                    return new FileSystemCapacityMetrics
                    {
                        Device = drive.Name,
                        MountPoint = drive.RootDirectory.FullName,
                        FileSystemType = drive.DriveFormat,
                        TotalBytes = total,
                        UsedBytes = used,
                        AvailableBytes = available,
                        UsedPercent = total <= 0 ? 0 : used * 100d / total,
                    };
                })
                .OrderBy(item => item.MountPoint, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"filesystem: {ex.Message}");
            return [];
        }
    }

    private async Task<IReadOnlyList<RaidMetrics>> CollectRaidAsync(string procRoot, ICollection<string> diagnostics, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(procRoot, "mdstat");
            if (!File.Exists(path)) return [];
            return LinuxProcParsers.ParseRaid(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddDiagnostic(diagnostics, "raid", null, ex);
            return [];
        }
    }

    private async Task<IReadOnlyList<ServiceRuntimeMetrics>> CollectServicesAsync(TimeSpan hostUptime, ICollection<string> diagnostics, CancellationToken ct)
    {
        try
        {
            var configured = _configuration.GetArray("monitoring:services");
            var units = configured.Length > 0 ? configured : DefaultServices;
            var safeUnits = units.Where(IsSafeUnitName).Distinct(StringComparer.Ordinal).ToArray();
            if (safeUnits.Length == 0) return [];
            var result = await ExecuteHostCommandAsync(
                "systemctl",
                $"show {string.Join(' ', safeUnits)} --property=Id --property=ActiveState --property=ActiveEnterTimestampMonotonic --property=NRestarts",
                ct,
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                diagnostics.Add($"systemd: {result.Stderr.Trim()}");
                return [];
            }
            return ParseSystemdServices(result.Stdout, hostUptime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddDiagnostic(diagnostics, "systemd", null, ex);
            return [];
        }
    }

    private async Task<IReadOnlyList<ContainerRuntimeMetrics>> CollectContainersAsync(ICollection<string> diagnostics, CancellationToken ct)
    {
        try
        {
            var result = await ExecuteHostCommandAsync(
                "docker",
                "stats --no-stream --format \"{{json .}}\"",
                ct,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                diagnostics.Add($"docker: {result.Stderr.Trim()}");
                return [];
            }
            return DockerStatsParser.Parse(result.Stdout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddDiagnostic(diagnostics, "docker", null, ex);
            return [];
        }
    }

    private async Task<IReadOnlyList<ProtocolSessionMetrics>> CollectProtocolSessionsAsync(ICollection<string> diagnostics, CancellationToken ct)
    {
        try
        {
            var result = await ExecuteHostCommandAsync(
                "ss",
                "-Htan state established",
                ct,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (result.ExitCode != 0) return [];
            return ParseProtocolSessions(result.Stdout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddDiagnostic(diagnostics, "protocol-sessions", null, ex);
            return [];
        }
    }

    internal static IReadOnlyList<ProtocolSessionMetrics> ParseProtocolSessions(string content)
    {
            var ports = new Dictionary<int, int>();
            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // Some iproute2 versions omit the state column when a state filter is supplied.
                var localEndpointIndex = long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? 2 : 3;
                if (fields.Length <= localEndpointIndex || !TryReadPort(fields[localEndpointIndex], out var port)) continue;
                ports[port] = ports.GetValueOrDefault(port) + 1;
            }
            return
            [
                new ProtocolSessionMetrics { Protocol = "ftp", ActiveSessions = ports.GetValueOrDefault(21) },
                new ProtocolSessionMetrics { Protocol = "ssh", ActiveSessions = ports.GetValueOrDefault(22) },
                new ProtocolSessionMetrics { Protocol = "smb", ActiveSessions = ports.GetValueOrDefault(445) },
                new ProtocolSessionMetrics { Protocol = "nfs", ActiveSessions = ports.GetValueOrDefault(2049) },
            ];
    }

    internal static IReadOnlyList<ServiceRuntimeMetrics> ParseSystemdServices(string content, TimeSpan hostUptime)
    {
        var result = new List<ServiceRuntimeMetrics>();
        foreach (var block in content.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var values = block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
            if (!values.TryGetValue("Id", out var id) || string.IsNullOrWhiteSpace(id)) continue;
            var activeMicroseconds = values.TryGetValue("ActiveEnterTimestampMonotonic", out var activeText)
                && long.TryParse(activeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var active)
                    ? active
                    : 0;
            var uptime = activeMicroseconds > 0
                ? hostUptime - TimeSpan.FromTicks(activeMicroseconds * 10)
                : TimeSpan.Zero;
            result.Add(new ServiceRuntimeMetrics
            {
                ServiceId = id,
                State = values.GetValueOrDefault("ActiveState", "unknown"),
                Uptime = uptime > TimeSpan.Zero ? uptime : TimeSpan.Zero,
                RestartCount = values.TryGetValue("NRestarts", out var restartText)
                    && long.TryParse(restartText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var restarts)
                        ? restarts
                        : 0,
            });
        }
        return result;
    }

    private int ReadPositiveInt(string key, int fallback)
        => int.TryParse(_configuration.GetValue(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;

    private string ResolveRoot(string configurationKey, string containerHostRoot, string nativeRoot)
    {
        var configured = _configuration.GetValue(configurationKey);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return Directory.Exists(containerHostRoot) ? containerHostRoot : nativeRoot;
    }

    private Task<CommandResult> ExecuteHostCommandAsync(string fileName, string arguments, CancellationToken ct, TimeSpan timeout)
    {
        // The reference container shares the host PID namespace. Entering PID 1's namespaces
        // makes systemd, Docker, and socket inspection describe the NAS host rather than FortOS itself.
        if (Directory.Exists("/host/proc/1/ns") && Directory.Exists("/proc/1/ns"))
        {
            return _executor.ExecuteAsync(
                "nsenter",
                $"--target 1 --mount --uts --ipc --net --pid -- {fileName} {arguments}",
                ct,
                timeout,
                throwOnNonZeroExit: false,
                logResult: false);
        }

        return _executor.ExecuteAsync(fileName, arguments, ct, timeout, throwOnNonZeroExit: false, logResult: false);
    }

    private static bool IsSafeUnitName(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '@' or '_' or '-');

    private static bool TryReadPort(string endpoint, out int port)
    {
        port = 0;
        var separator = endpoint.LastIndexOf(':');
        return separator >= 0
               && int.TryParse(endpoint[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
    }

    private void AddDiagnostic(ICollection<string> diagnostics, string subsystem, string? resource, Exception exception)
    {
        var prefix = string.IsNullOrWhiteSpace(resource) ? subsystem : $"{subsystem} ({resource})";
        diagnostics.Add($"{prefix}: {exception.Message}");
        _logger.LogWarning(exception, "Monitoring subsystem {Subsystem} could not collect {Resource}.", subsystem, resource);
    }
}

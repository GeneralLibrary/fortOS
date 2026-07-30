using GORT.Core;
using Microsoft.Extensions.Logging;

namespace GORT.Modules.Share.Services;

/// <summary>Rendered share protocol configuration content.</summary>
/// <param name="Smb">smb.conf content.</param>
/// <param name="NfsExports">exports content.</param>
/// <param name="Ftp">vsftpd.conf content.</param>
public sealed record RenderedShareConfigs(string Smb, string NfsExports, string Ftp);

/// <summary>
/// Share service coordinator.
/// Responsible for applying rendered protocol configurations to the system (writing to daemon config paths under /etc),
/// registering distribution-provided systemd services, and refreshing corresponding services after configuration changes.
/// This bridges the complete chain of "generate configuration -> daemon reads -> client can connect".
/// GORT does not start its own share daemons to avoid port and state file conflicts with distribution systemd units.
/// </summary>
public sealed class ShareServiceCoordinator
{
    /// <summary>SMB service identifier.</summary>
    public const string SmbServiceId = "smb";
    /// <summary>NFS service identifier.</summary>
    public const string NfsServiceId = "nfs";
    /// <summary>FTP service identifier.</summary>
    public const string FtpServiceId = "ftp";
    private const string ExportfsPath = "/usr/sbin/exportfs";
    private const string SmbdPath = "/usr/sbin/smbd";
    private const string VsftpdPath = "/usr/sbin/vsftpd";
    private const string SmbUnit = "smbd.service";
    private const string NfsUnit = "nfs-server.service";
    private const string FtpUnit = "vsftpd.service";

    private const string DefaultSmbConfPath = "/etc/samba/smb.conf";
    private const string DefaultExportsPath = "/etc/exports";
    private const string DefaultVsftpdConfPath = "/etc/vsftpd.conf";

    private readonly IServiceRegistry? _registry;
    private readonly IServiceSupervisor? _supervisor;
    private readonly IProcessManager? _processManager;
    private readonly IGortConfiguration? _configuration;
    private readonly ILogger _logger;

    /// <summary>Initialize the share service coordinator.</summary>
    /// <param name="registry">Optional service registry.</param>
    /// <param name="supervisor">Optional service supervisor for container environments without systemd.</param>
    /// <param name="processManager">Optional process manager.</param>
    /// <param name="configuration">Optional configuration for overriding system configuration file paths.</param>
    /// <param name="logger">Logger.</param>
    public ShareServiceCoordinator(IServiceRegistry? registry, IServiceSupervisor? supervisor, IProcessManager? processManager, IGortConfiguration? configuration, ILogger logger)
    {
        _registry = registry;
        _supervisor = supervisor;
        _processManager = processManager;
        _configuration = configuration;
        _logger = logger;
    }

    private string SmbConfPath => _configuration?.GetValue("share:smb_conf_path") ?? DefaultSmbConfPath;
    private string ExportsPath => _configuration?.GetValue("share:exports_path") ?? DefaultExportsPath;
    private string VsftpdConfPath => _configuration?.GetValue("share:vsftpd_conf_path") ?? DefaultVsftpdConfPath;

    /// <summary>
    /// Register distribution-provided share systemd units, with unified status and control exposed through the Service Bus.
    /// Only registers units that are actually installed on the host; development environments missing the
    /// corresponding packages will not produce invalid services.
    /// </summary>
    public async Task RegisterBuiltInServicesAsync(CancellationToken ct)
    {
        if (_registry is null)
        {
            return;
        }

        if (SystemdAvailable())
        {
            var definitions = new[]
            {
                (SmbServiceId, "Samba File Share", SmbUnit),
                (NfsServiceId, "NFS File Share", NfsUnit),
                (FtpServiceId, "FTP File Share", FtpUnit),
            };
            foreach (var (serviceId, displayName, unit) in definitions)
            {
                if (!SystemdUnitExists(unit))
                {
                    continue;
                }

                await RegisterAsync(new ServiceDefinition
                {
                    ServiceId = serviceId,
                    DisplayName = displayName,
                    Type = ServiceType.Systemd,
                    Startup = ServiceStartup.Automatic,
                    RestartPolicy = RestartPolicy.Never,
                    SystemdUnit = unit,
                }, ct).ConfigureAwait(false);
            }

            return;
        }

        // Containers do not run systemd as PID 1. Keep daemons in the foreground
        // so NativeServiceHost can own their lifecycle without competing units.
        var nativeDefinitions = new[]
        {
            (SmbServiceId, "Samba File Share", SmbdPath, "--foreground --no-process-group", RestartPolicy.OnFailure),
            (FtpServiceId, "FTP File Share", VsftpdPath, (string?)null, RestartPolicy.OnFailure),
        };
        foreach (var (serviceId, displayName, executable, arguments, restartPolicy) in nativeDefinitions)
        {
            if (!File.Exists(executable))
            {
                continue;
            }

            await RegisterAsync(new ServiceDefinition
            {
                ServiceId = serviceId,
                DisplayName = displayName,
                Type = ServiceType.Native,
                Startup = ServiceStartup.Manual,
                RestartPolicy = restartPolicy,
                Executable = executable,
                Arguments = arguments,
            }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Write rendered configurations to system paths and refresh the corresponding daemons,
    /// making share changes immediately effective for clients.
    /// </summary>
    public async Task ApplyAsync(RenderedShareConfigs configs, CancellationToken ct)
    {
        if (!SystemdAvailable() && !string.IsNullOrWhiteSpace(configs.NfsExports))
        {
            throw new PlatformNotSupportedException(
                "NFS sharing requires systemd to manage the kernel NFS service, and is only supported on Debian bare-metal installations.");
        }

        var changes = new[]
        {
            new ConfigChange("smb", SmbConfPath, configs.Smb),
            new ConfigChange("nfs", ExportsPath, configs.NfsExports),
            new ConfigChange("ftp", VsftpdConfPath, configs.Ftp),
        };

        var prepared = new List<PreparedConfig>(changes.Length);
        try
        {
            foreach (var change in changes)
            {
                var item = await PrepareAsync(change, ct).ConfigureAwait(false);
                prepared.Add(item);
                await ValidateAsync(item, ct).ConfigureAwait(false);
            }

            foreach (var item in prepared)
            {
                File.Move(item.TemporaryPath, item.Change.Path, overwrite: true);
                item.Committed = true;
            }

            if (SystemdAvailable())
            {
                await RefreshNfsExportsAsync(ct).ConfigureAwait(false);
                await ReloadSystemdUnitAsync(SmbUnit, ct).ConfigureAwait(false);
                await ReloadSystemdUnitAsync(FtpUnit, ct).ConfigureAwait(false);
            }
            else
            {
                await RestartIfRegisteredAsync(SmbServiceId, ct).ConfigureAwait(false);
                await RestartIfRegisteredAsync(FtpServiceId, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var rollbackErrors = Rollback(prepared);
            throw new ShareConfigurationException(
                rollbackErrors.Count == 0
                    ? "Share protocol configuration application failed; original configuration has been restored."
                    : $"Share protocol configuration application failed, and {rollbackErrors.Count} configurations could not be restored.",
                ex,
                rollbackErrors);
        }
        finally
        {
            Cleanup(prepared);
        }
    }

    private async Task RegisterAsync(ServiceDefinition definition, CancellationToken ct)
    {
        try
        {
            await _registry!.RegisterAsync(definition, ct).ConfigureAwait(false);
            _logger.LogInformation("Registered built-in share service {ServiceId} ({Executable}).", definition.ServiceId, definition.Executable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to register built-in share service {ServiceId}.", definition.ServiceId);
        }
    }

    private async Task<PreparedConfig> PrepareAsync(ConfigChange change, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(change.Path)
            ?? throw new ConfigurationException($"Share configuration path has no parent directory: {change.Path}");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(change.Path)}.{Guid.CreateVersion7():N}.tmp");
        var backupPath = Path.Combine(directory, $".{Path.GetFileName(change.Path)}.{Guid.CreateVersion7():N}.bak");
        var existed = File.Exists(change.Path);
        if (existed)
        {
            File.Copy(change.Path, backupPath, overwrite: false);
        }

        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
            await writer.WriteAsync(change.Content.AsMemory(), ct).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        return new PreparedConfig(change, temporaryPath, backupPath, existed);
    }

    private async Task ValidateAsync(PreparedConfig prepared, CancellationToken ct)
    {
        var executable = _configuration?.GetValue($"share:{prepared.Change.Protocol}_validator");
        var arguments = _configuration?.GetValue($"share:{prepared.Change.Protocol}_validator_args");
        if (string.IsNullOrWhiteSpace(executable)
            && prepared.Change.Protocol == "smb"
            && File.Exists("/usr/bin/testparm"))
        {
            executable = "/usr/bin/testparm";
            arguments = "-s --suppress-prompt {path}";
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        if (_processManager is null)
        {
            throw new ConfigurationException($"Validator configured for {prepared.Change.Protocol}, but the process manager is not available.");
        }

        var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = executable,
            Arguments = (arguments ?? "{path}").Replace("{path}", Quote(prepared.TemporaryPath), StringComparison.Ordinal),
            TimeoutSeconds = 30,
        }, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConfigurationException($"{prepared.Change.Protocol} configuration validation failed: {result.Stderr}");
        }
    }

    /// <summary>Execute exportfs -ra to make the kernel reload the NFS export table.</summary>
    private async Task RefreshNfsExportsAsync(CancellationToken ct)
    {
        if (_processManager is null || !File.Exists(ExportfsPath))
        {
            return;
        }

        var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = ExportfsPath,
            Arguments = "-ra",
        }, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConfigurationException($"Failed to refresh NFS export table: {result.Stderr}");
        }
        _logger.LogInformation("NFS export table has been refreshed.");
    }

    private async Task ReloadSystemdUnitAsync(string unit, CancellationToken ct)
    {
        if (_processManager is null || !SystemdUnitExists(unit))
        {
            return;
        }

        var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "systemctl",
            Arguments = $"reload-or-restart \"{unit}\"",
            TimeoutSeconds = 30,
        }, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConfigurationException($"Failed to refresh systemd unit {unit}: {result.Stderr}");
        }

        _logger.LogInformation("systemd unit {Unit} has reloaded the share configuration.", unit);
    }

    private async Task RestartIfRegisteredAsync(string serviceId, CancellationToken ct)
    {
        if (_supervisor is null || _registry is null
            || await _registry.GetAsync(serviceId, ct).ConfigureAwait(false) is null)
        {
            return;
        }

        await _supervisor.RestartAsync(serviceId, ct).ConfigureAwait(false);
        _logger.LogInformation("Container share service {ServiceId} has been restarted to load configuration.", serviceId);
    }

    private static bool SystemdAvailable()
        => Directory.Exists("/run/systemd/system") && File.Exists("/usr/bin/systemctl");

    private static bool SystemdUnitExists(string unit)
        => File.Exists(Path.Combine("/etc/systemd/system", unit))
            || File.Exists(Path.Combine("/usr/lib/systemd/system", unit))
            || File.Exists(Path.Combine("/lib/systemd/system", unit));

    private static IReadOnlyList<string> Rollback(IEnumerable<PreparedConfig> prepared)
    {
        var errors = new List<string>();
        foreach (var item in prepared.Where(item => item.Committed).Reverse())
        {
            try
            {
                if (item.Existed)
                {
                    File.Move(item.BackupPath, item.Change.Path, overwrite: true);
                }
                else if (File.Exists(item.Change.Path))
                {
                    File.Delete(item.Change.Path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{item.Change.Protocol}: {ex.Message}");
            }
        }

        return errors;
    }

    private static void Cleanup(IEnumerable<PreparedConfig> prepared)
    {
        foreach (var item in prepared)
        {
            if (File.Exists(item.TemporaryPath)) File.Delete(item.TemporaryPath);
            if (File.Exists(item.BackupPath)) File.Delete(item.BackupPath);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private sealed record ConfigChange(string Protocol, string Path, string Content);

    private sealed class PreparedConfig(ConfigChange change, string temporaryPath, string backupPath, bool existed)
    {
        public ConfigChange Change { get; } = change;
        public string TemporaryPath { get; } = temporaryPath;
        public string BackupPath { get; } = backupPath;
        public bool Existed { get; } = existed;
        public bool Committed { get; set; }
    }
}

/// <summary>Share configuration transaction failed; RollbackErrors indicates files requiring manual intervention.</summary>
public sealed class ShareConfigurationException(
    string message,
    Exception innerException,
    IReadOnlyList<string> rollbackErrors) : Exception(message, innerException)
{
    public IReadOnlyList<string> RollbackErrors { get; } = rollbackErrors;
}

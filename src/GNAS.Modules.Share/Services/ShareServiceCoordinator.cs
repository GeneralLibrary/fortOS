using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Modules.Share.Services;

/// <summary>渲染完成的共享协议配置内容。</summary>
/// <param name="Smb">smb.conf 内容。</param>
/// <param name="NfsExports">exports 内容。</param>
/// <param name="Ftp">vsftpd.conf 内容。</param>
public sealed record RenderedShareConfigs(string Smb, string NfsExports, string Ftp);

/// <summary>
/// 共享服务协调器。
/// 负责把渲染好的协议配置真正应用到系统（写入 /etc 下的守护进程配置路径）、
/// 注册发行版提供的 systemd 服务，并在配置变更后刷新对应服务，
/// 打通"生成配置 → 守护进程读取 → 客户端可连接"的完整链路。
/// GNAS 不另起共享守护进程，避免与发行版 systemd 单元争用端口和状态文件。
/// </summary>
public sealed class ShareServiceCoordinator
{
    /// <summary>SMB 服务标识。</summary>
    public const string SmbServiceId = "smb";
    /// <summary>NFS 服务标识。</summary>
    public const string NfsServiceId = "nfs";
    /// <summary>FTP 服务标识。</summary>
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
    private readonly IGnasConfiguration? _configuration;
    private readonly ILogger _logger;

    /// <summary>初始化共享服务协调器。</summary>
    /// <param name="registry">可选服务注册表。</param>
    /// <param name="supervisor">可选服务监管器，用于无 systemd 的容器环境。</param>
    /// <param name="processManager">可选进程管理器。</param>
    /// <param name="configuration">可选配置，用于覆盖系统配置文件路径。</param>
    /// <param name="logger">日志记录器。</param>
    public ShareServiceCoordinator(IServiceRegistry? registry, IServiceSupervisor? supervisor, IProcessManager? processManager, IGnasConfiguration? configuration, ILogger logger)
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
    /// 注册发行版提供的共享 systemd 单元，由 Service Bus 暴露统一的状态和控制接口。
    /// 仅注册本机实际安装的单元，开发环境缺少对应软件包时不会产生无效服务。
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
                (SmbServiceId, "Samba 文件共享", SmbUnit),
                (NfsServiceId, "NFS 文件共享", NfsUnit),
                (FtpServiceId, "FTP 文件共享", FtpUnit),
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
            (SmbServiceId, "Samba 文件共享", SmbdPath, "--foreground --no-process-group", RestartPolicy.OnFailure),
            (FtpServiceId, "FTP 文件共享", VsftpdPath, (string?)null, RestartPolicy.OnFailure),
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
    /// 将渲染后的配置写入系统路径并刷新对应守护进程，使共享变更对客户端立即生效。
    /// </summary>
    public async Task ApplyAsync(RenderedShareConfigs configs, CancellationToken ct)
    {
        if (!SystemdAvailable() && !string.IsNullOrWhiteSpace(configs.NfsExports))
        {
            throw new PlatformNotSupportedException(
                "NFS 共享需要由 systemd 管理内核 NFS 服务，仅支持 Debian 裸机安装。");
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
                    ? "共享协议配置应用失败，已恢复原配置。"
                    : $"共享协议配置应用失败，且有 {rollbackErrors.Count} 个配置无法恢复。",
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
            _logger.LogInformation("已注册内置共享服务 {ServiceId}（{Executable}）。", definition.ServiceId, definition.Executable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "注册内置共享服务 {ServiceId} 失败。", definition.ServiceId);
        }
    }

    private async Task<PreparedConfig> PrepareAsync(ConfigChange change, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(change.Path)
            ?? throw new ConfigurationException($"共享配置路径无父目录：{change.Path}");
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
            throw new ConfigurationException($"已配置 {prepared.Change.Protocol} 校验器，但进程管理器不可用。");
        }

        var result = await _processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = executable,
            Arguments = (arguments ?? "{path}").Replace("{path}", Quote(prepared.TemporaryPath), StringComparison.Ordinal),
            TimeoutSeconds = 30,
        }, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConfigurationException($"{prepared.Change.Protocol} 配置校验失败：{result.Stderr}");
        }
    }

    /// <summary>执行 exportfs -ra 让内核重新加载 NFS 导出表。</summary>
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
            throw new ConfigurationException($"刷新 NFS 导出表失败：{result.Stderr}");
        }
        _logger.LogInformation("NFS 导出表已刷新。");
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
            throw new ConfigurationException($"刷新 systemd 单元 {unit} 失败：{result.Stderr}");
        }

        _logger.LogInformation("systemd 单元 {Unit} 已刷新共享配置。", unit);
    }

    private async Task RestartIfRegisteredAsync(string serviceId, CancellationToken ct)
    {
        if (_supervisor is null || _registry is null
            || await _registry.GetAsync(serviceId, ct).ConfigureAwait(false) is null)
        {
            return;
        }

        await _supervisor.RestartAsync(serviceId, ct).ConfigureAwait(false);
        _logger.LogInformation("容器共享服务 {ServiceId} 已重启以加载配置。", serviceId);
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

/// <summary>共享配置事务失败；RollbackErrors 指示需要人工介入的文件。</summary>
public sealed class ShareConfigurationException(
    string message,
    Exception innerException,
    IReadOnlyList<string> rollbackErrors) : Exception(message, innerException)
{
    public IReadOnlyList<string> RollbackErrors { get; } = rollbackErrors;
}

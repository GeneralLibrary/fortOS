using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Modules.Share.Services;

/// <summary>渲染完成的共享协议配置内容。</summary>
/// <param name="Smb">smb.conf 内容。</param>
/// <param name="NfsExports">exports 内容。</param>
/// <param name="Ftp">vsftpd.conf 内容。</param>
/// <param name="WebDav">WebDAV nginx 片段。</param>
public sealed record RenderedShareConfigs(string Smb, string NfsExports, string Ftp, string WebDav);

/// <summary>
/// 共享服务协调器。
/// 负责把渲染好的协议配置真正应用到系统（写入 /etc 下的守护进程配置路径）、
/// 注册内置共享守护进程的 <see cref="ServiceDefinition"/>，并在配置变更后刷新对应服务，
/// 打通"生成配置 → 守护进程读取 → 客户端可连接"的完整链路。
/// 所有系统侧操作都是尽力而为的：在非 Linux 平台、缺少守护进程或权限不足时降级并记录日志，
/// 不会让共享的增删操作本身失败。
/// </summary>
public sealed class ShareServiceCoordinator
{
    /// <summary>SMB 服务标识。</summary>
    public const string SmbServiceId = "smb";
    /// <summary>NFS 服务标识。</summary>
    public const string NfsServiceId = "nfs";
    /// <summary>FTP 服务标识。</summary>
    public const string FtpServiceId = "ftp";
    /// <summary>WebDAV 服务标识。</summary>
    public const string WebDavServiceId = "webdav";

    private const string SmbdPath = "/usr/sbin/smbd";
    private const string RpcNfsdPath = "/usr/sbin/rpc.nfsd";
    private const string VsftpdPath = "/usr/sbin/vsftpd";
    private const string ExportfsPath = "/usr/sbin/exportfs";

    private const string DefaultSmbConfPath = "/etc/samba/smb.conf";
    private const string DefaultExportsPath = "/etc/exports";
    private const string DefaultVsftpdConfPath = "/etc/vsftpd.conf";
    private const string DefaultWebDavConfPath = "/etc/nginx/conf.d/gnas-webdav.conf";

    private readonly IServiceRegistry? _registry;
    private readonly IServiceSupervisor? _supervisor;
    private readonly IProcessManager? _processManager;
    private readonly IGnasConfiguration? _configuration;
    private readonly ILogger _logger;

    /// <summary>初始化共享服务协调器。</summary>
    /// <param name="registry">可选服务注册表。</param>
    /// <param name="supervisor">可选服务监管器。</param>
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
    private string WebDavConfPath => _configuration?.GetValue("share:webdav_conf_path") ?? DefaultWebDavConfPath;

    /// <summary>
    /// 注册内置共享守护进程的服务定义（Manual 启动，由共享变更按需拉起/重启）。
    /// 仅注册本机实际存在的守护进程，避免 RestartAsync 因服务不存在而抛出异常。
    /// </summary>
    public async Task RegisterBuiltInServicesAsync(CancellationToken ct)
    {
        if (_registry is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        if (File.Exists(SmbdPath))
        {
            // --foreground/--no-process-group 使 smbd 以前台子进程方式运行，便于 NativeServiceHost 监管生命周期。
            await RegisterAsync(new ServiceDefinition
            {
                ServiceId = SmbServiceId,
                DisplayName = "Samba 文件共享",
                Type = ServiceType.Native,
                Startup = ServiceStartup.Manual,
                RestartPolicy = RestartPolicy.OnFailure,
                Executable = SmbdPath,
                Arguments = "--foreground --no-process-group",
            }, ct).ConfigureAwait(false);
        }

        if (File.Exists(RpcNfsdPath))
        {
            // rpc.nfsd 只负责通知内核启动 nfsd 线程后立即退出，属于一次性命令而非常驻进程，
            // 因此使用 Never 重启策略避免退出后被误判为崩溃而进入重启循环。
            await RegisterAsync(new ServiceDefinition
            {
                ServiceId = NfsServiceId,
                DisplayName = "NFS 文件共享",
                Type = ServiceType.Native,
                Startup = ServiceStartup.Manual,
                RestartPolicy = RestartPolicy.Never,
                Executable = RpcNfsdPath,
                Arguments = "8",
            }, ct).ConfigureAwait(false);
        }

        if (File.Exists(VsftpdPath))
        {
            // vsftpd 默认配置 background=NO，以前台方式运行并读取 /etc/vsftpd.conf。
            await RegisterAsync(new ServiceDefinition
            {
                ServiceId = FtpServiceId,
                DisplayName = "FTP 文件共享",
                Type = ServiceType.Native,
                Startup = ServiceStartup.Manual,
                RestartPolicy = RestartPolicy.OnFailure,
                Executable = VsftpdPath,
            }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 将渲染后的配置写入系统路径并刷新对应守护进程，使共享变更对客户端立即生效。
    /// </summary>
    public async Task ApplyAsync(RenderedShareConfigs configs, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
        {
            _logger.LogDebug("非 Linux 平台不应用共享守护进程配置。");
            return;
        }

        var changes = new[]
        {
            new ConfigChange("smb", SmbConfPath, configs.Smb),
            new ConfigChange("nfs", ExportsPath, configs.NfsExports),
            new ConfigChange("ftp", VsftpdConfPath, configs.Ftp),
            new ConfigChange("webdav", WebDavConfPath, configs.WebDav),
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

            await RefreshNfsExportsAsync(ct).ConfigureAwait(false);
            await RestartIfRegisteredAsync(SmbServiceId, ct).ConfigureAwait(false);
            await RestartIfRegisteredAsync(FtpServiceId, ct).ConfigureAwait(false);
            await RestartIfRegisteredAsync(WebDavServiceId, ct).ConfigureAwait(false);
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

    /// <summary>仅当服务已注册时才通过监管器重启，避免 ServiceNotFoundException 中断共享操作。</summary>
    private async Task RestartIfRegisteredAsync(string serviceId, CancellationToken ct)
    {
        if (_supervisor is null || _registry is null)
        {
            return;
        }

        if (await _registry.GetAsync(serviceId, ct).ConfigureAwait(false) is null)
        {
            _logger.LogDebug("服务 {ServiceId} 未注册（守护进程未安装），跳过重启。", serviceId);
            return;
        }

        await _supervisor.RestartAsync(serviceId, ct).ConfigureAwait(false);
        _logger.LogInformation("共享服务 {ServiceId} 已重启以加载新配置。", serviceId);
    }

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

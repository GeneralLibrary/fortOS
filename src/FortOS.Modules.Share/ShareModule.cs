using System.Text.Json;
using FortOS.Core;
using FortOS.Modules.Host;
using FortOS.Modules.Share.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FortOS.Modules.Share;

/// <summary>Share protocol module, manages SMB, NFS, and FTP configurations.</summary>
public sealed class ShareModule : NasModuleBase
{
    private readonly SemaphoreSlim sync = new(1, 1);
    private ShareServiceCoordinator? coordinator;
    private string ConfigPath => Path.Combine(Context.DataDirectory, "config", "shares.json");

    /// <inheritdoc />
    public override string ModuleId => "share";

    /// <inheritdoc />
    public override string DisplayName => "Share Services";

    /// <inheritdoc />
    public override IReadOnlyList<string> Dependencies => ["storage"];

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["share:read", "share:write", "storage:filesystem:read"];

    /// <summary>
    /// Initialize: register built-in share daemon service definitions and replay persisted share configurations
    /// to system paths, ensuring clients can still access existing shares after a NAS restart.
    /// </summary>
    protected override async Task OnInitializeAsync(CancellationToken ct)
    {
        coordinator = new ShareServiceCoordinator(
            Services.GetService<IServiceRegistry>(),
            Services.GetService<IServiceSupervisor>(),
            Services.GetService<IProcessManager>(),
            Services.GetService<IFortOSConfiguration>(),
            Logger);
        await coordinator.RegisterBuiltInServicesAsync(ct).ConfigureAwait(false);

        RenderedShareConfigs rendered;
        await sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var shares = await ReadSharesAsync(ct).ConfigureAwait(false);
            rendered = await WriteRenderedConfigsAsync(shares, ct).ConfigureAwait(false);
        }
        finally
        {
            sync.Release();
        }

        await coordinator.ApplyAsync(rendered, ct).ConfigureAwait(false);
    }

    /// <summary>Create share and refresh service configuration.</summary>
    public async Task<ShareDefinition> CreateShareAsync(ShareDefinition share, CancellationToken ct)
    {
        ShareValidation.ValidateShare(share);
        // 整个「读-改-写-应用」在锁内串行：Apply（重启/重载 SMB/NFS 服务）若在锁外，
        // 两个并发操作会互相覆盖配置应用状态，导致静默配置不一致。
        await sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var shares = await ReadSharesAsync(ct).ConfigureAwait(false);
            if (shares.Any(s => string.Equals(s.ShareId, share.ShareId, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, share.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Share {share.Name} already exists.");
            }

            shares.Add(share);
            await WriteSharesAsync(shares, ct).ConfigureAwait(false);
            var rendered = await WriteRenderedConfigsAsync(shares, ct).ConfigureAwait(false);
            await ApplyRenderedConfigsAsync(rendered, ct).ConfigureAwait(false);
        }
        finally
        {
            sync.Release();
        }

        await PublishAsync("share.created", "share.created", share, ct).ConfigureAwait(false);
        return share;
    }

    /// <summary>Delete share and refresh service configuration.</summary>
    public async Task DeleteShareAsync(string shareId, CancellationToken ct)
    {
        // 与 Create 相同：Apply 在锁内串行，避免并发删除互相覆盖应用状态。
        await sync.WaitAsync(ct).ConfigureAwait(false);
        ShareDefinition? removed;
        try
        {
            var shares = await ReadSharesAsync(ct).ConfigureAwait(false);
            removed = shares.FirstOrDefault(s => string.Equals(s.ShareId, shareId, StringComparison.OrdinalIgnoreCase));
            if (removed is null)
            {
                return;
            }

            shares.Remove(removed);
            await WriteSharesAsync(shares, ct).ConfigureAwait(false);
            var rendered = await WriteRenderedConfigsAsync(shares, ct).ConfigureAwait(false);
            await ApplyRenderedConfigsAsync(rendered, ct).ConfigureAwait(false);
        }
        finally
        {
            sync.Release();
        }

        await PublishAsync("share.deleted", "share.deleted", removed, ct).ConfigureAwait(false);
    }

    /// <summary>List shares.</summary>
    public async Task<IReadOnlyList<ShareDefinition>> ListSharesAsync(CancellationToken ct)
    {
        await sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadSharesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            sync.Release();
        }
    }

    private async Task<List<ShareDefinition>> ReadSharesAsync(CancellationToken ct)
    {
        if (!File.Exists(ConfigPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(ConfigPath);
        return await JsonSerializer.DeserializeAsync<List<ShareDefinition>>(stream, cancellationToken: ct).ConfigureAwait(false) ?? [];
    }

    private async Task WriteSharesAsync(List<ShareDefinition> shares, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        await using var stream = File.Create(ConfigPath);
        await JsonSerializer.SerializeAsync(stream, shares, new JsonSerializerOptions { WriteIndented = true }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Render configurations for each protocol and write a copy to the module data directory (for auditing and troubleshooting),
    /// returning the rendered result for the coordinator to apply to system paths.
    /// </summary>
    private async Task<RenderedShareConfigs> WriteRenderedConfigsAsync(List<ShareDefinition> shares, CancellationToken ct)
    {
        var configDir = Path.Combine(Context.DataDirectory, "config");
        Directory.CreateDirectory(configDir);
        var rendered = new RenderedShareConfigs(
            new SmbConfigGenerator().Generate(shares),
            new NfsExportsGenerator().Generate(shares),
            new FtpConfigGenerator().Generate(shares));
        await File.WriteAllTextAsync(Path.Combine(configDir, "smb.conf"), rendered.Smb, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(configDir, "exports"), rendered.NfsExports, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(configDir, "vsftpd.conf"), rendered.Ftp, ct).ConfigureAwait(false);
        return rendered;
    }

    /// <summary>Apply rendered configurations to system daemon configuration through the coordinator.</summary>
    private Task ApplyRenderedConfigsAsync(RenderedShareConfigs rendered, CancellationToken ct)
        => coordinator?.ApplyAsync(rendered, ct) ?? Task.CompletedTask;
}

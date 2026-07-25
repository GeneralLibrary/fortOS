using System.Text.Json;
using GNAS.Core;
using GNAS.Modules.Host;
using GNAS.Modules.Share.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Modules.Share;

/// <summary>共享协议模块，管理 SMB、NFS 与 FTP 配置。</summary>
public sealed class ShareModule : NasModuleBase
{
    private readonly SemaphoreSlim sync = new(1, 1);
    private ShareServiceCoordinator? coordinator;
    private string ConfigPath => Path.Combine(Context.DataDirectory, "config", "shares.json");

    /// <inheritdoc />
    public override string ModuleId => "share";

    /// <inheritdoc />
    public override string DisplayName => "共享服务";

    /// <inheritdoc />
    public override IReadOnlyList<string> Dependencies => ["storage"];

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["share:read", "share:write", "storage:filesystem:read"];

    /// <summary>
    /// 初始化：注册内置共享守护进程服务定义，并重放持久化的共享配置到系统路径，
    /// 保证 NAS 重启后客户端仍能访问既有共享。
    /// </summary>
    protected override async Task OnInitializeAsync(CancellationToken ct)
    {
        coordinator = new ShareServiceCoordinator(
            Services.GetService<IServiceRegistry>(),
            Services.GetService<IServiceSupervisor>(),
            Services.GetService<IProcessManager>(),
            Services.GetService<IGnasConfiguration>(),
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

    /// <summary>创建共享并刷新服务配置。</summary>
    public async Task<ShareDefinition> CreateShareAsync(ShareDefinition share, CancellationToken ct)
    {
        ShareValidation.ValidateShare(share);
        RenderedShareConfigs rendered;
        await sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var shares = await ReadSharesAsync(ct).ConfigureAwait(false);
            if (shares.Any(s => string.Equals(s.ShareId, share.ShareId, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, share.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"共享 {share.Name} 已存在。");
            }

            shares.Add(share);
            await WriteSharesAsync(shares, ct).ConfigureAwait(false);
            rendered = await WriteRenderedConfigsAsync(shares, ct).ConfigureAwait(false);
        }
        finally
        {
            sync.Release();
        }

        await ApplyRenderedConfigsAsync(rendered, ct).ConfigureAwait(false);
        await PublishAsync("share.created", "share.created", share, ct).ConfigureAwait(false);
        return share;
    }

    /// <summary>删除共享并刷新服务配置。</summary>
    public async Task DeleteShareAsync(string shareId, CancellationToken ct)
    {
        await sync.WaitAsync(ct).ConfigureAwait(false);
        ShareDefinition? removed;
        RenderedShareConfigs? rendered = null;
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
            rendered = await WriteRenderedConfigsAsync(shares, ct).ConfigureAwait(false);
        }
        finally
        {
            sync.Release();
        }

        if (rendered is not null)
        {
            await ApplyRenderedConfigsAsync(rendered, ct).ConfigureAwait(false);
        }

        await PublishAsync("share.deleted", "share.deleted", removed, ct).ConfigureAwait(false);
    }

    /// <summary>列出共享。</summary>
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
    /// 渲染各协议配置并写入模块数据目录副本（供审计与排障），返回渲染结果供协调器应用到系统路径。
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

    /// <summary>通过协调器将渲染结果应用到系统守护进程配置。</summary>
    private Task ApplyRenderedConfigsAsync(RenderedShareConfigs rendered, CancellationToken ct)
        => coordinator?.ApplyAsync(rendered, ct) ?? Task.CompletedTask;
}

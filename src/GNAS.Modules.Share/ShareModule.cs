using System.Text.Json;
using GNAS.Core;
using GNAS.Modules.Host;
using GNAS.Modules.Share.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Modules.Share;

/// <summary>共享协议模块，管理 SMB、NFS、FTP 与 WebDAV 配置。</summary>
public sealed class ShareModule : NasModuleBase
{
    private readonly SemaphoreSlim sync = new(1, 1);
    private string ConfigPath => Path.Combine(Context.DataDirectory, "config", "shares.json");

    /// <inheritdoc />
    public override string ModuleId => "share";

    /// <inheritdoc />
    public override string DisplayName => "共享服务";

    /// <inheritdoc />
    public override IReadOnlyList<string> Dependencies => ["storage"];

    /// <inheritdoc />
    public override IReadOnlyList<string> RequiredCapabilities => ["share:read", "share:write", "storage:filesystem:read"];

    /// <summary>创建共享并刷新服务配置。</summary>
    public async Task<ShareDefinition> CreateShareAsync(ShareDefinition share, CancellationToken ct)
    {
        ShareValidation.ValidateShare(share);
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
            await WriteRenderedConfigsAsync(shares, ct).ConfigureAwait(false);
        }
        finally
        {
            sync.Release();
        }

        if (Services.GetService<IServiceSupervisor>() is { } supervisor)
        {
            await supervisor.RestartAsync("smb", ct).ConfigureAwait(false);
        }

        await PublishAsync("share.created", "share.created", share, ct).ConfigureAwait(false);
        return share;
    }

    /// <summary>删除共享并刷新服务配置。</summary>
    public async Task DeleteShareAsync(string shareId, CancellationToken ct)
    {
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
            await WriteRenderedConfigsAsync(shares, ct).ConfigureAwait(false);
        }
        finally
        {
            sync.Release();
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

    private async Task WriteRenderedConfigsAsync(List<ShareDefinition> shares, CancellationToken ct)
    {
        var configDir = Path.Combine(Context.DataDirectory, "config");
        Directory.CreateDirectory(configDir);
        await File.WriteAllTextAsync(Path.Combine(configDir, "smb.conf"), new SmbConfigGenerator().Generate(shares), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(configDir, "exports"), new NfsExportsGenerator().Generate(shares), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(configDir, "vsftpd.conf"), new FtpConfigGenerator().Generate(shares), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(configDir, "webdav.conf"), new WebDavConfigGenerator().Generate(shares), ct).ConfigureAwait(false);
    }
}

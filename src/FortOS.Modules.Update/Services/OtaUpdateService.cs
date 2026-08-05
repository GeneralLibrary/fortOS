using System.Security.Cryptography;
using FortOS.Core;

namespace FortOS.Modules.Update.Services;

/// <summary>OTA update service, responsible for downloading, verification, staging and rollback.</summary>
public sealed class OtaUpdateService
{
    private readonly HttpClient httpClient;
    private readonly IEventBus eventBus;
    private readonly string rootDirectory;

    /// <summary>Create the OTA update service.</summary>
    public OtaUpdateService(HttpClient httpClient, IEventBus eventBus, string rootDirectory)
    {
        this.httpClient = httpClient;
        this.eventBus = eventBus;
        this.rootDirectory = rootDirectory;
    }

    /// <summary>更新包最大体积（2 GiB）：防止恶意/异常 URL 无限下载填满磁盘。</summary>
    private const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Download the update package to the staging directory and verify SHA256.</summary>
    public async Task<DownloadResult> DownloadAsync(Uri packageUri, string expectedSha256, CancellationToken ct)
    {
        try
        {
            var staging = Path.Combine(rootDirectory, "updates", "staging");
            Directory.CreateDirectory(staging);
            var filePath = Path.Combine(staging, Path.GetFileName(packageUri.LocalPath));

            // 先读响应头校验 Content-Length，再流式下载并在写入时计数：
            // 双重防护，缺省或虚假的 Content-Length 也无法绕过大小上限。
            using var response = await httpClient.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxPackageBytes)
            {
                return new DownloadResult(false, null, $"Update package exceeds the maximum size of {MaxPackageBytes} bytes.");
            }

            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = File.Create(filePath))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > MaxPackageBytes)
                    {
                        await output.DisposeAsync().ConfigureAwait(false);
                        File.Delete(filePath);
                        return new DownloadResult(false, null, "Update package exceeded the maximum size while downloading.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }

            var hash = await ComputeSha256Async(filePath, ct).ConfigureAwait(false);
            if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(filePath);
                return new DownloadResult(false, filePath, $"SHA256 verification failed: {hash}");
            }

            return new DownloadResult(true, filePath, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return new DownloadResult(false, null, $"Download failed: {ex.Message}");
        }
    }

    /// <summary>Stage the update; actual restart is performed by the system layer subscribing to system.update.ready.</summary>
    public async Task ApplyAsync(string packagePath, CancellationToken ct)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("Update package does not exist.", packagePath);
        }

        var updateDir = Path.Combine(rootDirectory, "updates", "ready", Path.GetFileNameWithoutExtension(packagePath));
        Directory.CreateDirectory(updateDir);
        File.Copy(packagePath, Path.Combine(updateDir, Path.GetFileName(packagePath)), overwrite: true);
        await eventBus.PublishAsync("system.update.ready", "system.update.ready", System.Text.Json.JsonSerializer.Serialize(new { updateDir }), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rollback to the previous version in the previous directory.
    /// 使用 rename 原子切换：任何时刻 current 都是完整可用的版本（旧版或回滚版），
    /// 崩溃不会留下残缺的 current（旧实现先删 current 再逐文件复制，中途失败即丢版本）。
    /// </summary>
    public Task RollbackAsync(CancellationToken ct)
    {
        var previous = Path.Combine(rootDirectory, "updates", "previous");
        var current = Path.Combine(rootDirectory, "updates", "current");
        var backup = Path.Combine(rootDirectory, "updates", "rollback-tmp");
        if (!Directory.Exists(previous))
        {
            throw new InvalidOperationException("No previous version available for rollback.");
        }

        ct.ThrowIfCancellationRequested();

        // 1. 若 current 存在，先原子挪到临时名：保留旧版，失败时可原样恢复。
        if (Directory.Exists(current))
        {
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            Directory.Move(current, backup);
        }

        try
        {
            // 2. previous → current：同文件系统内的 rename 是原子操作。
            Directory.Move(previous, current);
        }
        catch
        {
            // 3. 切换失败：把临时名挪回 current，保持原状后重新抛出。
            if (Directory.Exists(backup) && !Directory.Exists(current))
            {
                Directory.Move(backup, current);
            }

            throw;
        }

        // 4. 成功：清理临时副本。
        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        return Task.CompletedTask;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>Download result.</summary>
public sealed record DownloadResult(bool Success, string? FilePath, string? ErrorMessage);

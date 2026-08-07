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

    /// <summary>Maximum update package size (2 GiB): prevents a malicious or abnormal URL from downloading endlessly and filling the disk.</summary>
    private const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Download the update package to the staging directory and verify SHA256.</summary>
    public async Task<DownloadResult> DownloadAsync(Uri packageUri, string expectedSha256, CancellationToken ct)
    {
        try
        {
            var staging = Path.Combine(rootDirectory, "updates", "staging");
            Directory.CreateDirectory(staging);
            var filePath = Path.Combine(staging, Path.GetFileName(packageUri.LocalPath));

            // Validate Content-Length from the response headers first, then stream the download while counting bytes written:
            // dual protection so a missing or bogus Content-Length cannot bypass the size limit.
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
    /// Uses an atomic rename switch: at any moment current is a fully usable version (the old or the rolled-back one), so a
    /// crash never leaves a partial current (the old implementation deleted current first and then copied file by file, losing the version on a mid-way failure).
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

        // 1. If current exists, atomically move it to a temporary name: the old version is preserved and can be restored as-is on failure.
        if (Directory.Exists(current))
        {
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            Directory.Move(current, backup);
        }

        try
        {
            // 2. previous → current: a rename within the same filesystem is atomic.
            Directory.Move(previous, current);
        }
        catch
        {
            // 3. Switch failed: move the temporary name back to current, restoring the original state, then rethrow.
            if (Directory.Exists(backup) && !Directory.Exists(current))
            {
                Directory.Move(backup, current);
            }

            throw;
        }

        // 4. Success: clean up the temporary copy.
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

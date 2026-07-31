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

    /// <summary>Download the update package to the staging directory and verify SHA256.</summary>
    public async Task<DownloadResult> DownloadAsync(Uri packageUri, string expectedSha256, CancellationToken ct)
    {
        try
        {
            var staging = Path.Combine(rootDirectory, "updates", "staging");
            Directory.CreateDirectory(staging);
            var filePath = Path.Combine(staging, Path.GetFileName(packageUri.LocalPath));
            await using (var input = await httpClient.GetStreamAsync(packageUri, ct).ConfigureAwait(false))
            await using (var output = File.Create(filePath))
            {
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
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

    /// <summary>Rollback to the previous version in the previous directory.</summary>
    public Task RollbackAsync(CancellationToken ct)
    {
        var previous = Path.Combine(rootDirectory, "updates", "previous");
        var current = Path.Combine(rootDirectory, "updates", "current");
        if (!Directory.Exists(previous))
        {
            throw new InvalidOperationException("No previous version available for rollback.");
        }

        if (Directory.Exists(current))
        {
            Directory.Delete(current, recursive: true);
        }

        CopyDirectory(previous, current);
        return Task.CompletedTask;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal), overwrite: true);
        }
    }
}

/// <summary>Download result.</summary>
public sealed record DownloadResult(bool Success, string? FilePath, string? ErrorMessage);

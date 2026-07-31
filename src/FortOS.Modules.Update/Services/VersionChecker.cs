using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FortOS.Modules.Update.Services;

/// <summary>GitHub Releases version checker.</summary>
public sealed class VersionChecker
{
    private readonly HttpClient httpClient;

    /// <summary>Create the version checker.</summary>
    public VersionChecker(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FortOS-VersionChecker/1.0");
        }
    }

    /// <summary>Check the latest version; returns an unavailable result on network failure.</summary>
    public async Task<VersionCheckResult> CheckLatestAsync(string owner, string repo, Version currentVersion, CancellationToken ct)
    {
        Validate(owner);
        Validate(repo);
        try
        {
            var release = await httpClient.GetFromJsonAsync<GitHubRelease>($"https://api.github.com/repos/{owner}/{repo}/releases/latest", ct).ConfigureAwait(false);
            var tag = release?.TagName?.TrimStart('v', 'V');
            if (tag is null || !Version.TryParse(tag, out var latest))
            {
                return new VersionCheckResult(false, null, currentVersion, "Unable to parse the latest version.");
            }

            return new VersionCheckResult(latest > currentVersion, latest, currentVersion, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new VersionCheckResult(false, null, currentVersion, $"Version check network failure: {ex.Message}");
        }
    }

    private static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('/') || value.Contains('\n') || value.Contains('\r'))
        {
            throw new ArgumentException("GitHub owner/repo name is invalid.", nameof(value));
        }
    }

    private sealed record GitHubRelease([property: JsonPropertyName("tag_name")] string? TagName);
}

/// <summary>Version check result.</summary>
public sealed record VersionCheckResult(bool UpdateAvailable, Version? LatestVersion, Version CurrentVersion, string? ErrorMessage);

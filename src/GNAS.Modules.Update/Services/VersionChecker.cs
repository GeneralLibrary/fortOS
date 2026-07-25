using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GNAS.Modules.Update.Services;

/// <summary>GitHub Releases 版本检查器。</summary>
public sealed class VersionChecker
{
    private readonly HttpClient httpClient;

    /// <summary>创建版本检查器。</summary>
    public VersionChecker(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GNAS-VersionChecker/1.0");
        }
    }

    /// <summary>检查最新版本；网络失败时返回不可用结果。</summary>
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
                return new VersionCheckResult(false, null, currentVersion, "无法解析最新版本。");
            }

            return new VersionCheckResult(latest > currentVersion, latest, currentVersion, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new VersionCheckResult(false, null, currentVersion, $"版本检查网络失败: {ex.Message}");
        }
    }

    private static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('/') || value.Contains('\n') || value.Contains('\r'))
        {
            throw new ArgumentException("GitHub owner/repo 名称非法。", nameof(value));
        }
    }

    private sealed record GitHubRelease([property: JsonPropertyName("tag_name")] string? TagName);
}

/// <summary>版本检查结果。</summary>
public sealed record VersionCheckResult(bool UpdateAvailable, Version? LatestVersion, Version CurrentVersion, string? ErrorMessage);

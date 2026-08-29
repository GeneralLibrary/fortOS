using FortOS.Api.Configuration;

namespace FortOS.Tests.Integration;

/// <summary>
/// Pure unit tests for the config metadata registry — no host is started, so
/// these run on any platform (unlike the WebApplicationFactory-based tests).
/// </summary>
public sealed class ConfigMetaRegistryTests
{
    [Fact]
    public void Entries_HaveUniqueKeys_AndReferenceKnownCategories()
    {
        var categoryIds = ConfigMetaRegistry.Categories.Select(c => c.Id).ToHashSet();
        var keys = ConfigMetaRegistry.Entries.Select(e => e.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(ConfigMetaRegistry.Entries, e => Assert.Contains(e.Category, categoryIds));
    }

    [Fact]
    public void Entries_AreOrderedWithinEachCategory()
    {
        foreach (var group in ConfigMetaRegistry.Entries.GroupBy(e => e.Category))
        {
            var orders = group.Select(e => e.Order).ToArray();
            Assert.Equal(orders.OrderBy(o => o).ToArray(), orders);
        }
    }

    [Fact]
    public void WhitelistedEntries_AreNeverSensitive()
        => Assert.All(ConfigMetaRegistry.Entries, e => Assert.False(ConfigMetaRegistry.IsSensitive(e.Key)));

    [Fact]
    public void SelectEntries_HaveNonEmptyOptions()
    {
        foreach (var entry in ConfigMetaRegistry.Entries.Where(e => e.Type == ConfigEntryType.Select))
        {
            Assert.NotNull(entry.Options);
            Assert.NotEmpty(entry.Options);
        }
    }

    [Theory]
    // Namespace segment "token" must not hide a non-credential property.
    [InlineData("security:token:lifetime_minutes")]
    [InlineData("security:require_auth")]
    [InlineData("rateLimit:defaultPerMinute")]
    [InlineData("Serilog:MinimumLevel")]
    [InlineData("agent:public_host")]
    [InlineData("Kestrel:Endpoints:Http:Url")]
    public void IsSensitive_NonCredentialKeys_AreNotSensitive(string key)
        => Assert.False(ConfigMetaRegistry.IsSensitive(key));

    [Theory]
    [InlineData("security:token")]
    [InlineData("alerts:smtp:password")]
    [InlineData("alerts:smtp:pass")]
    [InlineData("agent:api_key")]
    [InlineData("security:oauth:client_secret")]
    [InlineData("alerts:webhook:token")]
    [InlineData("smtp:credential")]
    // Credential words must match in ANY segment, even with a generic last segment.
    [InlineData("secret:store:path")]
    [InlineData("store:secret:path")]
    [InlineData("credentials:db:endpoint")]
    public void IsSensitive_CredentialKeys_AreSensitive(string key)
        => Assert.True(ConfigMetaRegistry.IsSensitive(key));

    [Fact]
    public void TypeName_IsLowerCaseControlType()
        => Assert.All(ConfigMetaRegistry.Entries,
            e => Assert.Equal(e.Type.ToString().ToLowerInvariant(), e.TypeName));
}

public class P0P2ConfigMetaTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void NewFeatureKeys_AreWhitelistedAndNotSensitive()
    {
        // P0-1 AI / P0-3 Remote / P1-6 Docker 的新增配置键。
        Assert.Contains(ConfigMetaRegistry.Entries, e => e.Key == "ai:enabled" && e.Type == ConfigEntryType.Boolean);
        Assert.Contains(ConfigMetaRegistry.Entries, e => e.Key == "ai:endpoint");
        Assert.Contains(ConfigMetaRegistry.Entries, e => e.Key == "ai:model");
        Assert.Contains(ConfigMetaRegistry.Entries, e => e.Key == "remote:enabled" && e.Type == ConfigEntryType.Boolean);
        Assert.Contains(ConfigMetaRegistry.Entries, e => e.Key == "remote:tailscale_hostname");
        Assert.Contains(ConfigMetaRegistry.Entries, e => e.Key == "docker:registry_mirrors" && e.Type == ConfigEntryType.Text);

        // 凭据类键必须保持敏感(不进动态表单),防止密钥经配置页暴露。
        Assert.True(ConfigMetaRegistry.IsSensitive("ai:api_key"));
        Assert.True(ConfigMetaRegistry.IsSensitive("remote:tailscale_auth_key"));
    }
}

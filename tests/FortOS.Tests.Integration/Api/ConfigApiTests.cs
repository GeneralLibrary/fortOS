using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FortOS.Tests.Integration.Api;

/// <summary>
/// Integration tests for the configuration metadata API (GET /api/config/meta),
/// the flat config listing (GET /api/config) and its sensitive-key filtering.
/// </summary>
public sealed class ConfigApiTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfigMeta_ReturnsCategoriesAndWhitelistedEntries()
    {
        using var factory = await ConfigTestFactory.CreateAsync(nameof(ConfigMeta_ReturnsCategoriesAndWhitelistedEntries));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/config/meta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var categories = body.GetProperty("categories");
        var categoryIds = categories.EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        // Mirrors ConfigMetaRegistry.Categories — keep in sync when categories are added.
        Assert.Equal(["security", "access", "observability", "storage", "advanced"], categoryIds);

        var entries = body.GetProperty("entries");
        var entryMap = entries.EnumerateArray().ToDictionary(e => e.GetProperty("key").GetString()!);

        // Boolean control.
        var requireAuth = entryMap["security:require_auth"];
        Assert.Equal("boolean", requireAuth.GetProperty("type").GetString());
        Assert.Equal("security", requireAuth.GetProperty("category").GetString());

        // Number control with validation hints.
        var tokenLifetime = entryMap["security:token:lifetime_minutes"];
        Assert.Equal("number", tokenLifetime.GetProperty("type").GetString());
        Assert.Equal(1, tokenLifetime.GetProperty("min").GetDouble());
        Assert.Equal(10080, tokenLifetime.GetProperty("max").GetDouble());

        // Select control with options.
        var logLevel = entryMap["Serilog:MinimumLevel"];
        Assert.Equal("select", logLevel.GetProperty("type").GetString());
        var options = logLevel.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList();
        Assert.Contains("Information", options);
        Assert.Equal(6, options.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfigMeta_EveryEntryReferencesAnExistingCategory()
    {
        using var factory = await ConfigTestFactory.CreateAsync(nameof(ConfigMeta_EveryEntryReferencesAnExistingCategory));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/config/meta");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var categoryIds = body.GetProperty("categories").EnumerateArray()
            .Select(c => c.GetProperty("id").GetString()).ToHashSet();
        foreach (var entry in body.GetProperty("entries").EnumerateArray())
        {
            Assert.Contains(entry.GetProperty("category").GetString(), categoryIds);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetConfig_ExposesWhitelistedKeys_AndHidesSensitiveOnes()
    {
        using var factory = await ConfigTestFactory.CreateAsync(nameof(GetConfig_ExposesWhitelistedKeys_AndHidesSensitiveOnes));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Keys defined in appsettings.json that the settings page whitelists must be visible.
        Assert.True(body.TryGetProperty("security:require_auth", out _), "security:require_auth missing");
        Assert.True(body.TryGetProperty("security:token:lifetime_minutes", out _), "security:token:lifetime_minutes missing (namespace segment 'token' must not trigger sensitive filtering)");
        Assert.True(body.TryGetProperty("rateLimit:defaultPerMinute", out _), "rateLimit:defaultPerMinute missing");
        Assert.True(body.TryGetProperty("Serilog:MinimumLevel", out _), "Serilog:MinimumLevel missing");

        // No returned key may look like a credential.
        var sensitive = new[] { "password", "secret", "token", "key", "pass", "credential" };
        foreach (var property in body.EnumerateObject())
        {
            var last = property.Name.Split(':')[^1];
            Assert.DoesNotContain(sensitive, s => last.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PutSensitiveKey_ReturnsBadRequest()
    {
        using var factory = await ConfigTestFactory.CreateAsync(nameof(PutSensitiveKey_ReturnsBadRequest));
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/config/" + Uri.EscapeDataString("security:token"),
            new { value = "whatever" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class ConfigTestFactory : WebApplicationFactory<Program>
    {
        private readonly string? previousDataRoot;

        private ConfigTestFactory(string dataRoot)
        {
            DataRoot = dataRoot;
            previousDataRoot = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
            Environment.SetEnvironmentVariable("FortOS_DATA_ROOT", DataRoot);
        }

        public string DataRoot { get; }

        public static async Task<ConfigTestFactory> CreateAsync(string testName)
        {
            var root = Path.GetFullPath(Path.Combine("TestArtifacts", "Api", "Config", testName, Guid.CreateVersion7().ToString()));
            Directory.CreateDirectory(root);
            var factory = new ConfigTestFactory(root);
            await Task.Yield();
            return factory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("security:require_auth", "false");
            builder.UseSetting("dashboard:enabled", "false");
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
        }

        protected override void Dispose(bool disposing)
        {
            Environment.SetEnvironmentVariable("FortOS_DATA_ROOT", previousDataRoot);
            base.Dispose(disposing);
        }
    }
}

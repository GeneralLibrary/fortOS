using FortOS.Core;
using FortOS.Modules.Agent;
using FortOS.Tests.Integration.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
namespace FortOS.Tests.Integration.Agent;

/// <summary>
/// Serializes tests that mutate the process-wide <c>ASPNETCORE_ENVIRONMENT</c> so they
/// cannot race each other (xunit runs methods within a class in parallel by default).
/// </summary>
[CollectionDefinition("AgentEnvironment", DisableParallelization = true)]
public sealed class AgentEnvironmentCollection;

[Collection("AgentEnvironment")]
public sealed class AgentModuleTests : IDisposable
{
    private const string OpenClawMutableImage = "ghcr.io/openclaw/openclaw:latest";
    private const string OpenClawDigestImage = "ghcr.io/openclaw/openclaw@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly string? _previousEnvironment;

    public AgentModuleTests()
    {
        _previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
    }

    public void Dispose() => Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _previousEnvironment);

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_WithoutSwitch_RejectsMutableTag()
    {
        var config = new TestConfiguration();
        var ex = Assert.Throws<ArgumentException>(() => AgentModule.ValidateImage(OpenClawMutableImage, config));
        Assert.Contains("sha256", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_RequireDigestDisabled_AllowsMutableTag()
    {
        var config = new TestConfiguration().Set("agent:require_digest", "false");
        AgentModule.ValidateImage(OpenClawMutableImage, config); // must not throw
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_RequireDigestDisabled_StillRejectsUnsafeImage()
    {
        var config = new TestConfiguration().Set("agent:require_digest", "false");
        var ex = Assert.Throws<ArgumentException>(() => AgentModule.ValidateImage("evil;rm -rf /", config));
        Assert.Contains("unsafe characters", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_RequireDigestDisabled_StillEnforcesAllowlist()
    {
        var config = new TestConfiguration()
            .Set("agent:require_digest", "false")
            .SetArray("agent:allowed_images", "docker.io/library/nginx:1.27");
        var ex = Assert.Throws<ArgumentException>(() => AgentModule.ValidateImage(OpenClawMutableImage, config));
        Assert.Contains("allowlist", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_DigestPinned_IsAccepted()
    {
        var config = new TestConfiguration();
        AgentModule.ValidateImage(OpenClawDigestImage, config); // must not throw
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_UnparseableSwitchValue_FailsClosed()
    {
        var config = new TestConfiguration().Set("agent:require_digest", "maybe");
        var ex = Assert.Throws<ArgumentException>(() => AgentModule.ValidateImage(OpenClawMutableImage, config));
        Assert.Contains("sha256", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Development_WithoutSwitch_AllowsMutableTag()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        var config = new TestConfiguration();
        AgentModule.ValidateImage(OpenClawMutableImage, config); // must not throw
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_RequireDigestExplicitTrue_RejectsMutableTag()
    {
        var config = new TestConfiguration().Set("agent:require_digest", "true");
        var ex = Assert.Throws<ArgumentException>(() => AgentModule.ValidateImage(OpenClawMutableImage, config));
        Assert.Contains("sha256", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_RuntimeConfigDisablesDigestCheck()
    {
        // The dashboard writes the switch to the runtime IConfiguration (SQLite overrides);
        // it must control the check even when the file-backed IFortOSConfiguration is empty.
        var runtime = new InMemoryConfiguration(("agent:require_digest", "false"));
        AgentModule.ValidateImage(OpenClawMutableImage, new TestConfiguration(), runtime); // must not throw
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_RuntimeConfigTrue_RejectsMutableTag()
    {
        var runtime = new InMemoryConfiguration(("agent:require_digest", "true"));
        var ex = Assert.Throws<ArgumentException>(() => AgentModule.ValidateImage(OpenClawMutableImage, new TestConfiguration(), runtime));
        Assert.Contains("sha256", ex.Message);
    }
}

/// <summary>Minimal <see cref="IConfiguration"/> stub for tests (indexer-backed, no sections).</summary>
internal sealed class InMemoryConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public InMemoryConfiguration(params (string Key, string Value)[] values)
        => _values = values.ToDictionary(v => v.Key, v => (string?)v.Value, StringComparer.OrdinalIgnoreCase);

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();

    public IEnumerable<IConfigurationSection> GetChildren() => [];

    public IChangeToken GetReloadToken() => throw new NotSupportedException();
}

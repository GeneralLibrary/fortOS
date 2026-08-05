using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortOS.Core;
using FortOS.Modules.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortOS.Tests.Integration.Api;

/// <summary>
/// Integration tests for the RAID endpoints (GET/POST /api/disks/raids).
/// Error-path assertions only — creating a real array needs mdadm on the host.
/// </summary>
public sealed class RaidApiTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListRaids_ReturnsOk()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(ListRaids_ReturnsOk));
        // The factory removes hosted services (including StartupOrchestrator) so the test
        // host does not run background daemons; the module context must therefore be set up
        // explicitly, otherwise StorageModule.RequiredService throws "Module has not been
        // initialized." when the controller resolves IDiskManager.
        await factory.InitializeModulesAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/disks/raids");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RaidCapability_ReturnsToolAndAvailability()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(RaidCapability_ReturnsToolAndAvailability));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/disks/raid-capability");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mdadm", body.GetProperty("tool").GetString());
        // Availability depends on whether mdadm happens to be installed on the CI host.
        Assert.True(body.TryGetProperty("available", out var available) && available.ValueKind is JsonValueKind.True or JsonValueKind.False);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateRaid_WithoutConfirm_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(CreateRaid_WithoutConfirm_ReturnsBadRequest));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/raids",
            new { level = "Raid1", diskPaths = new[] { "/dev/sda", "/dev/sdb" }, confirm = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateRaid_UnknownLevel_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(CreateRaid_UnknownLevel_ReturnsBadRequest));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/raids",
            new { level = "Unknown", diskPaths = new[] { "/dev/sda" }, confirm = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateRaid_NoDisks_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(CreateRaid_NoDisks_ReturnsBadRequest));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/raids",
            new { level = "Raid1", diskPaths = Array.Empty<string>(), confirm = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateRaid_UnsafeDiskPath_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(CreateRaid_UnsafeDiskPath_ReturnsBadRequest));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/raids",
            new { level = "Raid1", diskPaths = new[] { "/tmp/not-a-disk" }, confirm = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateRaid_MissingDiskPaths_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(CreateRaid_MissingDiskPaths_ReturnsBadRequest));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/raids",
            new { level = "Raid1", confirm = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateRaid_TooFewDisksForLevel_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(CreateRaid_TooFewDisksForLevel_ReturnsBadRequest));
        using var client = factory.CreateClient();

        // RAID 5 requires at least 3 disks.
        var response = await client.PostAsJsonAsync("/api/disks/raids",
            new { level = "Raid5", diskPaths = new[] { "/dev/sda", "/dev/sdb" }, confirm = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeviceStatus_UnknownDevice_ReturnsExistsFalse()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(DeviceStatus_UnknownDevice_ReturnsExistsFalse));
        await factory.InitializeModulesAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/disks/device-status?path=/dev/md-unknown");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("/dev/md-unknown", body.GetProperty("path").GetString());
        Assert.False(body.GetProperty("exists").GetBoolean());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Format_UnsafeDevice_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(Format_UnsafeDevice_ReturnsBadRequest));
        await factory.InitializeModulesAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/format",
            new { device = "/tmp/not-a-device", fsType = "ext4" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Format_UnsupportedFileSystem_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(Format_UnsupportedFileSystem_ReturnsBadRequest));
        await factory.InitializeModulesAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/format",
            new { device = "/dev/sda", fsType = "fat32" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Mount_UnsafeDevice_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(Mount_UnsafeDevice_ReturnsBadRequest));
        await factory.InitializeModulesAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/mount",
            new { device = "/tmp/x", mountPoint = "/srv/nas/test", fsType = "ext4" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Unmount_EmptyMountPoint_ReturnsBadRequest()
    {
        using var factory = await RaidTestFactory.CreateAsync(nameof(Unmount_EmptyMountPoint_ReturnsBadRequest));
        await factory.InitializeModulesAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/disks/unmount", new { mountPoint = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class RaidTestFactory : WebApplicationFactory<Program>
    {
        private readonly string? previousDataRoot;

        private RaidTestFactory(string dataRoot)
        {
            DataRoot = dataRoot;
            previousDataRoot = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
            Environment.SetEnvironmentVariable("FortOS_DATA_ROOT", DataRoot);
        }

        public string DataRoot { get; }

        /// <summary>
        /// Initializes just the storage module context (equivalent of what
        /// StartupOrchestrator/ModuleHost do in a production host). Required by endpoints
        /// that resolve services through a module context; the factory deliberately keeps
        /// hosted services removed. Only StorageModule is initialized — the other built-in
        /// modules (e.g. ShareModule) touch privileged host paths like /etc/samba that a
        /// non-root CI runner cannot write, and they are not needed for the RAID endpoints.
        /// </summary>
        public async Task InitializeModulesAsync()
        {
            var storage = Services.GetRequiredService<StorageModule>();
            await storage.InitializeAsync(new ModuleContext
            {
                Services = Services,
                EventBus = Services.GetRequiredService<IEventBus>(),
                LoggerFactory = Services.GetRequiredService<ILoggerFactory>(),
                DataDirectory = Path.Combine(DataRoot, "modules", "loaded", storage.ModuleId),
            }, CancellationToken.None);
        }

        public static async Task<RaidTestFactory> CreateAsync(string testName)
        {
            var root = Path.GetFullPath(Path.Combine("TestArtifacts", "Api", "Raid", testName, Guid.CreateVersion7().ToString()));
            Directory.CreateDirectory(root);
            var factory = new RaidTestFactory(root);
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

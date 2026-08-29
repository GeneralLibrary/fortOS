using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortOS.Core;
using FortOS.Security.KeyStore;
using FortOS.Security.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FortOS.Tests.Integration.Api;

public sealed class ApiGatewayTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Health_ReturnsOk()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(Health_ReturnsOk));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AnyResponse_IncludesSecurityHeaders()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(AnyResponse_IncludesSecurityHeaders));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("camera=(), microphone=(), geolocation=()", response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(ProtectedEndpoint_WithoutToken_ReturnsUnauthorized), createUser: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/disks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FilesEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(FilesEndpoint_WithoutToken_ReturnsUnauthorized), createUser: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/files?path=/srv/nas");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FilesEndpoint_WithoutToken_WhenAuthDisabled_Succeeds()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(FilesEndpoint_WithoutToken_WhenAuthDisabled_Succeeds), createUser: false, requireAuth: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/files?path={Uri.EscapeDataString(factory.DataRoot)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FilesEndpoint_WithAdminToken_Succeeds()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(FilesEndpoint_WithAdminToken_Succeeds), createUser: true);
        using var client = factory.CreateClient();

        var token = await LoginAsync(client, "admin", "Admin12345");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files?path={Uri.EscapeDataString(factory.DataRoot)}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected OK but got {response.StatusCode}: {body}");
        }
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FilesEndpoint_WithFileReadCapability_NonAdminUser_Succeeds()
    {
        // A non-admin user granted the fine-grained files:file:read capability must be able to list files.
        // Regression for the CapabilityConvention defaulting unannotated actions to "admin:**",
        // which previously short-circuited every /api/files request to 403 before the controller's
        // own files:file:* check could run.
        using var factory = await ApiTestFactory.CreateAsync(nameof(FilesEndpoint_WithFileReadCapability_NonAdminUser_Succeeds),
            async f =>
            {
                // First user becomes admin; create a regular user and grant files:file:read via a custom role entry.
                var database = new DatabaseProvider(f.DataRoot);
                var tokens = new NasTokenManager(new NasKeyStore(), database);
                var identity = new IdentityService(database, tokens);
                var admin = await identity.CreateLocalUserAsync("admin", "Admin12345", "Admin", "admin@example.invalid", CancellationToken.None);
                Assert.True(admin.Success, admin.ErrorMessage);
                var created = await identity.CreateLocalUserAsync("bob", "Bob12345", "Bob", "bob@example.invalid", CancellationToken.None);
                Assert.True(created.Success, created.ErrorMessage);
                await database.InitializeAsync(CancellationToken.None);
                await using var connection = await database.GetConnectionAsync(CancellationToken.None);
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE users SET roles_json = $roles WHERE username = $username;";
                command.Parameters.AddWithValue("$roles", """["user","files:file:read"]""");
                command.Parameters.AddWithValue("$username", "bob");
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            });
        using var client = factory.CreateClient();

        var token = await LoginAsync(client, "bob", "Bob12345");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files?path={Uri.EscapeDataString(factory.DataRoot)}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected OK but got {response.StatusCode}: {body}");
        }
        var body2 = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(JsonValueKind.Array, body2.ValueKind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FilesWrite_WithAdminToken_Succeeds()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(FilesWrite_WithAdminToken_Succeeds), createUser: true);
        using var client = factory.CreateClient();

        var token = await LoginAsync(client, "admin", "Admin12345");
        var path = Path.Combine(factory.DataRoot, "api-test", "hello.txt");
        using var write = new HttpRequestMessage(HttpMethod.Post, "/api/files/write")
        {
            Content = JsonContent.Create(new { path, content = "hello from api", encoding = "text", overwrite = true }),
        };
        write.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var writeResponse = await client.SendAsync(write);
        Assert.Equal(HttpStatusCode.OK, writeResponse.StatusCode);

        using var read = new HttpRequestMessage(HttpMethod.Get, $"/api/files/content?path={Uri.EscapeDataString(path)}");
        read.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var readResponse = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var content = await readResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("hello from api", content.GetProperty("content").GetString());

        using var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/files?path={Uri.EscapeDataString(path)}&hard=true");
        del.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var delResponse = await client.SendAsync(del);
        Assert.Equal(HttpStatusCode.OK, delResponse.StatusCode);
        Assert.False(File.Exists(path));
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("token").GetString() ?? throw new Xunit.Sdk.XunitException("Login did not return a token.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BackupEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(BackupEndpoint_WithoutToken_ReturnsUnauthorized), createUser: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/backup/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Login_RapidRequests_AreRateLimited()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(Login_RapidRequests_AreRateLimited), createUser: true);
        using var client = factory.CreateClient();
        HttpResponseMessage? response = null;

        for (var i = 0; i < 6; i++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "wrong-password" });
        }

        using (response)
        {
            Assert.NotNull(response);
            Assert.Equal((HttpStatusCode)429, response!.StatusCode);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Register_Bootstrap_CreatesFirstAdmin_ThenRequiresAuth()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(Register_Bootstrap_CreatesFirstAdmin_ThenRequiresAuth));
        using var client = factory.CreateClient();

        // Bootstrap phase: anonymous registration allowed when no users exist
        var first = await client.PostAsJsonAsync("/api/auth/register", new { username = "admin", password = "Admin12345", displayName = "Admin" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // After user exists: anonymous registration rejected
        var anonymous = await client.PostAsJsonAsync("/api/auth/register", new { username = "bob", password = "Password1", displayName = "Bob" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // First user automatically gets admin role, can create more users after login
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin12345" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var token = body.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { username = "bob", password = "Password1", displayName = "Bob" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var second = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Register_NonAdminToken_ReturnsForbidden()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(Register_NonAdminToken_ReturnsForbidden), createUser: true);
        using var client = factory.CreateClient();

        // createUser creates admin as first user; then creates a regular user and attempts registration with their token
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin12345" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var adminToken = (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString();

        using var createBob = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { username = "bob", password = "Password1" }),
        };
        createBob.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(createBob)).StatusCode);

        var bobLogin = await client.PostAsJsonAsync("/api/auth/login", new { username = "bob", password = "Password1" });
        var bobToken = (await bobLogin.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString();

        using var forbidden = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { username = "carol", password = "Password1" }),
        };
        forbidden.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bobToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(forbidden)).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Recovery_RsyncMode_WithToken_ReturnsExecutionResult()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(Recovery_RsyncMode_WithToken_ReturnsExecutionResult), createUser: true);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin12345" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/recovery/start")
        {
            Content = JsonContent.Create(new { target = Path.Combine(factory.DataRoot, "restore-target"), source = Path.Combine(factory.DataRoot, "restore-source"), mode = "rsync", dryRun = true }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("rsync", body.GetProperty("mode").GetString());
        Assert.True(body.TryGetProperty("success", out _));
        Assert.True(body.TryGetProperty("exitCode", out _));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Recovery_SnapshotMode_WithoutSnapshotId_ReturnsBadRequest()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(Recovery_SnapshotMode_WithoutSnapshotId_ReturnsBadRequest), createUser: true);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin12345" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/recovery/start")
        {
            Content = JsonContent.Create(new { target = Path.Combine(factory.DataRoot, "restore-target"), mode = "snapshot" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class ApiTestFactory : WebApplicationFactory<Program>
    {
        private readonly string? previousDataRoot;
        private readonly bool requireAuth;

        private ApiTestFactory(string dataRoot, bool requireAuth)
        {
            DataRoot = dataRoot;
            this.requireAuth = requireAuth;
            previousDataRoot = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
            Environment.SetEnvironmentVariable("FortOS_DATA_ROOT", DataRoot);
        }

        public string DataRoot { get; }

        public static Task<ApiTestFactory> CreateAsync(string testName, bool createUser = false, bool requireAuth = true)
            => CreateAsync(testName, async factory =>
            {
                if (createUser)
                {
                    var database = new DatabaseProvider(factory.DataRoot);
                    var tokens = new NasTokenManager(new NasKeyStore(), database);
                    var identity = new IdentityService(database, tokens);
                    var created = await identity.CreateLocalUserAsync("admin", "Admin12345", "Admin", "admin@example.invalid", CancellationToken.None);
                    Assert.True(created.Success, created.ErrorMessage);
                }
            }, requireAuth);

        public static async Task<ApiTestFactory> CreateAsync(string testName, Func<ApiTestFactory, Task> initialize, bool requireAuth = true)
        {
            var root = Path.GetFullPath(Path.Combine("TestArtifacts", "Api", testName, Guid.CreateVersion7().ToString()));
            Directory.CreateDirectory(root);
            var factory = new ApiTestFactory(root, requireAuth);
            await initialize(factory).ConfigureAwait(false);
            return factory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("security:require_auth", requireAuth ? "true" : "false");
            builder.UseSetting("rateLimit:loginPerMinute", "5");
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

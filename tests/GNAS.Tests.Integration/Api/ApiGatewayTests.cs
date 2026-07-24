using System.Net;
using System.Net.Http.Json;
using GNAS.Core;
using GNAS.Security.KeyStore;
using GNAS.Security.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GNAS.Tests.Integration.Api;

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
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(ProtectedEndpoint_WithoutToken_ReturnsUnauthorized), createUser: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/disks");

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

    private sealed class ApiTestFactory : WebApplicationFactory<Program>
    {
        private readonly string? previousDataRoot;

        private ApiTestFactory(string dataRoot)
        {
            DataRoot = dataRoot;
            previousDataRoot = Environment.GetEnvironmentVariable("GNAS_DATA_ROOT");
            Environment.SetEnvironmentVariable("GNAS_DATA_ROOT", DataRoot);
        }

        public string DataRoot { get; }

        public static async Task<ApiTestFactory> CreateAsync(string testName, bool createUser = false)
        {
            var root = Path.GetFullPath(Path.Combine("TestArtifacts", "Api", testName, Guid.CreateVersion7().ToString()));
            Directory.CreateDirectory(root);
            var factory = new ApiTestFactory(root);
            if (createUser)
            {
                var database = new DatabaseProvider(root);
                var tokens = new NasTokenManager(new NasKeyStore(), database);
                var identity = new IdentityService(database, tokens);
                var created = await identity.CreateLocalUserAsync("admin", "Admin12345", "Admin", "admin@example.invalid", CancellationToken.None);
                Assert.True(created.Success, created.ErrorMessage);
            }

            return factory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("security:require_auth", "true");
            builder.UseSetting("rateLimit:loginPerMinute", "5");
            builder.UseSetting("dashboard:enabled", "false");
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
        }

        protected override void Dispose(bool disposing)
        {
            Environment.SetEnvironmentVariable("GNAS_DATA_ROOT", previousDataRoot);
            base.Dispose(disposing);
        }
    }
}

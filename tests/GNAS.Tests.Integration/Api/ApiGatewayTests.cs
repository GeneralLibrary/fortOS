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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Register_Bootstrap_CreatesFirstAdmin_ThenRequiresAuth()
    {
        using var factory = await ApiTestFactory.CreateAsync(nameof(Register_Bootstrap_CreatesFirstAdmin_ThenRequiresAuth));
        using var client = factory.CreateClient();

        // bootstrap 阶段：无用户时允许匿名注册首个账户
        var first = await client.PostAsJsonAsync("/api/auth/register", new { username = "admin", password = "Admin12345", displayName = "Admin" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // 存在用户后：匿名注册被拒绝
        var anonymous = await client.PostAsJsonAsync("/api/auth/register", new { username = "bob", password = "Password1", displayName = "Bob" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // 首个用户自动获得 admin 角色，登录后可以继续创建用户
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

        // createUser 创建的首个用户是 admin；再创建一个普通用户并用其令牌尝试注册
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

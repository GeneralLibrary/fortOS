using FortOS.Agent;
using FortOS.Core;
using FortOS.Modules.Agent;
using FortOS.Modules.Backup;
using FortOS.Modules.Host;
using FortOS.Modules.Network;
using FortOS.Modules.Share;
using FortOS.Modules.Storage;
using FortOS.Modules.Update;
using FortOS.Observability;
using FortOS.Platform;
using FortOS.Security;
using FortOS.Security.Models;
using FortOS.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortOS.Tests.Integration.E2E;

public sealed class FullCycleE2ETests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task InProcess_CreateUserAuthorizeRegisterPublishCleanup_Completes()
    {
        var dataRoot = CreateDataRoot(nameof(InProcess_CreateUserAuthorizeRegisterPublishCleanup_Completes));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["security:require_auth"] = "true",
            ["dashboard:enabled"] = "false"
        }).Build();
        var previousDataRoot = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
        Environment.SetEnvironmentVariable("FortOS_DATA_ROOT", dataRoot);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddFortOSCore(dataRoot);
            services.AddFortOSPlatform();
            services.AddFortOSSecurity(configuration);
            services.AddFortOSServiceBus();
            services.AddFortOSModuleHost();
            services.AddSingleton<StorageModule>();
            services.AddSingleton<ShareModule>();
            services.AddSingleton<NetworkModule>();
            services.AddSingleton<AgentModule>();
            services.AddSingleton<BackupModule>();
            services.AddSingleton<UpdateModule>();
            services.AddSingleton<INasModule>(sp => sp.GetRequiredService<StorageModule>());
            services.AddSingleton<INasModule>(sp => sp.GetRequiredService<ShareModule>());
            services.AddSingleton<INasModule>(sp => sp.GetRequiredService<NetworkModule>());
            services.AddSingleton<INasModule>(sp => sp.GetRequiredService<AgentModule>());
            services.AddSingleton<INasModule>(sp => sp.GetRequiredService<BackupModule>());
            services.AddSingleton<INasModule>(sp => sp.GetRequiredService<UpdateModule>());
            services.AddFortOSAgent();
            services.AddFortOSObservability(configuration);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var identity = provider.GetRequiredService<IIdentityService>();
            var created = await identity.CreateLocalUserAsync("cycle", "Cycle12345", "Cycle User", "cycle@example.invalid", CancellationToken.None);
            Assert.True(created.Success, created.ErrorMessage);

            var login = await identity.AuthenticateLocalAsync("cycle", "Cycle12345", CancellationToken.None);
            Assert.True(login.Success, login.ErrorMessage);
            Assert.False(string.IsNullOrWhiteSpace(login.NasToken));

            var permission = await provider.GetRequiredService<IPermissionEngine>()
                .CheckPermissionAsync(login.NasToken!, NAbilityConstants.DataInternal, "/data/internal", NasDataLevel.Internal, CancellationToken.None);
            Assert.True(permission.Granted, permission.DenyReason);

            var registry = provider.GetRequiredService<IServiceRegistry>();
            var service = new ServiceDefinition
            {
                ServiceId = "e2e-service",
                DisplayName = "E2E Service",
                Type = ServiceType.Native,
                Startup = ServiceStartup.Manual,
                RestartPolicy = RestartPolicy.Never,
                Executable = "/bin/true"
            };
            await registry.RegisterAsync(service, CancellationToken.None);
            Assert.Equal("e2e-service", (await registry.GetAsync("e2e-service", CancellationToken.None))!.ServiceId);

            var received = new TaskCompletionSource<EventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = provider.GetRequiredService<IEventBus>().Subscribe("e2e.*", (envelope, _) =>
            {
                received.TrySetResult(envelope);
                return Task.CompletedTask;
            });
            await provider.GetRequiredService<IEventBus>().PublishAsync("e2e.completed", "e2e.completed", "{}", CancellationToken.None);
            Assert.Equal("e2e.completed", (await received.Task.WaitAsync(TimeSpan.FromSeconds(2))).Topic);

            await registry.UnregisterAsync("e2e-service", CancellationToken.None);
            var deleted = await identity.DeleteLocalUserAsync("cycle", CancellationToken.None);
            Assert.True(deleted.Success, deleted.ErrorMessage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FortOS_DATA_ROOT", previousDataRoot);
        }
    }

    private static string CreateDataRoot(string name)
    {
        var path = Path.GetFullPath(Path.Combine("TestArtifacts", "E2E", name, Guid.CreateVersion7().ToString()));
        Directory.CreateDirectory(path);
        return path;
    }
}

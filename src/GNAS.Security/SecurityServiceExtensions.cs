using GNAS.Core;
using GNAS.Security.KeyStore;
using GNAS.Security.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Security;

/// <summary>
/// GNAS Security dependency injection extensions.
/// </summary>
public static class SecurityServiceExtensions
{
    /// <summary>
    /// Registers security and identity layer services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddGnasSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<INasKeyStore, NasKeyStore>();
        services.AddSingleton<IMasterKeyRotationService>(sp => (NasKeyStore)sp.GetRequiredService<INasKeyStore>());
        services.AddSingleton<ITokenManager, NasTokenManager>();
        services.AddSingleton<IIdentityService, IdentityService>();
        services.AddSingleton<IPermissionEngine, PermissionEngine>();
        services.AddTransient<TokenBuilder>();
        return services;
    }
}

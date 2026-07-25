using GNAS.Core;
using GNAS.Security.KeyStore;
using GNAS.Security.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Security;

/// <summary>
/// GNAS Security 渚濊禆娉ㄥ叆鎵╁睍銆?
/// </summary>
public static class SecurityServiceExtensions
{
    /// <summary>
    /// 娉ㄥ唽瀹夊叏涓庤韩浠借璇佸眰鏈嶅姟銆?
    /// </summary>
    /// <param name="services">鏈嶅姟闆嗗悎銆?/param>
    /// <param name="configuration">搴旂敤閰嶇疆銆?/param>
    /// <returns>鏈嶅姟闆嗗悎銆?/returns>
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

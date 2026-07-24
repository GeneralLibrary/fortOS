using GNAS.Core;
using GNAS.Security.KeyStore;
using GNAS.Security.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Security;

/// <summary>
/// GNAS Security 依赖注入扩展。
/// </summary>
public static class SecurityServiceExtensions
{
    /// <summary>
    /// 注册安全与身份认证层服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddGnasSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<INasKeyStore, NasKeyStore>();
        services.AddSingleton<ITokenManager, NasTokenManager>();
        services.AddSingleton<IIdentityService, IdentityService>();
        services.AddSingleton<IPermissionEngine, PermissionEngine>();
        services.AddTransient<TokenBuilder>();
        return services;
    }
}

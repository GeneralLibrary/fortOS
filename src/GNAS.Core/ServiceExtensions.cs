using Microsoft.Extensions.DependencyInjection;

namespace GNAS.Core;

/// <summary>
/// GNAS Core 依赖注入扩展。
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// 注册 GNAS Core 基础服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="dataRoot">数据根目录。</param>
    /// <param name="configPath">配置文件路径。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddGnasCore(this IServiceCollection services, string? dataRoot = null, string? configPath = null)
    {
        services.AddSingleton<IDatabaseProvider>(_ => new DatabaseProvider(dataRoot));
        services.AddSingleton<IGnasConfiguration>(_ => new GnasConfiguration(configPath));
        return services;
    }
}

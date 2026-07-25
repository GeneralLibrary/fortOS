namespace GNAS.Core;

/// <summary>权限决策引擎接口。</summary>
public interface IPermissionEngine
{
    /// <summary>检查权限。</summary>
    Task<PermissionResult> CheckPermissionAsync(string token, string requiredCapability, string? resourcePath, NasDataLevel dataLevel, CancellationToken ct);
}

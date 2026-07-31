namespace FortOS.Core;

/// <summary>Permission decision engine interface.</summary>
public interface IPermissionEngine
{
    /// <summary>Check permissions.</summary>
    Task<PermissionResult> CheckPermissionAsync(string token, string requiredCapability, string? resourcePath, NasDataLevel dataLevel, CancellationToken ct);
}

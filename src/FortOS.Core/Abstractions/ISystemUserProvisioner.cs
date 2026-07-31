namespace FortOS.Core;

/// <summary>
/// System user provisioning abstraction.
/// Used to bridge accounts to the underlying system (e.g., Linux system users + Samba user database)
/// when FortOS internal users (SQLite) are created or deleted, so that sharing protocol clients
/// can authenticate with the same credentials.
/// Implementations must be best-effort: provisioning failure should not block the FortOS user lifecycle.
/// </summary>
public interface ISystemUserProvisioner
{
    /// <summary>Provision a system-side account for the given username and plaintext password (idempotent).</summary>
    Task ProvisionAsync(string username, string password, CancellationToken ct);
    /// <summary>Remove the system-side account for the given username (idempotent).</summary>
    Task RemoveAsync(string username, CancellationToken ct);
}

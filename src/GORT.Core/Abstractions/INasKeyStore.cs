namespace GORT.Core;

/// <summary>NAS key store interface.</summary>
public interface INasKeyStore
{
    /// <summary>Get or create a signing key.</summary>
    Task<byte[]> GetOrCreateSigningKeyAsync(string keyId, CancellationToken ct);
    /// <summary>Sign data.</summary>
    Task<byte[]> SignDataAsync(string keyId, byte[] data, CancellationToken ct);
    /// <summary>Get or create an audit chain key.</summary>
    Task<byte[]> GetOrCreateChainKeyAsync(CancellationToken ct);
    /// <summary>Compute HMAC.</summary>
    Task<byte[]> ComputeHmacAsync(string keyId, byte[] data, CancellationToken ct);
    /// <summary>Encrypt data.</summary>
    Task<byte[]> EncryptAsync(string keyId, byte[] plaintext, CancellationToken ct);
    /// <summary>Decrypt data.</summary>
    Task<byte[]> DecryptAsync(string keyId, byte[] ciphertext, CancellationToken ct);
    /// <summary>Store a secret.</summary>
    Task StoreSecretAsync(string name, byte[] value, CancellationToken ct);
    /// <summary>Read a secret.</summary>
    Task<byte[]?> GetSecretAsync(string name, CancellationToken ct);
    /// <summary>Delete a secret.</summary>
    Task DeleteSecretAsync(string name, CancellationToken ct);
    /// <summary>Generate an agent secret.</summary>
    Task<string> GenerateAgentSecretAsync(string agentId, CancellationToken ct);
    /// <summary>Read an agent secret.</summary>
    Task<string?> GetAgentSecretAsync(string agentId, CancellationToken ct);
}
